using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sama.Shared.Kernel;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Shared.Messaging.Consuming;

/// <summary>
/// Maps a wire event type to the code that deserialises and handles it. Built once at
/// startup with the generic argument already closed, so message dispatch costs a dictionary
/// lookup and a delegate call rather than reflection per message.
/// </summary>
public sealed class EventSubscriptionRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, string, CancellationToken, Task>> _dispatchers
        = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> SubscribedEventTypes => _dispatchers.Keys;

    public void Register<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        var eventType = EventTypeName.Of<TEvent>();

        _dispatchers[eventType] = static async (provider, payload, cancellationToken) =>
        {
            var integrationEvent = JsonSerializer.Deserialize<TEvent>(payload, SamaJson.Default)
                ?? throw new InvalidOperationException(
                    $"Payload for {typeof(TEvent).Name} deserialised to null.");

            var handler = provider.GetRequiredService<IIntegrationEventHandler<TEvent>>();
            await handler.HandleAsync(integrationEvent, cancellationToken);
        };
    }

    public bool TryGetDispatcher(
        string eventType,
        out Func<IServiceProvider, string, CancellationToken, Task>? dispatcher)
        => _dispatchers.TryGetValue(eventType, out dispatcher);
}
