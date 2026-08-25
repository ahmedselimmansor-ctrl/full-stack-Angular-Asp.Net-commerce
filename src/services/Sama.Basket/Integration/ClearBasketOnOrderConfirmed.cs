using Microsoft.Extensions.Logging;
using Sama.Basket.Storage;
using Sama.Shared.Messaging.Consuming;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Basket.Integration;

/// <summary>
/// Empties the basket once an order is confirmed.
/// <para>
/// Clearing on confirmation rather than at checkout is deliberate. An order can still fail
/// after it is placed — stock runs out, payment declines — and a shopper whose basket was
/// already emptied would have to rebuild it from memory. Waiting for the confirmation means
/// the basket survives every path that does not end in a sale.
/// </para>
/// </summary>
public sealed class ClearBasketOnOrderConfirmed(
    BasketStore store,
    ILogger<ClearBasketOnOrderConfirmed> logger) : IIntegrationEventHandler<OrderConfirmed>
{
    public async Task HandleAsync(OrderConfirmed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Naturally idempotent: deleting an already-deleted key is a no-op, so a redelivery
        // costs nothing.
        await store.ClearAsync(integrationEvent.TenantId, integrationEvent.CustomerId, cancellationToken);

        logger.LogInformation(
            "Cleared basket for customer {CustomerId} after order {OrderId} was confirmed.",
            integrationEvent.CustomerId, integrationEvent.OrderId);
    }
}
