using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sama.Shared.Infrastructure.Persistence;

/// <summary>
/// A domain event queued for publication, written in the same transaction as the state
/// change that produced it.
/// <para>
/// Publishing to Pub/Sub directly from a request handler has two failure modes and no good
/// answer to either: publish-then-commit can announce an order that was rolled back, and
/// commit-then-publish can lose the announcement if the process dies in between. Writing the
/// intent to the same database, in the same transaction, makes the two atomic; a separate
/// dispatcher then delivers at-least-once.
/// </para>
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>Wire event name, e.g. <c>ordering.order.placed.v1</c>.</summary>
    public required string Type { get; init; }

    /// <summary>JSON-serialised event payload.</summary>
    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Delivery attempts so far. Drives exponential backoff and the poison-message cutoff.</summary>
    public int Attempts { get; set; }

    /// <summary>When the dispatcher may next try. Lets a failing message step aside for healthy ones.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>Carried so a consumer can restore tenant context without trusting the payload.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>W3C traceparent, so the consumer's span joins the trace that produced the event.</summary>
    public string? TraceParent { get; init; }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(2000);
        builder.Property(m => m.TraceParent).HasMaxLength(64);

        // The dispatcher's only query is "unprocessed, due now, oldest first". A partial index
        // keeps that lookup on the small unprocessed tail instead of the whole history table.
        builder.HasIndex(m => new { m.NextAttemptAt, m.OccurredAt })
            .HasDatabaseName("ix_outbox_pending")
            .HasFilter("processed_at IS NULL");
    }
}
