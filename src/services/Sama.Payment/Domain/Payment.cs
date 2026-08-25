using Sama.Shared.Kernel.Domain;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Payment.Domain;

public enum PaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Refunded = 3,
}

/// <summary>
/// An attempt to collect money for an order.
/// <para>
/// Payments are never deleted and never hard-updated out of a terminal state. This table is
/// the reconciliation record against the provider's own ledger, so its history has to survive
/// even when a row is superseded.
/// </para>
/// </summary>
public sealed class Payment : AggregateRoot<Guid>, ITenantScoped, IUserOwned, IAuditable
{
    private Payment() { }

    private Payment(Guid id, Guid tenantId, Guid customerId, Guid orderId, decimal amount, string currency)
        : base(id)
    {
        TenantId = tenantId;
        OwnerId = customerId;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Pending;
    }

    public Guid TenantId { get; private set; }

    /// <summary>The paying customer. Drives the <c>.own</c> access scope on the read endpoints.</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>Uniquely indexed: one order is charged at most once.</summary>
    public Guid OrderId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "USD";

    public PaymentStatus Status { get; private set; }

    /// <summary>The provider's own identifier, for reconciliation and refunds.</summary>
    public string? ProviderReference { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public static Payment Requested(Guid tenantId, Guid customerId, Guid orderId, decimal amount, string currency)
        => new(SamaId.New(), tenantId, customerId, orderId, amount, currency);

    public void MarkSucceeded(string providerReference, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending)
        {
            return;
        }

        Status = PaymentStatus.Succeeded;
        ProviderReference = providerReference;
        SettledAt = now;

        Raise(new PaymentSucceeded(OrderId, TenantId, Id, Amount, Currency));
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending)
        {
            return;
        }

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        SettledAt = now;

        Raise(new PaymentFailed(OrderId, TenantId, Id, reason));
    }

    public Result Refund()
    {
        if (Status != PaymentStatus.Succeeded)
        {
            return Result.Failure(Error.Conflict(
                "payment.refund.notSettled",
                "Only a settled payment can be refunded."));
        }

        Status = PaymentStatus.Refunded;
        return Result.Success();
    }

    public void SetCreated(DateTimeOffset at, Guid? by) { CreatedAt = at; CreatedBy = by; }
    public void SetUpdated(DateTimeOffset at, Guid? by) { UpdatedAt = at; UpdatedBy = by; }
}
