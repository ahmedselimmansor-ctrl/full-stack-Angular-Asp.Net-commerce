using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sama.Shared.Infrastructure.Redis;
using Sama.Shared.Messaging.Publishing;
using StackExchange.Redis;

namespace Sama.Shared.Messaging.Consuming;

public sealed class SubscriberOptions
{
    public const string SectionName = "Subscriber";

    /// <summary>
    /// Subscription id this service pulls from, without the environment prefix — typically
    /// "<c>{service}-{topic}</c>", e.g. <c>inventory-ordering</c>. Terraform creates it with
    /// an attribute filter matching only the event types this service registered a handler for.
    /// </summary>
    public string[] Subscriptions { get; set; } = [];

    /// <summary>
    /// How long a processed message id is remembered for deduplication. Must comfortably
    /// exceed the subscription's retention so a late redelivery is still recognised.
    /// </summary>
    public int DeduplicationRetentionHours { get; set; } = 24;
}

/// <summary>
/// Pulls integration events and dispatches them to registered handlers.
/// <para>
/// Acknowledgement is the contract: <c>Ack</c> means the work is durably done, <c>Nack</c>
/// returns the message for redelivery under the subscription's retry policy, and repeated
/// failures land it in the dead-letter topic. A handler that throws must therefore leave no
/// half-applied state behind — hence the transaction boundary inside the handler, not here.
/// </para>
/// </summary>
public sealed class PubSubSubscriberService(
    IServiceScopeFactory scopeFactory,
    EventSubscriptionRegistry registry,
    IRedisKeyspace keyspace,
    IOptions<PubSubOptions> pubSubOptions,
    IOptions<SubscriberOptions> subscriberOptions,
    ILogger<PubSubSubscriberService> logger) : BackgroundService
{
    private readonly PubSubOptions _pubSub = pubSubOptions.Value;
    private readonly SubscriberOptions _subscriber = subscriberOptions.Value;
    private readonly List<SubscriberClient> _clients = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_subscriber.Subscriptions.Length == 0)
        {
            logger.LogInformation("No Pub/Sub subscriptions configured; the subscriber is idle.");
            return;
        }

        // Each subscription runs its own supervised loop, and nothing thrown inside one is
        // allowed to escape this method.
        //
        // The default BackgroundServiceExceptionBehavior is StopHost: an exception here would
        // terminate the entire process. For a message consumer that is the wrong trade - a
        // broker that is unreachable, or credentials that are not yet mounted, would take down
        // an HTTP API that is otherwise perfectly able to serve reads. Degrade and retry instead.
        var loops = _subscriber.Subscriptions
            .Select(subscriptionId => RunSubscriptionAsync(subscriptionId, stoppingToken))
            .ToArray();

        await Task.WhenAll(loops);
    }

    private async Task RunSubscriptionAsync(string subscriptionId, CancellationToken stoppingToken)
    {
        var qualified = $"{_pubSub.TopicPrefix}-{subscriptionId}";
        var backoff = TimeSpan.FromSeconds(2);
        var maxBackoff = TimeSpan.FromMinutes(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            SubscriberClient? client = null;

            try
            {
                client = await BuildClientAsync(qualified, stoppingToken);

                lock (_clients)
                {
                    _clients.Add(client);
                }

                logger.LogInformation("Subscribed to {Subscription}.", qualified);

                // Returns only when the client is stopped, so reaching the next line means a
                // deliberate shutdown rather than a failure.
                await client.StartAsync((message, ct) => HandleAsync(qualified, message, ct));

                backoff = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Subscription {Subscription} failed; retrying in {Backoff}s. The HTTP API is unaffected.",
                    qualified, backoff.TotalSeconds);

                try
                {
                    await Task.Delay(backoff, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, maxBackoff.Ticks));
            }
            finally
            {
                if (client is not null)
                {
                    lock (_clients)
                    {
                        _clients.Remove(client);
                    }
                }
            }
        }

        logger.LogInformation("Subscription loop for {Subscription} stopped.", qualified);
    }

    private async Task<SubscriberClient> BuildClientAsync(string qualified, CancellationToken cancellationToken)
    {
        var subscriptionName = SubscriptionName.FromProjectSubscription(_pubSub.ProjectId, qualified);

        var builder = new SubscriberClientBuilder
        {
            SubscriptionName = subscriptionName,
            Settings = new SubscriberClient.Settings
            {
                AckDeadline = TimeSpan.FromSeconds(_pubSub.AckDeadlineSeconds),

                // Bounds how many handlers run at once, which in turn bounds database
                // connection use - an unbounded subscriber will exhaust the pool.
                FlowControlSettings = new FlowControlSettings(
                    maxOutstandingElementCount: _pubSub.MaxConcurrentMessages,
                    maxOutstandingByteCount: 32 * 1024 * 1024),
            },
        };

        if (!string.IsNullOrWhiteSpace(_pubSub.EmulatorHost))
        {
            builder.EmulatorDetection = EmulatorDetection.EmulatorOrProduction;
        }

        return await builder.BuildAsync(cancellationToken);
    }

    private async Task<SubscriberClient.Reply> HandleAsync(
        string subscription,
        PubsubMessage message,
        CancellationToken cancellationToken)
    {
        var eventType = message.Attributes.GetValueOrDefault("eventType");
        var messageId = message.Attributes.GetValueOrDefault("messageId") ?? message.MessageId;

        if (string.IsNullOrEmpty(eventType))
        {
            logger.LogWarning("Message {MessageId} has no eventType attribute; acknowledging to drop it.", messageId);
            return SubscriberClient.Reply.Ack;
        }

        if (!registry.TryGetDispatcher(eventType, out var dispatcher) || dispatcher is null)
        {
            // A filter should have kept this away from us. Acking prevents an endless
            // redelivery loop for an event this service will never handle.
            logger.LogWarning("No handler registered for {EventType}; acknowledging.", eventType);
            return SubscriberClient.Reply.Ack;
        }

        var dedupeKey = keyspace.Key($"consumed:{subscription}:{messageId}");
        var retention = TimeSpan.FromHours(_subscriber.DeduplicationRetentionHours);

        // Claim before handling. Redelivery of an already-processed message is normal
        // operation for Pub/Sub, not an error, so it is dropped quietly.
        var claimed = await keyspace.Database.StringSetAsync(dedupeKey, "1", retention, when: When.NotExists);
        if (!claimed)
        {
            logger.LogDebug("Message {MessageId} ({EventType}) was already processed; skipping.", messageId, eventType);
            return SubscriberClient.Reply.Ack;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var payload = message.Data.ToStringUtf8();

            await dispatcher(scope.ServiceProvider, payload, cancellationToken);

            return SubscriberClient.Reply.Ack;
        }
        catch (Exception ex)
        {
            // Release the claim so the redelivery is actually retried rather than being
            // mistaken for a duplicate and dropped.
            await keyspace.Database.KeyDeleteAsync(dedupeKey);

            logger.LogError(ex, "Handler for {EventType} ({MessageId}) failed; nacking for redelivery.", eventType, messageId);
            return SubscriberClient.Reply.Nack;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        SubscriberClient[] snapshot;
        lock (_clients)
        {
            snapshot = [.. _clients];
        }

        foreach (var client in snapshot)
        {
            try
            {
                // Drain rather than nack: stop pulling, then let in-flight handlers finish.
                // NackImmediately would return a whole in-flight batch for redelivery on every
                // rolling deploy, multiplying duplicate work exactly when load is already shifting.
                await client.StopAsync(
                    new SubscriberClient.ShutdownOptions
                    {
                        Mode = SubscriberClient.ShutdownMode.WaitForProcessing,
                        Timeout = TimeSpan.FromSeconds(20),
                    },
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "A Pub/Sub subscriber did not stop cleanly.");
            }
        }

        await base.StopAsync(cancellationToken);
    }
}
