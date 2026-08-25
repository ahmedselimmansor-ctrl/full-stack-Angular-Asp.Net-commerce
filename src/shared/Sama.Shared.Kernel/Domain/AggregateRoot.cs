namespace Sama.Shared.Kernel.Domain;

/// <summary>
/// Non-generic view of an aggregate's pending events. The persistence layer collects events
/// across every tracked entity regardless of id type, which a generic base alone cannot express.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

/// <summary>
/// Consistency boundary. Only aggregate roots are loaded and saved as units, and only
/// they raise domain events.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id) { }

    protected AggregateRoot() { }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Optimistic concurrency token, mapped to the PostgreSQL system column <c>xmin</c>.
    /// Two replicas updating the same aggregate collide loudly instead of one silently
    /// overwriting the other - which for stock counts and order state is a correctness bug.
    /// </summary>
    public uint Version { get; protected set; }

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
