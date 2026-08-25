using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Sama.Basket.Domain;
using Sama.Basket.Integration;
using Sama.Basket.Storage;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Infrastructure.RateLimiting;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Permissions;
using Sama.Shared.Security.Tenancy;

namespace Sama.Basket.Features;

public sealed record BasketResponse(
    Guid OwnerId,
    IReadOnlyList<BasketLineResponse> Items,
    int TotalQuantity,
    decimal Subtotal,
    string Currency,
    DateTimeOffset UpdatedAt);

public sealed record BasketLineResponse(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string Currency,
    string? ImageUrl);

public sealed record AddItemRequest(Guid ProductId, int Quantity);

public sealed record SetQuantityRequest(int Quantity);

public sealed class AddItemRequestValidator : AbstractValidator<AddItemRequest>
{
    public AddItemRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.Quantity).GreaterThan(0).LessThanOrEqualTo(1000);
    }
}

public static class BasketEndpoints
{
    public static IEndpointRouteBuilder MapBasketEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/v1/basket").WithTags("Basket");

        group.MapGet("/", GetAsync)
            .RequirePermission(SamaPermissions.Basket.ReadOwn)
            .RateLimit("read")
            .WithSummary("Returns the caller's basket.");

        group.MapPost("/items", AddItemAsync)
            .RequirePermission(SamaPermissions.Basket.WriteOwn)
            .RateLimit("write")
            .WithSummary("Adds a product to the basket, or increases its quantity.");

        group.MapPut("/items/{productId:guid}", SetQuantityAsync)
            .RequirePermission(SamaPermissions.Basket.WriteOwn)
            .RateLimit("write")
            .WithSummary("Sets the quantity of a line; zero removes it.");

        group.MapDelete("/items/{productId:guid}", RemoveItemAsync)
            .RequirePermission(SamaPermissions.Basket.WriteOwn)
            .RateLimit("write")
            .WithSummary("Removes a line from the basket.");

        group.MapDelete("/", ClearAsync)
            .RequirePermission(SamaPermissions.Basket.WriteOwn)
            .RateLimit("write")
            .WithSummary("Empties the basket.");

        return routes;
    }

    private static async Task<IResult> GetAsync(
        HttpContext context,
        BasketStore store,
        ICurrentUser currentUser,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        // A basket is private to one shopper, so nothing along the path may retain it. Without
        // this a shared cache could hand one customer another's basket.
        context.Response.SetNoStore();

        var basket = await store.GetAsync(tenant.RequireTenantId(), currentUser.RequireUserId(), cancellationToken);
        return Results.Ok(ToResponse(basket));
    }

    private static async Task<IResult> AddItemAsync(
        AddItemRequest request,
        HttpContext context,
        IValidator<AddItemRequest> validator,
        BasketStore store,
        ICatalogClient catalog,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IOptions<BasketOptions> options,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        // The price comes from the catalogue, never from the request body. A client-supplied
        // price is the oldest e-commerce vulnerability there is.
        var lookup = await catalog.LookupAsync([request.ProductId], cancellationToken);
        if (lookup.IsFailure)
        {
            return lookup.Error.ToProblem();
        }

        var product = lookup.Value.FirstOrDefault(p => p.Id == request.ProductId);
        if (product is null)
        {
            return Error.NotFound("basket.product.notFound", "No such product.").ToProblem();
        }

        if (!product.IsPurchasable)
        {
            return Error.Conflict("basket.product.unavailable", "That product is not currently available.").ToProblem();
        }

        var result = await store.MutateAsync(
            tenant.RequireTenantId(),
            currentUser.RequireUserId(),
            isGuest: false,
            basket =>
            {
                basket.AddOrIncrement(
                    new BasketItem
                    {
                        ProductId = product.Id,
                        Sku = product.Sku,
                        Name = product.Name,
                        Quantity = request.Quantity,
                        UnitPrice = product.Price,
                        Currency = product.Currency,
                        ImageUrl = product.PrimaryImageUrl,
                    },
                    options.Value.MaxQuantityPerLine);

                return Result.Success();
            },
            cancellationToken);

        return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : result.Error.ToProblem();
    }

    private static async Task<IResult> SetQuantityAsync(
        Guid productId,
        SetQuantityRequest request,
        HttpContext context,
        BasketStore store,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IOptions<BasketOptions> options,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        if (request.Quantity < 0)
        {
            return Error.Validation("basket.quantity.invalid", "Quantity cannot be negative.").ToProblem();
        }

        var result = await store.MutateAsync(
            tenant.RequireTenantId(),
            currentUser.RequireUserId(),
            isGuest: false,
            basket => basket.SetQuantity(productId, request.Quantity, options.Value.MaxQuantityPerLine)
                ? Result.Success()
                : Result.Failure(Error.NotFound("basket.line.notFound", "That product is not in the basket.")),
            cancellationToken);

        return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : result.Error.ToProblem();
    }

    private static async Task<IResult> RemoveItemAsync(
        Guid productId,
        HttpContext context,
        BasketStore store,
        ICurrentUser currentUser,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        var result = await store.MutateAsync(
            tenant.RequireTenantId(),
            currentUser.RequireUserId(),
            isGuest: false,
            basket =>
            {
                // Removing something already absent is the same end state; treating it as an
                // error would make a retried delete fail.
                basket.Remove(productId);
                return Result.Success();
            },
            cancellationToken);

        return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : result.Error.ToProblem();
    }

    private static async Task<IResult> ClearAsync(
        HttpContext context,
        BasketStore store,
        ICurrentUser currentUser,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        await store.ClearAsync(tenant.RequireTenantId(), currentUser.RequireUserId(), cancellationToken);
        return Results.NoContent();
    }

    private static BasketResponse ToResponse(CustomerBasket basket)
        => new(
            basket.OwnerId,
            [.. basket.Items.Select(i => new BasketLineResponse(
                i.ProductId, i.Sku, i.Name, i.Quantity, i.UnitPrice, i.LineTotal, i.Currency, i.ImageUrl))],
            basket.TotalQuantity,
            basket.Subtotal,
            basket.Currency,
            basket.UpdatedAt);

    private static IResult ToValidationProblem(FluentValidation.Results.ValidationResult validation)
    {
        var details = validation.Errors
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray(), StringComparer.Ordinal);

        return Error.Validation("request.invalid", "One or more fields are invalid.", details).ToProblem();
    }
}
