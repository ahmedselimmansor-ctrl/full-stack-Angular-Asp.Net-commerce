using Sama.Shared.Kernel.Primitives;
using Shouldly;

namespace Sama.UnitTests.Kernel;

/// <summary>
/// Money is the type most likely to be wrong in a way nobody notices until a reconciliation
/// disagrees, so these tests are about rounding and currency discipline rather than arithmetic.
/// </summary>
public class MoneyTests
{
    [Theory]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("JOD", 3)]  // three minor units
    [InlineData("KWD", 3)]
    [InlineData("JPY", 0)]  // no minor unit
    [InlineData("IQD", 0)]
    public void Create_rounds_to_the_currencys_minor_units(string currency, int decimals)
    {
        var result = Money.Create(1.23456789m, currency);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(Math.Round(1.23456789m, decimals, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Create_rounds_half_away_from_zero_not_to_even()
    {
        // Bankers' rounding would give 0.12 here. Commerce expects 0.13, and a systematic
        // half-cent bias across millions of lines is a real reconciliation problem.
        Money.Create(0.125m, "USD").Value.Amount.ShouldBe(0.13m);
        Money.Create(0.135m, "USD").Value.Amount.ShouldBe(0.14m);
    }

    [Fact]
    public void Create_normalises_the_currency_code_to_upper_case()
    {
        Money.Create(10m, "usd").Value.Currency.ShouldBe("USD");
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    public void Create_rejects_a_malformed_currency_code(string currency)
    {
        var result = Money.Create(10m, currency);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("money.currency.invalid");
    }

    [Fact]
    public void Create_rejects_an_unsupported_currency()
    {
        var result = Money.Create(10m, "XYZ");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("money.currency.unsupported");
    }

    [Fact]
    public void Adding_different_currencies_throws_rather_than_silently_coercing()
    {
        var usd = Money.Create(10m, "USD").Value;
        var eur = Money.Create(10m, "EUR").Value;

        Should.Throw<InvalidOperationException>(() => usd.Add(eur));
    }

    [Fact]
    public void Create_rounds_before_any_arithmetic_happens()
    {
        // A Money cannot hold sub-precision: 0.335 is rounded to 0.34 at construction, so
        // three of them are 1.02 and not 1.005. That is the intended discipline — an amount
        // that cannot be charged should never exist in the first place.
        var line = Money.Create(0.335m, "USD").Value;

        line.Amount.ShouldBe(0.34m);
        line.Multiply(3).Amount.ShouldBe(1.02m);
    }

    [Fact]
    public void Multiply_rounds_its_own_result_to_the_currencys_precision()
    {
        // FromTrusted skips construction-time rounding, which is how a value materialised
        // from the database arrives. Multiply must still round its own output.
        var line = Money.FromTrusted(0.335m, "USD");

        line.Multiply(3).Amount.ShouldBe(1.01m);
    }

    [Fact]
    public void Multiplying_by_a_fractional_factor_rounds_the_result()
    {
        // A 17.5% tax on 19.99 is 3.49825, which must become 3.50.
        Money.Create(19.99m, "USD").Value.Multiply(0.175m).Amount.ShouldBe(3.50m);
    }

    [Fact]
    public void Comparison_operators_order_amounts_within_a_currency()
    {
        var small = Money.Create(5m, "USD").Value;
        var large = Money.Create(10m, "USD").Value;

        (small < large).ShouldBeTrue();
        (large > small).ShouldBeTrue();
        (small <= Money.Create(5m, "USD").Value).ShouldBeTrue();
    }

    [Fact]
    public void ToString_prints_the_currencys_full_precision()
    {
        Money.Create(1.5m, "USD").Value.ToString().ShouldBe("1.50 USD");
        Money.Create(1.5m, "JOD").Value.ToString().ShouldBe("1.500 JOD");
        Money.Create(1.5m, "JPY").Value.ToString().ShouldBe("2 JPY");
    }
}
