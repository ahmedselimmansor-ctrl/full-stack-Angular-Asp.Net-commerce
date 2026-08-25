using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sama.Payment.Persistence;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Messaging.Consuming;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Payment.Integration;

/// <summary>
/// Charges an order once the stock behind it is held.
/// <para>
/// The sequencing matters: payment happens only after inventory has confirmed, so the common
/// failure — items sold out between browsing and checkout — costs a rejection rather than a
/// charge followed by a refund.
/// </para>
/// </summary>
public sealed class ChargeOnPaymentRequested(
    PaymentDbContext database,
    IPaymentProvider provider,
    IClock clock,
    ILogger<ChargeOnPaymentRequested> logger) : IIntegrationEventHandler<PaymentRequested>
{
    public async Task HandleAsync(PaymentRequested integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // Guard before touching the provider. Charging is the one irreversible side effect in
        // the saga, so a redelivered request must be recognised before any money moves.
        var existing = await database.Payments
            .FirstOrDefaultAsync(p => p.OrderId == integrationEvent.OrderId, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "Order {OrderId} already has payment {PaymentId} ({Status}); ignoring redelivery.",
                integrationEvent.OrderId, existing.Id, existing.Status);
            return;
        }

        var payment = Domain.Payment.Requested(
            integrationEvent.TenantId,
            integrationEvent.CustomerId,
            integrationEvent.OrderId,
            integrationEvent.Amount,
            integrationEvent.Currency);

        // Persist the intent before calling out. If the process dies mid-charge, this row is
        // what lets reconciliation find the orphan instead of it vanishing entirely.
        database.Payments.Add(payment);
        await database.SaveChangesAsync(cancellationToken);

        var outcome = await provider.ChargeAsync(
            new PaymentInstruction(payment.Id, payment.OrderId, payment.Amount, payment.Currency),
            cancellationToken);

        if (outcome.Succeeded)
        {
            payment.MarkSucceeded(outcome.ProviderReference ?? string.Empty, clock.UtcNow);
        }
        else
        {
            payment.MarkFailed(outcome.FailureReason ?? "The payment was declined.", clock.UtcNow);
        }

        // The resulting event goes to the outbox in the same transaction as the status change,
        // so Ordering's view of the payment can never disagree with this service's.
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Payment {PaymentId} for order {OrderId} {Status}.",
            payment.Id, payment.OrderId, payment.Status);
    }
}
