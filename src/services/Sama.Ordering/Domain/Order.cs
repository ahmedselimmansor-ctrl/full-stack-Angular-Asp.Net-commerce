using System.Globalization;
using System.Security.Cryptography;
using Sama.Shared.Kernel.Domain;
using Sama.Shared.Kernel.Primitives;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Ordering.Domain;

/// <summary>
/// Where an order is in the checkout saga.
/// <para>
/// The order is the saga's state, so this enum is also its progress marker. Every transition
/// goes through a guarded method on the aggregate, which is what makes an out-of-order or
/// redelivered event a no-op instead of a corruption.
/// </para>
/// </summary>
public enum OrderStatus
{
    /// <summary>Placed; waiting for inventory to answer.</summary>
    Pending = 0,

    /// <summary>Stock is held; waiting for payment.</summary>
    AwaitingPayment = 1,

    /// <summary>Paid and committed.</summary>
    Confirmed = 2,

    /// <summary>Despatched to the customer.</summary>
    Shipped = 3,

    /// <summary>Ended without a sale. Terminal.</summary>
    Cancelled = 4,
}

public sealed class Order : AggregateRoot<Guid>, ITenantScoped, IUserOwned, IAuditable
{
    private readonly List<OrderLine> _lines = [];

    private Order() { }

    private Order(Guid id, Guid tenantId, Guid customerId, string orderNumber, string currency)
        : base(id)
    {
        TenantId = tenantId;
        OwnerId = customerId;
        OrderNumber = orderNumber;
        Currency = currency;
        Status = OrderStatus.Pending;
    }

    public Guid TenantId { get; private set; }

    /// <summary>
    /// The customer. Named OwnerId to satisfy <see cref="IUserOwned"/>, which is what lets the
    /// access-scope filter narrow a listing to "my orders" without every query remembering to.
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>Human-facing reference, quoted in emails and to support.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }

    public string Currency { get; private set; } = "USD";

    public decimal Total { get; private set; }

    public string? CancellationReason { get; private set; }

    public Guid? PaymentId { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string ShippingLine1 { get; private set; } = string.Empty;
    public string ShippingCity { get; private set; } = string.Empty;
    public string ShippingPostcode { get; private set; } = string.Empty;
    public string ShippingCountry { get; private set; } = string.Empty;

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public bool IsTerminal => Status is OrderStatus.Cancelled;

    public static Result<Order> Place(
        Guid tenantId,
        Guid customerId,
        IReadOnlyList<OrderLine> lines,
        ShippingAddress shipping,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(shipping);

        if (lines.Count == 0)
        {
            return Error.Validation("ordering.order.empty", "An order needs at least one line.");
        }

        // Mixed currencies cannot be summed into one total, and a payment provider charges in
        // exactly one. Reject rather than silently pick the first.
        var currencies = lines.Select(l => l.Currency).Distinct(StringComparer.Ordinal).ToArray();
        if (currencies.Length > 1)
        {
            return Error.Validation(
                "ordering.order.mixedCurrency",
                $"An order cannot mix currencies ({string.Join(", ", currencies)}).");
        }

        var order = new Order(SamaId.New(), tenantId, customerId, NewOrderNumber(now), currencies[0]);

        order._lines.AddRange(lines);
        order.Total = lines.Aggregate(
            Money.Zero(currencies[0]),
            (running, line) => running.Add(line.LineTotal())).Amount;

        order.ShippingLine1 = shipping.Line1;
        order.ShippingCity = shipping.City;
        order.ShippingPostcode = shipping.Postcode;
        order.ShippingCountry = shipping.Country;

        order.Raise(new OrderPlaced(
            order.Id,
            tenantId,
            customerId,
            order.OrderNumber,
            order.Total,
            order.Currency,
            [.. lines.Select(l => new OrderLineSnapshot(l.ProductId, l.Sku, l.Quantity, l.UnitPrice))]));

        return order;
    }

    /// <summary>Inventory confirmed the hold; ask for payment.</summary>
    public void MarkStockReserved()
    {
        // Only a Pending order advances. A redelivered stock.reserved for an order that has
        // already been paid, or cancelled, must not drag it backwards.
        if (Status != OrderStatus.Pending)
        {
            return;
        }

        Status = OrderStatus.AwaitingPayment;
        Raise(new PaymentRequested(Id, TenantId, OwnerId, Total, Currency));
    }

    /// <summary>Payment cleared; the order is committed.</summary>
    public void Confirm(Guid paymentId, DateTimeOffset now)
    {
        if (Status is OrderStatus.Confirmed or OrderStatus.Shipped)
        {
            return;
        }

        if (Status == OrderStatus.Cancelled)
        {
            // A payment that lands after cancellation is a refund case for a human, not a
            // reason to resurrect the order.
            throw new InvalidOperationException(
                $"Order {OrderNumber} was cancelled and cannot be confirmed; the payment needs refunding.");
        }

        Status = OrderStatus.Confirmed;
        PaymentId = paymentId;
        ConfirmedAt = now;

        Raise(new OrderConfirmed(Id, TenantId, OwnerId));
    }

    /// <summary>
    /// Ends the order and asks downstream to compensate. Emitting the event here — rather than
    /// leaving it to the caller — is what guarantees held stock is always released.
    /// </summary>
    public Result Cancel(string reason, DateTimeOffset now)
    {
        if (Status == OrderStatus.Cancelled)
        {
            // Idempotent: a retried cancellation should succeed, not 409.
            return Result.Success();
        }

        if (Status is OrderStatus.Shipped)
        {
            return Result.Failure(Error.Conflict(
                "ordering.order.alreadyShipped",
                "This order has shipped and can no longer be cancelled. Raise a return instead."));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = now;

        Raise(new OrderCancelled(
            Id, TenantId, OwnerId, reason,
            [.. _lines.Select(l => new OrderLineSnapshot(l.ProductId, l.Sku, l.Quantity, l.UnitPrice))]));

        return Result.Success();
    }

    public void MarkShipped()
    {
        if (Status == OrderStatus.Confirmed)
        {
            Status = OrderStatus.Shipped;
        }
    }

    /// <summary>
    /// A sortable, human-quotable reference: <c>SAMA-20260825-7F3A9C</c>. The random suffix is
    /// cryptographic so the reference cannot be used to infer daily order volume.
    /// </summary>
    private static string NewOrderNumber(DateTimeOffset now)
    {
        Span<byte> entropy = stackalloc byte[3];
        RandomNumberGenerator.Fill(entropy);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"SAMA-{now:yyyyMMdd}-{Convert.ToHexString(entropy)}");
    }

    public void SetCreated(DateTimeOffset at, Guid? by) { CreatedAt = at; CreatedBy = by; }
    public void SetUpdated(DateTimeOffset at, Guid? by) { UpdatedAt = at; UpdatedBy = by; }
}

public sealed record ShippingAddress(string Line1, string City, string Postcode, string Country);

public sealed class OrderLine
{
    private OrderLine() { }

    public OrderLine(Guid orderId, Guid productId, string sku, string name, int quantity, decimal unitPrice, string currency)
    {
        OrderId = orderId;
        ProductId = productId;
        Sku = sku;
        Name = name;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Currency = currency;
    }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    /// <summary>
    /// Copied at checkout, not looked up later. An order is a record of what was bought, and
    /// renaming a product next month must not rewrite last month's invoices.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public string Currency { get; private set; } = "USD";

    public Money LineTotal() => Money.FromTrusted(UnitPrice, Currency).Multiply(Quantity);
}
