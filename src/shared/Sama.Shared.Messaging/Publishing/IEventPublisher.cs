namespace Sama.Shared.Messaging.Publishing;

/// <summary>An outbox row on its way to the broker.</summary>
public sealed record PublishRequest(
    Guid MessageId,
    string EventType,
    string Payload,
    Guid? TenantId,
    string? TraceParent);

/// <summary>
/// Transport for integration events. Only the outbox dispatcher calls this — application
/// code raises a domain event on an aggregate and lets the outbox handle delivery, so a
/// publish can never succeed for a transaction that later rolled back.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(PublishRequest request, CancellationToken cancellationToken = default);
}
