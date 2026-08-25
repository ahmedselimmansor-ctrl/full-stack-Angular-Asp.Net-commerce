using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sama.Ordering.Domain;
using Sama.Ordering.Integration;
using Sama.Ordering.Persistence;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Infrastructure.Idempotency;
using Sama.Shared.Infrastructure.RateLimiting;
using Sama.Shared.Kernel;
using Sama.Shared.Kernel.Domain;
using Sama.Shared.Kernel.Paging;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.AccessControl;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Permissions;
using Sama.Shared.Security.Tenancy;

namespace Sama.Ordering.Features;

public sealed record CheckoutRequest(ShippingAddressRequest ShippingAddress);

public sealed record ShippingAddressRequest(string Line1, string City, string Postcode, string Country);

public sealed record OrderSummary(
    Guid Id, string OrderNumber, string Status, decimal Total, string Currency,
    int LineCount, DateTimeOffset CreatedAt);

public sealed record OrderDetail(
    Guid Id, string OrderNumber, string Status, decimal Total, string Currency,
    IReadOnlyList<OrderLineDto> Lines, ShippingAddressRequest ShippingAddress,
    string? CancellationReason, DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt);

public sealed record OrderLineDto(Guid ProductId, string Sku, string Name, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record CancelOrderRequest(string? Reason);

public sealed class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(r => r.ShippingAddress).NotNull();
        RuleFor(r => r.ShippingAddress.Line1).NotEmpty().MaximumLength(300);
        RuleFor(r => r.ShippingAddress.City).NotEmpty().MaximumLength(150);
        RuleFor(r => r.ShippingAddress.Postcode).NotEmpty().MaximumLength(32);
        RuleFor(r => r.ShippingAddress.Country)
            .NotEmpty().Length(2)
            .WithMessage("Country must be a 2-letter ISO 3166-1 code.");
    }
}

public static class OrderEndpoints
{
    /// <summary>How long a completed checkout's response is replayable for a repeated key.</summary>
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/v1/orders").WithTags("Orders");

        group.MapPost("/", CheckoutAsync)
            .RequirePermission(SamaPermissions.Ordering.Create)
            .RateLimit("checkout")
            .WithSummary("Places an order from the caller's basket. Requires an Idempotency-Key header.");

        group.MapGet("/", ListAsync)
            .RequireAnyPermission(SamaPermissions.Ordering.ReadOwn, SamaPermissions.Ordering.ReadAny)
            .RateLimit("read")
            .WithSummary("Lists orders visible to the caller.");

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAnyPermission(SamaPermissions.Ordering.ReadOwn, SamaPermissions.Ordering.ReadAny)
            .RateLimit("read")
            .WithSummary("Returns one order.");

        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .RequireAnyPermission(SamaPermissions.Ordering.CancelOwn, SamaPermissions.Ordering.CancelAny)
            .RateLimit("write")
            .WithSummary("Cancels an order and releases any held stock.");

