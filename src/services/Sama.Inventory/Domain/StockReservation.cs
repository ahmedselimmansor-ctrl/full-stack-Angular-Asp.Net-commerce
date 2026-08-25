using Sama.Shared.Kernel.Domain;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Inventory.Domain;

public enum ReservationStatus
{
    Held = 0,
    Released = 1,
    Fulfilled = 2,
}

/// <summary>
/// The units held for one order.
/// <para>
/// This record is what makes the saga's compensating action possible: when an order is
/// cancelled the release does not have to trust the cancellation message to say what to give
/// back, it replays what was actually taken.
/// </para>
/// </summary>
public sealed class StockReservation : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private readonly List<ReservationLine> _lines = [];

    private StockReservation() { }

    private StockReservation(Guid id, Guid tenantId, Guid orderId)
        : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        Status = ReservationStatus.Held;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Uniquely indexed: one order may hold stock exactly once, however often its event is redelivered.</summary>
    public Guid OrderId { get; private set; }

    public ReservationStatus Status { get; private set; }

    public IReadOnlyCollection<ReservationLine> Lines => _lines.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public static StockReservation Hold(Guid tenantId, Guid orderId, IEnumerable<(Guid ProductId, int Quantity)> lines)
    {
        var reservation = new StockReservation(SamaId.New(), tenantId, orderId);

        foreach (var (productId, quantity) in lines)
        {
            reservation._lines.Add(new ReservationLine(reservation.Id, productId, quantity));
        }

        return reservation;
    }

    /// <summary>Announces the hold. Written to the outbox by the same transaction that creates it.</summary>
    public void RaiseReserved() => Raise(new StockReserved(OrderId, TenantId, Id));

    public void MarkReleased() => Status = ReservationStatus.Released;

    public void MarkFulfilled() => Status = ReservationStatus.Fulfilled;

    public void SetCreated(DateTimeOffset at, Guid? by) { CreatedAt = at; CreatedBy = by; }
    public void SetUpdated(DateTimeOffset at, Guid? by) { UpdatedAt = at; UpdatedBy = by; }
}

public sealed class ReservationLine
{
    private ReservationLine() { }

    public ReservationLine(Guid reservationId, Guid productId, int quantity)
    {
        ReservationId = reservationId;
        ProductId = productId;
        Quantity = quantity;
    }

    public Guid ReservationId { get; private set; }

    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }
}
