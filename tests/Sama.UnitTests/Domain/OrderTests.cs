using Sama.Ordering.Domain;
using Sama.Shared.Kernel.Domain;
using Sama.Shared.Messaging.Contracts;
using Shouldly;

namespace Sama.UnitTests.Domain;

/// <summary>
/// The order aggregate is the checkout saga's state machine. Its guards are what make an
/// out-of-order or redelivered event harmless, so these tests deliberately drive transitions
/// in the wrong order and assert that nothing moves backwards.
/// </summary>
public class OrderTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Customer = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly ShippingAddress Address = new("1 Nile St", "Cairo", "11511", "EG");

    private static Order Placed(params (decimal Price, int Quantity)[] lines)
    {
        var orderId = SamaId.New();

        var orderLines = lines
            .Select((line, index) => new OrderLine(
                orderId, SamaId.New(), $"SKU-{index}", $"Item {index}", line.Quantity, line.Price, "USD"))
            .ToList();

        var result = Order.Place(Tenant, Customer, orderLines, Address, Now);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    [Fact]
    public void Placing_an_order_totals_its_lines_and_announces_itself()
    {
        var order = Placed((10.00m, 2), (5.50m, 1));

        order.Total.ShouldBe(25.50m);
        order.Status.ShouldBe(OrderStatus.Pending);
        order.OwnerId.ShouldBe(Customer);
        order.DomainEvents.OfType<OrderPlaced>().ShouldHaveSingleItem();
    }

    [Fact]
    public void An_order_number_is_human_quotable_and_date_stamped()
    {
        Placed((10m, 1)).OrderNumber.ShouldStartWith("SAMA-20260825-");
    }

    [Fact]
    public void Two_orders_placed_at_the_same_instant_get_different_numbers()
    {
        // The suffix is cryptographic rather than sequential, so the reference cannot be used
        // to infer daily order volume — and it must not collide.
        Placed((10m, 1)).OrderNumber.ShouldNotBe(Placed((10m, 1)).OrderNumber);
    }

    [Fact]
    public void An_order_cannot_be_placed_with_no_lines()
    {
        var result = Order.Place(Tenant, Customer, [], Address, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ordering.order.empty");
    }

    [Fact]
    public void An_order_cannot_mix_currencies()
    {
        // Mixed currencies cannot be summed into one total, and a payment provider charges in
        // exactly one — better to refuse than to silently pick the first.
        var orderId = SamaId.New();
        var lines = new List<OrderLine>
        {
            new(orderId, SamaId.New(), "A", "A", 1, 10m, "USD"),
            new(orderId, SamaId.New(), "B", "B", 1, 10m, "EUR"),
        };

        var result = Order.Place(Tenant, Customer, lines, Address, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ordering.order.mixedCurrency");
    }

    [Fact]
    public void Reserving_stock_moves_a_pending_order_on_and_requests_payment()
    {
        var order = Placed((10m, 1));
        order.ClearDomainEvents();

        order.MarkStockReserved();

        order.Status.ShouldBe(OrderStatus.AwaitingPayment);
        order.DomainEvents.OfType<PaymentRequested>().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_redelivered_stock_reservation_does_not_request_payment_twice()
    {
        // Pub/Sub delivers at least once. Two payment requests would be two charges.
        var order = Placed((10m, 1));
        order.MarkStockReserved();
        order.ClearDomainEvents();

        order.MarkStockReserved();

        order.DomainEvents.ShouldBeEmpty();
        order.Status.ShouldBe(OrderStatus.AwaitingPayment);
    }

    [Fact]
    public void Stock_reservation_arriving_after_cancellation_does_not_revive_the_order()
    {
        var order = Placed((10m, 1));
        order.Cancel("out of stock", Now);
        order.ClearDomainEvents();

        order.MarkStockReserved();

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Confirming_records_the_payment_and_announces_the_sale()
    {
        var order = Placed((10m, 1));
        order.MarkStockReserved();
        order.ClearDomainEvents();

        var paymentId = SamaId.New();
        order.Confirm(paymentId, Now);

        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.PaymentId.ShouldBe(paymentId);
        order.ConfirmedAt.ShouldBe(Now);
        order.DomainEvents.OfType<OrderConfirmed>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Confirming_twice_announces_the_sale_only_once()
    {
        var order = Placed((10m, 1));
        order.MarkStockReserved();
        order.Confirm(SamaId.New(), Now);
        order.ClearDomainEvents();

        order.Confirm(SamaId.New(), Now);

        order.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Confirming_a_cancelled_order_throws_so_a_human_sees_the_orphaned_payment()
    {
        // Money taken against a dead order is a refund case. Swallowing it here would lose
        // the only signal that a refund is owed; throwing dead-letters the message instead.
        var order = Placed((10m, 1));
        order.Cancel("payment failed", Now);

        Should.Throw<InvalidOperationException>(() => order.Confirm(SamaId.New(), Now));
    }

    [Fact]
    public void Cancelling_announces_the_cancellation_with_the_lines_to_release()
    {
        // The event carries the lines so inventory can compensate without a second lookup.
        var order = Placed((10m, 2), (5m, 3));
        order.ClearDomainEvents();

        order.Cancel("customer changed their mind", Now).IsSuccess.ShouldBeTrue();

        var cancelled = order.DomainEvents.OfType<OrderCancelled>().ShouldHaveSingleItem();
        cancelled.Lines.Count.ShouldBe(2);
        cancelled.Lines.Sum(l => l.Quantity).ShouldBe(5);
    }

    [Fact]
    public void Cancelling_twice_succeeds_but_announces_only_once()
    {
        // A retried cancellation should succeed rather than 409; releasing stock twice would
        // invent inventory.
        var order = Placed((10m, 1));
        order.Cancel("first", Now);
        order.ClearDomainEvents();

        order.Cancel("second", Now).IsSuccess.ShouldBeTrue();

        order.DomainEvents.ShouldBeEmpty();
        order.CancellationReason.ShouldBe("first");
    }

    [Fact]
    public void A_shipped_order_cannot_be_cancelled()
    {
        var order = Placed((10m, 1));
        order.MarkStockReserved();
        order.Confirm(SamaId.New(), Now);
        order.MarkShipped();

        var result = order.Cancel("too late", Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ordering.order.alreadyShipped");
    }

    [Fact]
    public void Only_a_confirmed_order_can_ship()
    {
        var order = Placed((10m, 1));

        order.MarkShipped();

        order.Status.ShouldBe(OrderStatus.Pending);
    }
}
