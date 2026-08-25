using System.ComponentModel.DataAnnotations;

namespace Sama.Shared.Infrastructure.Redis;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>StackExchange.Redis connection string. On GCP this is the Memorystore private endpoint.</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Prefixes every key this service writes. Keeps one Memorystore instance shared safely
    /// across services and environments, and makes a targeted FLUSH possible by pattern.
    /// </summary>
    [Required]
    public string InstanceName { get; set; } = "sama";

    /// <summary>Memorystore enforces TLS on the STANDARD tier; local Redis in Compose does not.</summary>
    public bool UseTls { get; set; }

    [Range(100, 30_000)]
    public int ConnectTimeoutMs { get; set; } = 5_000;

    /// <summary>
    /// Redis is a cache and a rate-limit store, not a system of record. A slow Redis should
    /// degrade the request, not hang it, so this stays well under the client HTTP timeout.
    /// </summary>
    [Range(100, 30_000)]
    public int OperationTimeoutMs { get; set; } = 1_000;
}
