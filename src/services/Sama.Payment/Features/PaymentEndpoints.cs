using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sama.Payment.Persistence;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Infrastructure.RateLimiting;
using Sama.Shared.Kernel.Paging;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Security.AccessControl;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Permissions;

namespace Sama.Payment.Features;

public sealed record PaymentView(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string Status,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SettledAt);

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/v1/payments").WithTags("Payments");

        group.MapGet("/", ListAsync)
            .RequireAnyPermission(SamaPermissions.Payment.ReadOwn, SamaPermissions.Payment.ReadAny)
            .RateLimit("read")
            .WithSummary("Lists payments visible to the caller.");

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAnyPermission(SamaPermissions.Payment.ReadOwn, SamaPermissions.Payment.ReadAny)
            .RateLimit("read")
            .WithSummary("Returns one payment.");

        group.MapPost("/{id:guid}/refund", RefundAsync)
            .RequirePermission(SamaPermissions.Payment.Refund)
            .RateLimit("write")
            .WithSummary("Refunds a settled payment.");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        PaymentDbContext database,
        ICurrentUser currentUser,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        context.Response.SetNoStore();

        var request = new PageRequest(page, pageSize);
        var scope = currentUser.ScopeFor(SamaPermissions.Payment.ReadOwn, SamaPermissions.Payment.ReadAny);

        var query = database.Payments
            .AsNoTracking()
            .WithinScope(scope, currentUser.UserId);

        var total = await query.LongCountAsync(cancellationToken);

        var payments = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(p => new PaymentView(
                p.Id, p.OrderId, p.Amount, p.Currency, p.Status.ToString(),
                p.FailureReason, p.CreatedAt, p.SettledAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedResult<PaymentView>(payments, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        HttpContext context,
        PaymentDbContext database,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        var scope = currentUser.ScopeFor(SamaPermissions.Payment.ReadOwn, SamaPermissions.Payment.ReadAny);

        var payment = await database.Payments
            .AsNoTracking()
            .WithinScope(scope, currentUser.UserId)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return payment is null
            ? Error.NotFound("payment.notFound", "No such payment.").ToProblem()
            : Results.Ok(new PaymentView(
                payment.Id, payment.OrderId, payment.Amount, payment.Currency, payment.Status.ToString(),
                payment.FailureReason, payment.CreatedAt, payment.SettledAt));
    }

    private static async Task<IResult> RefundAsync(
        Guid id,
        HttpContext context,
        PaymentDbContext database,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        var payment = await database.Payments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (payment is null)
        {
            return Error.NotFound("payment.notFound", "No such payment.").ToProblem();
        }

        var result = payment.Refund();
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        await database.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