        return routes;
    }

    private static async Task<IResult> CheckoutAsync(
        CheckoutRequest request,
        HttpContext context,
        IValidator<CheckoutRequest> validator,
        OrderingDbContext database,
        IBasketClient basketClient,
        ICatalogClient catalogClient,
        IIdempotencyStore idempotency,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IClock clock,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        // Checkout is the one operation where a retry must not create a second order. The key
        // is mandatory rather than optional: a client that omits it has no safe retry story,
        // and silently allowing that turns a dropped response into a double charge.
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Error.Validation(
                    "ordering.idempotencyKey.missing",
                    "An Idempotency-Key header is required so this request can be retried safely.")
                .ToProblem();
        }

        var userId = currentUser.RequireUserId();
        var tenantId = tenant.RequireTenantId();

        // Scope the key to the caller, so one user's key cannot collide with (or replay)
        // another's, and fingerprint the body to catch a key reused for different content.
        var scopedKey = $"{tenantId:N}:{userId:N}:{idempotencyKey}";
        var fingerprint = Fingerprint(request);

        var claim = await idempotency.BeginAsync(scopedKey, fingerprint, IdempotencyRetention, cancellationToken);

        switch (claim.State)
        {
            case IdempotencyState.Completed:
                context.Response.StatusCode = claim.StatusCode ?? StatusCodes.Status202Accepted;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(claim.Body ?? "{}", cancellationToken);
                return Results.Empty;

            case IdempotencyState.InProgress:
                return Error.Conflict(
                        "ordering.checkout.inProgress",
                        "This checkout is already being processed. Wait a moment before retrying.")
                    .ToProblem();

            case IdempotencyState.PayloadMismatch:
                return Error.Conflict(
                        "ordering.idempotencyKey.reused",
                        "That Idempotency-Key was already used for a different request.")
                    .ToProblem();

            default:
                break;
        }

        try
        {
            var basket = await basketClient.GetAsync(cancellationToken);
            if (basket.IsFailure)
            {
                await idempotency.AbandonAsync(scopedKey, cancellationToken);
                return basket.Error.ToProblem();
            }

            if (basket.Value.Items.Count == 0)
            {
                await idempotency.AbandonAsync(scopedKey, cancellationToken);
                return Error.Validation("ordering.basket.empty", "Your basket is empty.").ToProblem();
            }

            // Re-price every line against the catalogue. The basket's prices were captured
            // when items were added and may be stale or, if a client was tampered with,
            // fabricated. The catalogue is the only authority on what something costs.
            var productIds = basket.Value.Items.Select(i => i.ProductId).Distinct().ToArray();
            var pricing = await catalogClient.LookupAsync(productIds, cancellationToken);
            if (pricing.IsFailure)
            {
                await idempotency.AbandonAsync(scopedKey, cancellationToken);
                return pricing.Error.ToProblem();
            }

            var priceByProduct = pricing.Value.ToDictionary(p => p.Id);
            var lines = new List<OrderLine>(basket.Value.Items.Count);
            var orderId = SamaId.New();

            foreach (var item in basket.Value.Items)
            {
                if (!priceByProduct.TryGetValue(item.ProductId, out var product) || !product.IsPurchasable)
                {
                    await idempotency.AbandonAsync(scopedKey, cancellationToken);
                    return Error.Conflict(
                            "ordering.line.unavailable",
                            $"'{item.Name}' is no longer available. Remove it from your basket to continue.")
                        .ToProblem();
                }

                lines.Add(new OrderLine(
                    orderId, product.Id, product.Sku, product.Name, item.Quantity, product.Price, product.Currency));
            }

            var shipping = new ShippingAddress(
                request.ShippingAddress.Line1.Trim(),
                request.ShippingAddress.City.Trim(),
                request.ShippingAddress.Postcode.Trim(),
                request.ShippingAddress.Country.Trim().ToUpperInvariant());

            var order = Order.Place(tenantId, userId, lines, shipping, clock.UtcNow);
            if (order.IsFailure)
            {
                await idempotency.AbandonAsync(scopedKey, cancellationToken);
                return order.Error.ToProblem();
            }

            database.Orders.Add(order.Value);

            // One transaction writes the order and the order.placed outbox row, so the saga
            // can never be started for an order that failed to persist.
            await database.SaveChangesAsync(cancellationToken);

            var body = JsonSerializer.Serialize(new
            {
                id = order.Value.Id,
                orderNumber = order.Value.OrderNumber,
                status = order.Value.Status.ToString(),
                total = order.Value.Total,
                currency = order.Value.Currency,
            }, SamaJson.Default);

            await idempotency.CompleteAsync(
                scopedKey, StatusCodes.Status202Accepted, body, IdempotencyRetention, cancellationToken);

            // 202, not 201: the order exists but the saga is still running, so its final state
            // is not yet decided. The client polls or waits for a notification.
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            context.Response.ContentType = "application/json";
            context.Response.Headers.Location = $"/api/v1/orders/{order.Value.Id}";
            await context.Response.WriteAsync(body, cancellationToken);

            return Results.Empty;
        }
        catch
        {
            // Release the claim so the client's retry is allowed to proceed rather than being
            // told an operation is in progress for the next 24 hours.
            await idempotency.AbandonAsync(scopedKey, CancellationToken.None);
            throw;
        }
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        OrderingDbContext database,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        context.Response.SetNoStore();

        var request = new PageRequest(page, pageSize);

        // Row-level access control: a customer sees their own orders, support sees every order
        // in the tenant. The narrowing happens inside the SQL, so the database never returns
        // rows the caller may not see.
        var scope = currentUser.ScopeFor(SamaPermissions.Ordering.ReadOwn, SamaPermissions.Ordering.ReadAny);

        var query = database.Orders
            .AsNoTracking()
            .WithinScope(scope, currentUser.UserId);

        var total = await query.LongCountAsync(cancellationToken);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(o => new OrderSummary(
                o.Id, o.OrderNumber, o.Status.ToString(), o.Total, o.Currency, o.Lines.Count, o.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedResult<OrderSummary>(orders, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        HttpContext context,
        OrderingDbContext database,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        var scope = currentUser.ScopeFor(SamaPermissions.Ordering.ReadOwn, SamaPermissions.Ordering.ReadAny);

        var order = await database.Orders
            .AsNoTracking()
            .WithinScope(scope, currentUser.UserId)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        // 404 rather than 403 for someone else's order: a 403 would confirm the order exists,
        // which turns sequential id probing into an order-count oracle.
        return order is null
            ? Error.NotFound("ordering.order.notFound", "No such order.").ToProblem()
            : Results.Ok(ToDetail(order));
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        CancelOrderRequest request,
        HttpContext context,
        OrderingDbContext database,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        var scope = currentUser.ScopeFor(SamaPermissions.Ordering.CancelOwn, SamaPermissions.Ordering.CancelAny);

        var order = await database.Orders
            .WithinScope(scope, currentUser.UserId)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            return Error.NotFound("ordering.order.notFound", "No such order.").ToProblem();
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Cancelled by the customer."
            : request.Reason.Trim();

        var result = order.Cancel(reason[..Math.Min(reason.Length, 300)], clock.UtcNow);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        // Cancelling raises order.cancelled, which inventory consumes to release the hold.
        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDetail(order));
    }

    private static OrderDetail ToDetail(Order order)
        => new(
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.Total,
            order.Currency,
            [.. order.Lines.Select(l => new OrderLineDto(
                l.ProductId, l.Sku, l.Name, l.Quantity, l.UnitPrice, l.LineTotal().Amount))],
            new ShippingAddressRequest(
                order.ShippingLine1, order.ShippingCity, order.ShippingPostcode, order.ShippingCountry),
            order.CancellationReason,
            order.CreatedAt,
            order.ConfirmedAt);

    private static string Fingerprint(CheckoutRequest request)
        => Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, SamaJson.Default))))[..32];

    private static IResult ToValidationProblem(FluentValidation.Results.ValidationResult validation)
    {
        var details = validation.Errors
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray(), StringComparer.Ordinal);

        return Error.Validation("request.invalid", "One or more fields are invalid.", details).ToProblem();
    }
}
