using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Messaging.Publishing;

namespace Sama.Shared.Messaging.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Range(50, 60_000)]
    public int PollIntervalMs { get; set; } = 500;

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Attempts before a message is parked. Parked messages stay in the table with their last
    /// error for inspection — deleting them would silently drop a business event.
    /// </summary>
    [Range(1, 50)]
    public int MaxAttempts { get; set; } = 10;

    [Range(1, 3600)]
    public int MaxBackoffSeconds { get; set; } = 300;
}

/// <summary>
/// Drains the transactional outbox to the broker.
/// <para>
/// Every replica runs one of these, and they compete for the same rows. The claim query uses
/// <c>FOR UPDATE SKIP LOCKED</c>, so each row is handed to exactly one replica per pass and a
/// slow publish blocks only that row — no leader election, no distributed lock, and
/// throughput scales with replica count.
/// </para>
/// </summary>
public sealed partial class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    IClock clock,
    ILogger<OutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : SamaDbContext
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Outbox dispatcher for {Context} started (batch {BatchSize}, interval {Interval}ms).",
            typeof(TContext).Name, _options.BatchSize, _options.PollIntervalMs);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollIntervalMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Keep draining while batches come back full, so a burst is not metered out
                // one batch per tick.
                int dispatched;
                do
                {
                    dispatched = await DispatchBatchAsync(stoppingToken);
                }
                while (dispatched == _options.BatchSize && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop must survive anything, or the service silently stops publishing.
                logger.LogError(ex, "Outbox dispatch pass failed; retrying on the next tick.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Outbox dispatcher for {Context} stopped.", typeof(TContext).Name);
    }

    private async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        // This worker has no request and therefore no tenant; the filter would otherwise
        // reduce every query to the empty-tenant slice.
        database.TenantFilterEnabled = false;

        var schema = database.Model.GetDefaultSchema() ?? "public";
        EnsureSafeIdentifier(schema);

        // Connection resiliency is enabled, so EF refuses a hand-rolled transaction unless the
        // whole unit runs inside an execution strategy - otherwise a retry would resume
        // mid-transaction on a connection that no longer has one.
        //
        // A retry can republish messages whose commit was lost. That is the accepted trade of
        // at-least-once delivery; consumers already deduplicate on message id.
        var strategy = database.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
            await ClaimAndPublishAsync(database, publisher, schema, cancellationToken));
    }

    private async Task<int> ClaimAndPublishAsync(
        TContext database,
        IEventPublisher publisher,
        string schema,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Raw SQL because EF cannot express FOR UPDATE SKIP LOCKED, which is the whole point:
        // it is what lets N replicas share the queue without stepping on each other.
        //
        // The only interpolated value is the schema, taken from our own EF model and validated
        // above as an identifier. Everything caller-influenced ({0}, {1}) is bound as a real
        // parameter by EF rather than concatenated into the text.
        var claimSql =
            $$"""
              SELECT * FROM "{{schema}}"."outbox_messages"
              WHERE processed_at IS NULL
                AND next_attempt_at IS NOT NULL
                AND next_attempt_at <= {0}
              ORDER BY occurred_at
              LIMIT {1}
              FOR UPDATE SKIP LOCKED
              """;

        var pending = await database.OutboxMessages
            .FromSqlRaw(claimSql, clock.UtcNow, _options.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(
                    new PublishRequest(message.Id, message.Type, message.Payload, message.TenantId, message.TraceParent),
                    cancellationToken);

                message.ProcessedAt = clock.UtcNow;
                message.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordFailure(message, ex);
            }
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return pending.Count;
    }

    private void RecordFailure(OutboxMessage message, Exception exception)
    {
        message.Attempts++;
        message.LastError = Truncate(exception.Message, 2000);

        if (message.Attempts >= _options.MaxAttempts)
        {
            // Parked: a null next attempt takes it out of the claim query for good, while
            // leaving the row (and its error) in place for an operator to replay.
            message.NextAttemptAt = null;

            logger.LogError(
                exception,
                "Outbox message {MessageId} ({EventType}) parked after {Attempts} attempts.",
                message.Id, message.Type, message.Attempts);
            return;
        }

        var backoff = Math.Min(
            Math.Pow(2, message.Attempts),
            _options.MaxBackoffSeconds);

        // Jitter: without it, a broker outage synchronises every replica's retries into a
        // thundering herd the moment it recovers.
        var jitter = Random.Shared.NextDouble() * backoff * 0.2;
        message.NextAttemptAt = clock.UtcNow.AddSeconds(backoff + jitter);

        logger.LogWarning(
            exception,
            "Outbox message {MessageId} ({EventType}) failed on attempt {Attempts}; retrying at {NextAttempt}.",
            message.Id, message.Type, message.Attempts, message.NextAttemptAt);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    /// <summary>
    /// The schema comes from our own EF model rather than from user input, but it is
    /// interpolated into SQL — validating it keeps that true even if the model gains a
    /// configurable schema later.
    /// </summary>
    private static void EnsureSafeIdentifier(string identifier)
    {
        if (!IdentifierPattern().IsMatch(identifier))
        {
            throw new InvalidOperationException(
                $"Schema name '{identifier}' is not a valid SQL identifier.");
        }
    }

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
