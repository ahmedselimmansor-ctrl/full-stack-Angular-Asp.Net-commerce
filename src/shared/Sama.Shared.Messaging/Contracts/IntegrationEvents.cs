using Sama.Shared.Kernel.Domain;

namespace Sama.Shared.Messaging.Contracts;

/// <summary>
/// The published contract between services. Integration events are versioned in their name
/// (<c>…​.v1</c>) and are additive-only: adding an optional member is safe, removing or
/// renaming one is a new version, because producers and consumers deploy independently and
/// an old consumer will keep reading the old shape for as long as it takes to roll forward.
/// </summary>
public abstract record IntegrationEvent : DomainEvent;

// ---------------------------------------------------------------------------------------
// Ordering
// ---------------------------------------------------------------------------------------

public sealed record OrderPlaced(
    Guid OrderId,
    Guid TenantId,
    Guid CustomerId,
    string OrderNumber,
    decimal Total,
    string Currency,
    IReadOnlyList<OrderLineSnapshot> Lines) : IntegrationEvent
{
    public override string EventType => "ordering.order.placed.v1";
}

public sealed record OrderLineSnapshot(Guid ProductId, string Sku, int Quantity, decimal UnitPrice);

public sealed record OrderConfirmed(Guid OrderId, Guid TenantId, Guid CustomerId) : IntegrationEvent
{
    public override string EventType => "ordering.order.confirmed.v1";
}

public sealed record OrderCancelled(
    Guid OrderId,
    Guid TenantId,
    Guid CustomerId,
    string Reason,
    IReadOnlyList<OrderLineSnapshot> Lines) : IntegrationEvent
{
    public override string EventType => "ordering.order.cancelled.v1";
}

// ---------------------------------------------------------------------------------------
// Inventory
// ---------------------------------------------------------------------------------------

public sealed record StockReserved(Guid OrderId, Guid TenantId, Guid ReservationId) : IntegrationEvent
{
    public override string EventType => "inventory.stock.reserved.v1";
}

public sealed record StockReservationRejected(
    Guid OrderId,
    Guid TenantId,
    IReadOnlyList<Guid> UnavailableProductIds) : IntegrationEvent
{
    public override string EventType => "inventory.stock.rejected.v1";
}

// ---------------------------------------------------------------------------------------
// Payment
// ---------------------------------------------------------------------------------------

public sealed record PaymentRequested(
    Guid OrderId,
    Guid TenantId,
    Guid CustomerId,
    decimal Amount,
    string Currency) : IntegrationEvent
{
    public override string EventType => "payment.payment.requested.v1";
}

public sealed record PaymentSucceeded(
    Guid OrderId,
    Guid TenantId,
    Guid PaymentId,
    decimal Amount,
    string Currency) : IntegrationEvent
{
    public override string EventType => "payment.payment.succeeded.v1";
}

public sealed record PaymentFailed(
    Guid OrderId,
    Guid TenantId,
    Guid PaymentId,
    string Reason) : IntegrationEvent
{
    public override string EventType => "payment.payment.failed.v1";
}

// ---------------------------------------------------------------------------------------
// Catalog / Identity
// ---------------------------------------------------------------------------------------

public sealed record ProductPriceChanged(
    Guid ProductId,
    Guid TenantId,
    decimal OldPrice,
    decimal NewPrice,
    string Currency) : IntegrationEvent
{
    public override string EventType => "catalog.product.priceChanged.v1";
}

public sealed record ProductPublished(Guid ProductId, Guid TenantId, string Sku) : IntegrationEvent
{
    public override string EventType => "catalog.product.published.v1";
}

public sealed record UserRegistered(Guid UserId, Guid TenantId, string Email) : IntegrationEvent
{
    public override string EventType => "identity.user.registered.v1";
}

/// <summary>
/// Maps an event type to the Pub/Sub topic that carries it. One topic per owning service
/// rather than per event: subscribers attach an attribute filter for the specific types they
/// want, which keeps topic count flat as the event catalogue grows.
/// </summary>
public static class EventTopics
{
    public const string Ordering = "ordering";
    public const string Inventory = "inventory";
    public const string Payment = "payment";
    public const string Catalog = "catalog";
    public const string Identity = "identity";
    public const string Basket = "basket";

    public static string ForEventType(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var separator = eventType.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new ArgumentException(
                $"Event type '{eventType}' is malformed; expected '<area>.<subject>.<verb>.v<n>'.",
                nameof(eventType));
        }

        return eventType[..separator];
    }
}
