namespace Sama.Shared.Kernel.Domain;

/// <summary>
/// Something that happened inside an aggregate. Raised during the transaction and
/// written to the outbox in that same transaction, so publishing can never disagree
/// with the state change that caused it.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }

    /// <summary>Wire name, e.g. <c>ordering.order.placed.v1</c>. Drives Pub/Sub topic routing.</summary>
    string EventType { get; }
}

/// <summary>Convenience base that stamps the id and timestamp.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = SamaId.New();

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public abstract string EventType { get; }
}
