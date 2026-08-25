using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sama.Inventory.Domain;
using Sama.Inventory.Persistence;
using Sama.Shared.Messaging.Consuming;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Inventory.Integration;

/// <summary>
/// The saga's compensating action: give back stock held for an order that will not complete.
/// <para>
/// The quantities come from the stored reservation, not from the cancellation message. The
/// message describes the order; only the reservation records what was actually taken, and
/// after a partial fulfilment or a manual adjustment those two can legitimately differ.
/// Releasing from the message would then invent or destroy stock.
/// </para>
/// </summary>
public sealed class ReleaseStockOnOrderCancelled(
    InventoryDbContext database,
    ILogger<ReleaseStockOnOrderCancelled> logger) : IIntegrationEventHandler<OrderCancelled>
{
    public async Task HandleAsync(OrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var reservation = await database.Reservations
            .FirstOrDefaultAsync(r => r.OrderId == integrationEvent.OrderId, cancellationToken);

        if (reservation is null)
        {
            // The order was rejected before stock was ever held — nothing to give back.
            logger.LogDebug("Order {OrderId} has no reservation to release.", integrationEvent.OrderId);
            return;
        }

        if (reservation.Status != ReservationStatus.Held)
        {
            // Already released or already despatched. Releasing twice would inflate availability.
            logger.LogDebug(
                "Reservation for order {OrderId} is {Status}; nothing to release.",
                integrationEvent.OrderId, reservation.Status);
            return;
        }

        var productIds = reservation.Lines.Select(l => l.ProductId).ToArray();

        var stock = await database.StockItems
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId, cancellationToken);

        foreach (var line in reservation.Lines)
        {
            if (stock.TryGetValue(line.ProductId, out var item))
            {
                item.Release(line.Quantity);
            }
        }

        reservation.MarkReleased();
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Released stock for cancelled order {OrderId} ({Reason}).",
            integrationEvent.OrderId, integrationEvent.Reason);
    }
}
