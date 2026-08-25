using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sama.Ordering.Persistence;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Messaging.Consuming;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Ordering.Integration;

/// <summary>
/// Stock is held — move the order on and request payment.
/// <para>
/// Every handler here loads the order, calls one guarded transition, and saves. The guard
/// inside the aggregate is what makes redelivery and out-of-order arrival harmless: a
/// transition that no longer applies simply does nothing.
/// </para>
/// </summary>
public sealed class AdvanceOnStockReserved(
    OrderingDbContext database,
    ILogger<AdvanceOnStockReserved> logger) : IIntegrationEventHandler<StockReserved>
{
    public async Task HandleAsync(StockReserved integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var order = await database.Orders.FirstOrDefaultAsync(o => o.Id == integrationEvent.OrderId, cancellationToken);
        if (order is null)
        {
            logger.LogWarning("Stock reserved for unknown order {OrderId}.", integrationEvent.OrderId);
            return;
        }

        order.MarkStockReserved();
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderNumber} is awaiting payment.", order.OrderNumber);
    }
}

/// <summary>Stock could not be held — the order cannot proceed.</summary>
public sealed class CancelOnStockRejected(
    OrderingDbContext database,
    IClock clock,
    ILogger<CancelOnStockRejected> logger) : IIntegrationEventHandler<StockReservationRejected>
{
    public async Task HandleAsync(StockReservationRejected integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var order = await database.Orders.FirstOrDefaultAsync(o => o.Id == integrationEvent.OrderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        var result = order.Cancel("One or more items are out of stock.", clock.UtcNow);
        if (result.IsFailure)
        {
            logger.LogError(
                "Could not cancel order {OrderId} after a stock rejection: {Error}.",
                integrationEvent.OrderId, result.Error);
            return;
        }

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderNumber} cancelled: out of stock.", order.OrderNumber);
    }
}

/// <summary>Payment cleared — commit the order.</summary>
public sealed class ConfirmOnPaymentSucceeded(
    OrderingDbContext database,
    IClock clock,
    ILogger<ConfirmOnPaymentSucceeded> logger) : IIntegrationEventHandler<PaymentSucceeded>
{
    public async Task HandleAsync(PaymentSucceeded integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var order = await database.Orders.FirstOrDefaultAsync(o => o.Id == integrationEvent.OrderId, cancellationToken);
        if (order is null)
        {
            logger.LogError(
                "Payment {PaymentId} succeeded for unknown order {OrderId}; this needs a refund.",
                integrationEvent.PaymentId, integrationEvent.OrderId);
            return;
        }

        // Confirm throws when the order was already cancelled — money taken against a dead
        // order is a refund case that must not be swallowed. Letting it throw nacks the
        // message and routes it to the dead-letter queue where a human will see it.
        order.Confirm(integrationEvent.PaymentId, clock.UtcNow);
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderNumber} confirmed.", order.OrderNumber);
    }
}

/// <summary>Payment failed — end the order and let inventory release its hold.</summary>
public sealed class CancelOnPaymentFailed(
    OrderingDbContext database,
    IClock clock,
    ILogger<CancelOnPaymentFailed> logger) : IIntegrationEventHandler<PaymentFailed>
{
    public async Task HandleAsync(PaymentFailed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var order = await database.Orders.FirstOrDefaultAsync(o => o.Id == integrationEvent.OrderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        var result = order.Cancel($"Payment failed: {integrationEvent.Reason}", clock.UtcNow);
        if (result.IsFailure)
        {
            logger.LogError(
                "Could not cancel order {OrderId} after payment failure: {Error}.",
                integrationEvent.OrderId, result.Error);
            return;
        }

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderNumber} cancelled: payment failed.", order.OrderNumber);
    }
}
