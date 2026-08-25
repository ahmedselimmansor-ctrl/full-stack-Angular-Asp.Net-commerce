using System.ComponentModel.DataAnnotations;

namespace Sama.Shared.Messaging.Publishing;

public sealed class PubSubOptions
{
    public const string SectionName = "PubSub";

    [Required]
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Prefixed onto every topic and subscription name, e.g. <c>sama-prod</c>. Keeps
    /// environments that share a project from delivering each other's events.
    /// </summary>
    [Required]
    public string TopicPrefix { get; set; } = "sama";

    /// <summary>
    /// Emulator endpoint for local development (<c>localhost:8085</c>). When set, the client
    /// connects insecurely and skips credential discovery.
    /// </summary>
    public string? EmulatorHost { get; set; }

    /// <summary>Concurrent messages a subscriber will process before applying backpressure.</summary>
    [Range(1, 1000)]
    public int MaxConcurrentMessages { get; set; } = 16;

    /// <summary>How long a consumer may hold a message before Pub/Sub redelivers it.</summary>
    [Range(10, 600)]
    public int AckDeadlineSeconds { get; set; } = 60;
}
