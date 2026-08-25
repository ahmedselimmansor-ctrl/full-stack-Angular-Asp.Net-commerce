using System.Collections.Concurrent;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Shared.Messaging.Publishing;

/// <summary>
/// Publishes to Google Cloud Pub/Sub. Publisher clients are expensive to build and are
/// thread-safe, so one is cached per topic for the life of the process.
/// </summary>
public sealed class PubSubEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly PubSubOptions _options;
    private readonly ILogger<PubSubEventPublisher> _logger;
    private readonly ConcurrentDictionary<string, Task<PublisherClient>> _publishers = new(StringComparer.Ordinal);

    public PubSubEventPublisher(IOptions<PubSubOptions> options, ILogger<PubSubEventPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(PublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var topicId = $"{_options.TopicPrefix}-{EventTopics.ForEventType(request.EventType)}";
        var publisher = await _publishers
            .GetOrAdd(topicId, CreatePublisherAsync)
            .WaitAsync(cancellationToken);

        var message = new PubsubMessage
        {
            Data = ByteString.CopyFromUtf8(request.Payload),

            // Subscriptions filter on these, so a consumer only receives the types it handles.
            Attributes =
            {
                ["eventType"] = request.EventType,
                ["messageId"] = request.MessageId.ToString("N"),
            },

            // Pub/Sub guarantees ordering only within an ordering key. Keying on the tenant
            // keeps one tenant's events in sequence without serialising the whole topic.
            OrderingKey = request.TenantId?.ToString("N") ?? string.Empty,
        };

        if (request.TraceParent is { Length: > 0 } traceParent)
        {
            message.Attributes["traceparent"] = traceParent;
        }

        if (request.TenantId is { } tenantId)
        {
            message.Attributes["tenantId"] = tenantId.ToString("N");
        }

        var serverId = await publisher.PublishAsync(message);

        _logger.LogDebug(
            "Published {EventType} ({MessageId}) to {Topic} as {ServerId}.",
            request.EventType, request.MessageId, topicId, serverId);
    }

    private async Task<PublisherClient> CreatePublisherAsync(string topicId)
    {
        var topicName = TopicName.FromProjectTopic(_options.ProjectId, topicId);
        var builder = new PublisherClientBuilder
        {
            TopicName = topicName,

            // Ordering keys are set on every message; without this the client rejects them.
            Settings = new PublisherClient.Settings
            {
                EnableMessageOrdering = true,
                BatchingSettings = new BatchingSettings(
                    elementCountThreshold: 100,
                    byteCountThreshold: 1024 * 1024,

                    // The dispatcher already batches by polling interval; a long delay here
                    // would stack on top of that and slow the saga noticeably.
                    delayThreshold: TimeSpan.FromMilliseconds(50)),
            },
        };

        if (!string.IsNullOrWhiteSpace(_options.EmulatorHost))
        {
            builder.EmulatorDetection = EmulatorDetection.EmulatorOrProduction;
        }

        return await builder.BuildAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _publishers.Values)
        {
            try
            {
                var publisher = await entry;

                // Flushes anything still batched. Skipping this loses the tail of the buffer
                // on a rolling deploy.
                await publisher.ShutdownAsync(TimeSpan.FromSeconds(10));
            }
            catch (RpcException ex)
            {
                _logger.LogWarning(ex, "A Pub/Sub publisher did not shut down cleanly.");
            }
        }

        _publishers.Clear();
    }
}
