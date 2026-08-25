using Sama.Shared.Kernel.Domain;
using Sama.Shared.Kernel.Results;

namespace Sama.Inventory.Domain;

/// <summary>
/// Stock for one product.
/// <para>
/// Available stock is <see cref="QuantityOnHand"/> minus <see cref="QuantityReserved"/>.
/// Tracking the reservation separately — rather than decrementing on hand at checkout — means
/// a cancelled or unpaid order restores availability without ever having claimed the units
/// left the warehouse, and it keeps the on-hand figure reconcilable against a physical count.
/// </para>
/// </summary>
public sealed class StockItem : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private StockItem() { }

    private StockItem(Guid id, Guid tenantId, Guid productId, string sku, int quantityOnHand)
        : base(id)
    {
        TenantId = tenantId;
        ProductId = productId;
        Sku = sku;
        QuantityOnHand = quantityOnHand;
    }

    public Guid TenantId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public int QuantityOnHand { get; private set; }

    public int QuantityReserved { get; private set; }

    /// <summary>Threshold that triggers replenishment reporting.</summary>
    public int ReorderLevel { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    public bool IsBelowReorderLevel => QuantityAvailable <= ReorderLevel;

    public static StockItem Create(Guid tenantId, Guid productId, string sku, int quantityOnHand)
        => new(SamaId.New(), tenantId, productId, sku, quantityOnHand);

    /// <summary>Holds units for an order without removing them from stock on hand.</summary>
    public Result Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("inventory.quantity.invalid", "Reserved quantity must be positive."));
        }

        if (quantity > QuantityAvailable)
        {
            return Result.Failure(Error.Conflict(
                "inventory.stock.insufficient",
                $"Only {QuantityAvailable} of {Sku} are available."));
        }

        QuantityReserved += quantity;
        return Result.Success();
    }

    /// <summary>Returns held units to availability, when an order is cancelled or payment fails.</summary>
    public void Release(int quantity)
        // Clamped: a duplicate release must not drive the reservation negative and invent stock.
        => QuantityReserved = Math.Max(0, QuantityReserved - quantity);

    /// <summary>Converts a reservation into a despatch: the units have physically left.</summary>
    public Result Fulfil(int quantity)
    {
        if (quantity > QuantityReserved)
        {
            return Result.Failure(Error.Conflict(
                "inventory.fulfil.exceedsReservation",
                $"Cannot fulfil {quantity} of {Sku}; only {QuantityReserved} are reserved."));
        }

        QuantityReserved -= quantity;
        QuantityOnHand -= quantity;
        return Result.Success();
    }

    /// <summary>Absolute correction from a stock count or a goods-in receipt.</summary>
    public Result Adjust(int newQuantityOnHand, int? reorderLevel = null)
    {
        if (newQuantityOnHand < 0)
        {
            return Result.Failure(Error.Validation("inventory.quantity.negative", "Stock on hand cannot be negative."));
        }

        if (newQuantityOnHand < QuantityReserved)
        {
            // Allowing this would leave reserved units that no longer exist, and the shortfall
            // would only surface at despatch, after the customer has paid.
            return Result.Failure(Error.Conflict(
                "inventory.adjust.belowReserved",
                $"Cannot set stock to {newQuantityOnHand}; {QuantityReserved} units are already reserved against orders."));
        }

        QuantityOnHand = newQuantityOnHand;

        if (reorderLevel is { } level && level >= 0)
        {
            ReorderLevel = level;
        }

        return Result.Success();
    }

    public void SetCreated(DateTimeOffset at, Guid? by) { CreatedAt = at; CreatedBy = by; }
    public void SetUpdated(DateTimeOffset at, Guid? by) { UpdatedAt = at; UpdatedBy = by; }
}
