using Sama.Shared.Kernel.Primitives;

namespace Sama.Basket.Domain;

/// <summary>
/// A shopper's basket.
/// <para>
/// Baskets live in Redis rather than PostgreSQL. They are high-churn, short-lived, and
/// disposable: a basket lost to an eviction is an annoyance, whereas an order lost is a
/// financial incident. Keeping them out of the relational store also keeps the busiest write
/// path in the system off the database that checkout depends on.
/// </para>
/// </summary>
public sealed record CustomerBasket
{
    public required Guid TenantId { get; init; }

    /// <summary>The signed-in user, or the anonymous basket id held in a cookie.</summary>
    public required Guid OwnerId { get; init; }

    public required List<BasketItem> Items { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Bumped on every write and checked on save, so two devices editing one basket cannot
    /// silently overwrite each other's changes.
    /// </summary>
    public long Version { get; set; }

    public static CustomerBasket Empty(Guid tenantId, Guid ownerId, DateTimeOffset now)
        => new() { TenantId = tenantId, OwnerId = ownerId, Items = [], UpdatedAt = now };

    public decimal Subtotal => Items.Sum(item => item.LineTotal);

    public string Currency => Items.Count > 0 ? Items[0].Currency : "USD";

    public int TotalQuantity => Items.Sum(item => item.Quantity);

    /// <summary>
    /// Adds or increments a line.
    /// <para>
    /// The unit price is captured from the catalogue at the moment of adding, but it is
    /// <i>not</i> what the customer is ultimately charged: Ordering re-prices every line
    /// against the catalogue at checkout. Otherwise a basket left open for a week would let a
    /// shopper buy at last week's price — or, worse, let a tampered client name its own.
    /// </para>
    /// </summary>
    public void AddOrIncrement(BasketItem item, int maxQuantityPerLine)
    {
        var existing = Items.FindIndex(i => i.ProductId == item.ProductId);

        if (existing >= 0)
        {
            var merged = Math.Min(Items[existing].Quantity + item.Quantity, maxQuantityPerLine);
            Items[existing] = Items[existing] with { Quantity = merged, UnitPrice = item.UnitPrice };
            return;
        }

        Items.Add(item with { Quantity = Math.Min(item.Quantity, maxQuantityPerLine) });
    }

    public bool SetQuantity(Guid productId, int quantity, int maxQuantityPerLine)
    {
        var index = Items.FindIndex(i => i.ProductId == productId);
        if (index < 0)
        {
            return false;
        }

        if (quantity <= 0)
        {
            Items.RemoveAt(index);
            return true;
        }

        Items[index] = Items[index] with { Quantity = Math.Min(quantity, maxQuantityPerLine) };
        return true;
    }

    public bool Remove(Guid productId) => Items.RemoveAll(i => i.ProductId == productId) > 0;
}

public sealed record BasketItem
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required string Currency { get; init; }

    public string? ImageUrl { get; init; }

    public decimal LineTotal => Money.FromTrusted(UnitPrice, Currency).Multiply(Quantity).Amount;
}
