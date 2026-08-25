using Sama.Basket.Domain;
using Shouldly;

namespace Sama.UnitTests.Domain;

public class BasketTests
{
    private const int MaxPerLine = 20;

    private static CustomerBasket Empty() =>
        CustomerBasket.Empty(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    private static BasketItem Item(Guid productId, int quantity, decimal price = 10m) => new()
    {
        ProductId = productId,
        Sku = "SKU-1",
        Name = "Item",
        Quantity = quantity,
        UnitPrice = price,
        Currency = "USD",
    };

    [Fact]
    public void Adding_the_same_product_twice_merges_into_one_line()
    {
        var basket = Empty();
        var product = Guid.CreateVersion7();

        basket.AddOrIncrement(Item(product, 2), MaxPerLine);
        basket.AddOrIncrement(Item(product, 3), MaxPerLine);

        basket.Items.Count.ShouldBe(1);
        basket.Items[0].Quantity.ShouldBe(5);
    }

    [Fact]
    public void Merging_refreshes_the_unit_price_from_the_incoming_item()
    {
        // The incoming item's price came from the catalogue moments ago; the existing line's
        // may be older. Neither is what the customer is finally charged — Ordering re-prices
        // at checkout — but the basket should show the fresher number.
        var basket = Empty();
        var product = Guid.CreateVersion7();

        basket.AddOrIncrement(Item(product, 1, 10m), MaxPerLine);
        basket.AddOrIncrement(Item(product, 1, 12m), MaxPerLine);

        basket.Items[0].UnitPrice.ShouldBe(12m);
    }

    [Fact]
    public void A_merged_quantity_is_capped_rather_than_refused()
    {
        var basket = Empty();
        var product = Guid.CreateVersion7();

        basket.AddOrIncrement(Item(product, 15), MaxPerLine);
        basket.AddOrIncrement(Item(product, 15), MaxPerLine);

        basket.Items[0].Quantity.ShouldBe(MaxPerLine);
    }

    [Fact]
    public void Setting_a_quantity_of_zero_removes_the_line()
    {
        var basket = Empty();
        var product = Guid.CreateVersion7();
        basket.AddOrIncrement(Item(product, 3), MaxPerLine);

        basket.SetQuantity(product, 0, MaxPerLine).ShouldBeTrue();

        basket.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Setting_a_quantity_on_a_line_that_is_not_there_reports_it()
    {
        Empty().SetQuantity(Guid.CreateVersion7(), 1, MaxPerLine).ShouldBeFalse();
    }

    [Fact]
    public void The_subtotal_is_the_sum_of_the_line_totals()
    {
        var basket = Empty();
        basket.AddOrIncrement(Item(Guid.CreateVersion7(), 2, 10.00m), MaxPerLine);
        basket.AddOrIncrement(Item(Guid.CreateVersion7(), 3, 5.50m), MaxPerLine);

        basket.Subtotal.ShouldBe(36.50m);
        basket.TotalQuantity.ShouldBe(5);
    }

    [Fact]
    public void A_line_total_rounds_to_the_currencys_precision()
    {
        var basket = Empty();
        basket.AddOrIncrement(Item(Guid.CreateVersion7(), 3, 0.335m), MaxPerLine);

        basket.Items[0].LineTotal.ShouldBe(1.01m);
    }

    [Fact]
    public void Removing_a_product_that_is_absent_is_not_an_error()
    {
        // A retried delete must not fail; the end state is the same either way.
        Empty().Remove(Guid.CreateVersion7()).ShouldBeFalse();
    }
}
