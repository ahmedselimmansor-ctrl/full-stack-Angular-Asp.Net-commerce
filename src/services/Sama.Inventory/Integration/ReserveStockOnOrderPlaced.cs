using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sama.Inventory.Domain;
using Sama.Inventory.Persistence;
using Sama.Shared.Kernel.Domain;
using Sama.Shared.Messaging.Consuming;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Inventory.Integration;

/// <summary>
/// First step of the checkout saga: hold stock for a newly placed order, and answer with
/// either <see cref="StockReserved"/> or <see cref="StockReservationRejected"/>.
/// <para>
/// The reply is emitted through this service's own outbox in the same transaction as the
/// reservation, so Ordering can never be told stock was held by a transaction that rolled back.
/// </para>
/// </summary>
public sealed class ReserveStockOnOrderPlaced(
    InventoryDbContext database,
    ILogger<ReserveStockOnOrderPlaced> logger) : IIntegrationEventHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Redelivery is normal for Pub/Sub. If this order already holds stock, the work is
        // done — reserving again would double-count the units.
        var existing = await database.Reservations
            .AnyAsync(r => r.OrderId == integrationEvent.OrderId, cancellationToken);

        if (existing)
        {
            logger.LogDebug("Order {OrderId} already holds a reservation; ignoring redelivery.", integrationEvent.OrderId);
            return;
        }

        var productIds = integrationEvent.Lines.Select(l => l.ProductId).Distinct().ToArray();

        var stock = await database.StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, cancellationToken);

        var unavailable = new List<Guid>();

        foreach (var line in integrationEvent.Lines)
        {
            if (!stock.TryGetValue(line.ProductId, out var item))
            {
                // A product with no stock record has never been received into the warehouse.
                unavailable.Add(line.ProductId);
                continue;
            }

            if (item.Reserve(line.Quantity).IsFailure)
            {
                unavailable.Add(line.ProductId);
            }
        }

        if (unavailable.Count > 0)
        {
            // Abandon every in-memory reservation made in this pass: the order is all-or-nothing,
            // and partially holding stock for an order that will be cancelled strands inventory.
            database.ChangeTracker.Clear();

            database.EnqueueIntegrationEvent(
                new StockReservationRejected(integrationEvent.OrderId, integrationEvent.TenantId, unavailable),
                integrationEvent.TenantId);

            await database.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Rejected order {OrderId}: {Count} line(s) unavailable.",
                integrationEvent.OrderId, unavailable.Count);
            return;
        }

        var reservation = StockReservation.Hold(
            integrationEvent.TenantId,
            integrationEvent.OrderId,
            integrationEvent.Lines.Select(l => (l.ProductId, l.Quantity)));

        reservation.RaiseReserved();
        database.Reservations.Add(reservation);

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Reserved stock for order {OrderId} ({Lines} lines).",
            integrationEvent.OrderId, integrationEvent.Lines.Count);
    }
}
