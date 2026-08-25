using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sama.Inventory.Domain;
using Sama.Inventory.Persistence;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Infrastructure.RateLimiting;
using Sama.Shared.Kernel.Paging;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Security.Permissions;
using Sama.Shared.Security.Tenancy;

namespace Sama.Inventory.Features;

public sealed record StockLevel(
    Guid ProductId,
    string Sku,
    int OnHand,
    int Reserved,
    int Available,
    int ReorderLevel,
    bool BelowReorderLevel);

public sealed record AdjustStockRequest(int QuantityOnHand, int? ReorderLevel);

public sealed record CreateStockItemRequest(Guid ProductId, string Sku, int QuantityOnHand, int ReorderLevel);

public static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/v1/stock").WithTags("Stock");

        group.MapGet("/", ListAsync)
            .RequirePermission(SamaPermissions.Inventory.ReadStock)
            .RateLimit("read")
            .WithSummary("Lists stock levels.");

        group.MapGet("/{productId:guid}", GetAsync)
            .RequirePermission(SamaPermissions.Inventory.ReadStock)
            .RateLimit("read")
            .WithSummary("Returns the stock level for one product.");

        group.MapPost("/", CreateAsync)
            .RequirePermission(SamaPermissions.Inventory.WriteStock)
            .RateLimit("write")
            .WithSummary("Creates a stock record for a product.");

        group.MapPut("/{productId:guid}", AdjustAsync)
            .RequirePermission(SamaPermissions.Inventory.WriteStock)
            .RateLimit("write")
            .WithSummary("Sets stock on hand from a count or a goods-in receipt.");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        InventoryDbContext database,
        CancellationToken cancellationToken,
        bool lowOnly = false,
        int page = 1,
        int pageSize = 50)
    {
        // Stock levels move constantly and are commercially sensitive; never cache them.
        context.Response.SetNoStore();

        var request = new PageRequest(page, pageSize);
        var query = database.StockItems.AsNoTracking();

        if (lowOnly)
        {
            query = query.Where(s => s.QuantityOnHand - s.QuantityReserved <= s.ReorderLevel);
        }

        var total = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderBy(s => s.Sku)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(s => new StockLevel(
                s.ProductId, s.Sku, s.QuantityOnHand, s.QuantityReserved,
                s.QuantityOnHand - s.QuantityReserved, s.ReorderLevel,
                s.QuantityOnHand - s.QuantityReserved <= s.ReorderLevel))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedResult<StockLevel>(items, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetAsync(
        Guid productId,
        HttpContext context,
        InventoryDbContext database,
        CancellationToken cancellationToken)
    {
        context.Response.SetNoStore();

        var item = await database.StockItems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);

        return item is null
            ? Error.NotFound("inventory.stock.notFound", "No stock record for that product.").ToProblem()
            : Results.Ok(ToLevel(item));
    }

    private static async Task<IResult> CreateAsync(
        CreateStockItemRequest request,
        InventoryDbContext database,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        if (request.QuantityOnHand < 0)
        {
            return Error.Validation("inventory.quantity.negative", "Stock on hand cannot be negative.").ToProblem();
        }

        var exists = await database.StockItems
            .AnyAsync(s => s.ProductId == request.ProductId, cancellationToken);

        if (exists)
        {
            return Error.Conflict("inventory.stock.exists", "That product already has a stock record.").ToProblem();
        }

        var item = StockItem.Create(tenant.RequireTenantId(), request.ProductId, request.Sku, request.QuantityOnHand);

        var adjusted = item.Adjust(request.QuantityOnHand, request.ReorderLevel);
        if (adjusted.IsFailure)
        {
            return adjusted.Error.ToProblem();
        }

        database.StockItems.Add(item);
        await database.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/v1/stock/{item.ProductId}", ToLevel(item));
    }

    private static async Task<IResult> AdjustAsync(
        Guid productId,
        AdjustStockRequest request,
        InventoryDbContext database,
        CancellationToken cancellationToken)
    {
        var item = await database.StockItems
            .FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);

        if (item is null)
        {
            return Error.NotFound("inventory.stock.notFound", "No stock record for that product.").ToProblem();
        }

        var result = item.Adjust(request.QuantityOnHand, request.ReorderLevel);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        // Concurrent adjustments collide on the xmin token rather than silently overwriting;
        // the shared exception handler turns that into a 409 telling the operator to reload.
        await database.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToLevel(item));
    }

    private static StockLevel ToLevel(StockItem item)
        => new(item.ProductId, item.Sku, item.QuantityOnHand, item.QuantityReserved,
            item.QuantityAvailable, item.ReorderLevel, item.IsBelowReorderLevel);
}
