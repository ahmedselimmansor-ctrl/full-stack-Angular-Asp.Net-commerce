using System.Globalization;
using Sama.Shared.Kernel.Results;

namespace Sama.Shared.Kernel.Primitives;

/// <summary>
/// An amount in a currency. Currency travels with the number so that adding EUR to USD
/// is a compile-time-shaped mistake rather than a silent one, and every amount is
/// rounded to the currency's minor unit at construction.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    /// <summary>ISO 4217 alphabetic code, upper case.</summary>
    public string Currency { get; }

    public static Money Zero(string currency) => Create(0m, currency).Value;

    public static Result<Money> Create(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            return Error.Validation("money.currency.invalid", "Currency must be a 3-letter ISO 4217 code.");
        }

        var normalised = currency.ToUpperInvariant();
        if (!CurrencyPrecision.TryGetValue(normalised, out var decimals))
        {
            return Error.Validation("money.currency.unsupported", $"Currency '{normalised}' is not supported.");
        }

        // Bankers' rounding would bias totals on half-cent values; commerce expects half-up.
        var rounded = Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
        return new Money(rounded, normalised);
    }

    /// <summary>For values already known to be valid (database materialisation, arithmetic results).</summary>
    public static Money FromTrusted(decimal amount, string currency) => new(amount, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(int quantity)
        => new(Math.Round(Amount * quantity, Decimals, MidpointRounding.AwayFromZero), Currency);

    public Money Multiply(decimal factor)
        => new(Math.Round(Amount * factor, Decimals, MidpointRounding.AwayFromZero), Currency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator *(Money money, int quantity) => money.Multiply(quantity);

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public bool IsNegative => Amount < 0;

    public bool IsZero => Amount == 0;

    private int Decimals => CurrencyPrecision.TryGetValue(Currency, out var d) ? d : 2;

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Cannot combine {Currency} with {other.Currency}.");
        }
    }

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(other);
        return Amount.CompareTo(other.Amount);
    }

    public override string ToString() => $"{Amount.ToString($"F{Decimals}", CultureInfo.InvariantCulture)} {Currency}";

    /// <summary>
    /// Minor-unit precision per currency. JOD/KWD/BHD are 3-decimal currencies and
    /// IQD has no minor unit in practice — rounding those to 2 would silently lose money.
    /// </summary>
    private static readonly Dictionary<string, int> CurrencyPrecision = new(StringComparer.Ordinal)
    {
        ["USD"] = 2, ["EUR"] = 2, ["GBP"] = 2, ["AED"] = 2, ["SAR"] = 2,
        ["EGP"] = 2, ["QAR"] = 2, ["TRY"] = 2, ["MAD"] = 2,
        ["JOD"] = 3, ["KWD"] = 3, ["BHD"] = 3, ["OMR"] = 3,
        ["JPY"] = 0, ["IQD"] = 0,
    };

    public static IReadOnlyCollection<string> SupportedCurrencies => CurrencyPrecision.Keys;
}
