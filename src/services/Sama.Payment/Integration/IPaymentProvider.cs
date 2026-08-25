using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sama.Payment.Integration;

public sealed record PaymentInstruction(Guid PaymentId, Guid OrderId, decimal Amount, string Currency);

public sealed record PaymentOutcome(bool Succeeded, string? ProviderReference, string? FailureReason)
{
    public static PaymentOutcome Approved(string reference) => new(true, reference, null);

    public static PaymentOutcome Declined(string reason) => new(false, null, reason);
}

/// <summary>
/// The payment gateway.
/// <para>
/// Kept behind an interface so a real processor (Stripe, Checkout.com, a local acquirer) drops
/// in without touching the saga. Note what is <i>not</i> here: no card number, expiry or CVV.
/// The storefront tokenises card details directly with the provider, and Sama only ever sees
/// the resulting token — which is what keeps this service out of PCI-DSS scope.
/// </para>
/// </summary>
public interface IPaymentProvider
{
    Task<PaymentOutcome> ChargeAsync(PaymentInstruction instruction, CancellationToken cancellationToken);
}

public sealed class SimulatedProviderOptions
{
    public const string SectionName = "PaymentProvider";

    /// <summary>
    /// Amounts whose minor units equal this are declined, so the failure branch of the saga can
    /// be exercised on demand. Set to -1 to disable.
    /// </summary>
    [Range(-1, 99)]
    public int DeclineOnCents { get; set; } = 13;

    /// <summary>Simulated round-trip latency, so timeouts and concurrency behave realistically.</summary>
    [Range(0, 10_000)]
    public int LatencyMs { get; set; } = 150;
}

/// <summary>
/// Development and test stand-in for a real acquirer. Deterministic rather than random: a
/// flaky test suite teaches people to re-run rather than to investigate.
/// </summary>
public sealed class SimulatedPaymentProvider(
    IOptions<SimulatedProviderOptions> options,
    ILogger<SimulatedPaymentProvider> logger) : IPaymentProvider
{
    private readonly SimulatedProviderOptions _options = options.Value;

    public async Task<PaymentOutcome> ChargeAsync(
        PaymentInstruction instruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        if (_options.LatencyMs > 0)
        {
            await Task.Delay(_options.LatencyMs, cancellationToken);
        }

        var cents = (int)(Math.Abs(instruction.Amount) * 100 % 100);

        if (_options.DeclineOnCents >= 0 && cents == _options.DeclineOnCents)
        {
            logger.LogInformation(
                "Simulated decline for order {OrderId} ({Amount} {Currency}).",
                instruction.OrderId, instruction.Amount, instruction.Currency);

            return PaymentOutcome.Declined("The card was declined by the issuer.");
        }

        return PaymentOutcome.Approved($"sim_{instruction.PaymentId:N}");
    }
}
