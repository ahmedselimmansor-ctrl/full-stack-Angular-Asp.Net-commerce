using Sama.Inventory.Domain;
using Shouldly;

namespace Sama.UnitTests.Domain;

/// <summary>
/// Stock is where an arithmetic slip becomes overselling. Available stock is on-hand minus
/// reserved, and every operation here has to keep that identity true.
/// </summary>
public class StockItemTests
{
    private static StockItem WithStock(int onHand) =>
        StockItem.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "SKU-1", onHand);

    [Fact]
    public void Reserving_holds_units_without_removing_them_from_stock_on_hand()
    {
        // The distinction matters: a cancelled order restores availability without ever
        // having claimed the units left the warehouse.
        var item = WithStock(10);

        item.Reserve(3).IsSuccess.ShouldBeTrue();

        item.QuantityOnHand.ShouldBe(10);
        item.QuantityReserved.ShouldBe(3);
        item.QuantityAvailable.ShouldBe(7);
    }

    [Fact]
    public void Reserving_more_than_is_available_is_refused()
    {
        var item = WithStock(5);
        item.Reserve(4);

        var result = item.Reserve(2);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("inventory.stock.insufficient");
        item.QuantityReserved.ShouldBe(4, "a refused reservation must not partially apply");
    }

    [Fact]
    public void Reserving_exactly_the_available_quantity_succeeds()
    {
        var item = WithStock(5);

        item.Reserve(5).IsSuccess.ShouldBeTrue();
        item.QuantityAvailable.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserving_a_non_positive_quantity_is_refused(int quantity)
    {
        WithStock(10).Reserve(quantity).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Releasing_returns_held_units_to_availability()
    {
        var item = WithStock(10);
        item.Reserve(4);

        item.Release(4);

        item.QuantityReserved.ShouldBe(0);
        item.QuantityAvailable.ShouldBe(10);
    }

    [Fact]
    public void Releasing_more_than_is_reserved_cannot_invent_stock()
    {
        // A duplicate release must not drive the reservation negative, which would show up as
        // availability exceeding what physically exists.
        var item = WithStock(10);
        item.Reserve(2);

        item.Release(5);

        item.QuantityReserved.ShouldBe(0);
        item.QuantityAvailable.ShouldBe(10);
    }

    [Fact]
    public void Fulfilling_removes_units_from_both_reserved_and_on_hand()
    {
        var item = WithStock(10);
        item.Reserve(3);

        item.Fulfil(3).IsSuccess.ShouldBeTrue();

        item.QuantityOnHand.ShouldBe(7);
        item.QuantityReserved.ShouldBe(0);
    }

    [Fact]
    public void Fulfilling_more_than_is_reserved_is_refused()
    {
        var item = WithStock(10);
        item.Reserve(2);

        item.Fulfil(3).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Adjusting_below_the_reserved_quantity_is_refused()
    {
        // Allowing it would leave reserved units that no longer exist, and the shortfall
        // would only surface at despatch — after the customer has paid.
        var item = WithStock(10);
        item.Reserve(6);

        var result = item.Adjust(4);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("inventory.adjust.belowReserved");
        item.QuantityOnHand.ShouldBe(10);
    }

    [Fact]
    public void Adjusting_to_a_negative_quantity_is_refused()
    {
        WithStock(10).Adjust(-1).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_reorder_level_flags_stock_that_needs_replenishing()
    {
        var item = WithStock(10);
        item.Adjust(10, reorderLevel: 3);

        item.IsBelowReorderLevel.ShouldBeFalse();

        item.Reserve(7);
        item.IsBelowReorderLevel.ShouldBeTrue("3 available is at the reorder level");
    }
}
