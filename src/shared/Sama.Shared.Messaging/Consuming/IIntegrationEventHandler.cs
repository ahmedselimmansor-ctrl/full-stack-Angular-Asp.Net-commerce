using System.Runtime.CompilerServices;
using Sama.Shared.Kernel.Domain;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Shared.Messaging.Consuming;

/// <summary>
/// Handles one integration event type.
/// <para>
/// Pub/Sub delivers at least once, so a handler must be idempotent. The subscriber
/// deduplicates on message id, which covers redelivery of the <i>same</i> message; a handler
/// still has to tolerate a semantically equivalent event arriving twice (for example after a
/// producer replays a parked outbox row).
/// </para>
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

internal static class EventTypeName
{
    /// <summary>
    /// Reads the wire name off an event type without constructing one.
    /// <para>
    /// Every event overrides <c>EventType</c> with a constant, so an uninitialised instance
    /// returns the right value and no constructor or required member is involved. This keeps
    /// the wire name declared exactly once, on the event itself, instead of being repeated at
    /// every subscription registration where a typo would silently mean "never delivered".
    /// </para>
    /// </summary>
    public static string Of<TEvent>()
        where TEvent : IntegrationEvent
    {
        var probe = (IDomainEvent)RuntimeHelpers.GetUninitializedObject(typeof(TEvent));
        var name = probe.EventType;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                $"{typeof(TEvent).Name} does not declare a constant EventType, so it cannot be subscribed to.");
        }

        return name;
    }
}
