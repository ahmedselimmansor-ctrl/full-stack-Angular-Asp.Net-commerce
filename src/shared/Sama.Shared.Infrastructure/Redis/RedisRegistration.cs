using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Sama.Shared.Infrastructure.Redis;

public static class RedisRegistration
{
    /// <summary>
    /// Registers one multiplexed Redis connection for the process. StackExchange.Redis is
    /// designed to be shared — creating a connection per operation is the classic way to
    /// exhaust ports under load.
    /// </summary>
    public static IServiceCollection AddSamaRedis(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;
            var logger = provider.GetRequiredService<ILogger<ConnectionMultiplexer>>();

            var configurationOptions = ConfigurationOptions.Parse(options.ConnectionString);
            configurationOptions.Ssl = options.UseTls;
            configurationOptions.ConnectTimeout = options.ConnectTimeoutMs;
            configurationOptions.SyncTimeout = options.OperationTimeoutMs;
            configurationOptions.AsyncTimeout = options.OperationTimeoutMs;

            // Reconnect in the background instead of throwing for the lifetime of the process
            // after one blip — Memorystore maintenance failovers are routine.
            configurationOptions.AbortOnConnectFail = false;
            configurationOptions.ConnectRetry = 3;
            configurationOptions.ReconnectRetryPolicy = new ExponentialRetry(deltaBackOffMilliseconds: 200);
            configurationOptions.ClientName = $"{options.InstanceName}-{Environment.MachineName}";

            var multiplexer = ConnectionMultiplexer.Connect(configurationOptions);

            multiplexer.ConnectionFailed += (_, args) =>
            {
                if (logger.IsEnabled(LogLevel.Error))
                {
                    logger.LogError(
                        "Redis connection failed ({FailureType}) on {EndPoint}.",
                        args.FailureType,
                        args.EndPoint?.ToString() ?? "unknown");
                }
            };
            multiplexer.ConnectionRestored += (_, args) =>
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Redis connection restored on {EndPoint}.",
                        args.EndPoint?.ToString() ?? "unknown");
                }
            };

            return multiplexer;
        });

        services.AddSingleton<IRedisKeyspace, RedisKeyspace>();

        return services;
    }
}

/// <summary>
/// The Redis database plus this service's key namespace. Prefixing is explicit rather than
/// handled by <c>WithKeyPrefix</c>, because Lua scripts address keys through KEYS[] and a
/// transparently-prefixing wrapper makes it ambiguous which side applied the prefix.
/// </summary>
public interface IRedisKeyspace
{
    IDatabase Database { get; }

    /// <summary>Namespaces a key: <c>sama:ratelimit:user:123</c>.</summary>
    string Key(string suffix);
}

internal sealed class RedisKeyspace(IConnectionMultiplexer multiplexer, IOptions<RedisOptions> options)
    : IRedisKeyspace
{
    private readonly string _prefix = options.Value.InstanceName + ":";

    public IDatabase Database => multiplexer.GetDatabase();

    public string Key(string suffix) => _prefix + suffix;
}
