using System.Globalization;
using Sama.Shared.Infrastructure.Redis;
using StackExchange.Redis;

namespace Sama.Shared.Infrastructure.Idempotency;

public sealed class RedisIdempotencyStore(IRedisKeyspace keyspace) : IIdempotencyStore
{
    /// <summary>
    /// Claim-or-report, atomically. A plain GET-then-SET would let two concurrent retries
    /// both see "absent" and both charge the customer — precisely the case this exists to stop.
    /// </summary>
    private const string BeginScript = """
        local key         = KEYS[1]
        local fingerprint = ARGV[1]
        local ttl         = tonumber(ARGV[2])

        local existing = redis.call('HGETALL', key)
        if #existing == 0 then
            redis.call('HSET', key, 'state', 'in_progress', 'fp', fingerprint)
            redis.call('PEXPIRE', key, ttl)
            return { 'started' }
        end

        local map = {}
        for i = 1, #existing, 2 do map[existing[i]] = existing[i + 1] end

        if map['fp'] ~= fingerprint then
            return { 'mismatch' }
        end
        if map['state'] == 'completed' then
            return { 'completed', map['status'], map['body'] }
        end
        return { 'in_progress' }
        """;

    public async ValueTask<IdempotencyRecord> BeginAsync(
        string key,
        string requestFingerprint,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        var raw = await keyspace.Database.ScriptEvaluateAsync(
            BeginScript,
            [keyspace.Key("idem:" + key)],
            [
                requestFingerprint,
                ((long)retention.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            ]).WaitAsync(cancellationToken);

        var result = (RedisValue[]?)raw;
        if (result is null || result.Length == 0)
        {
            return new IdempotencyRecord(IdempotencyState.Started);
        }

        return (string?)result[0] switch
        {
            "started" => new IdempotencyRecord(IdempotencyState.Started),
            "mismatch" => new IdempotencyRecord(IdempotencyState.PayloadMismatch),
            "completed" => new IdempotencyRecord(
                IdempotencyState.Completed,
                result.Length > 1 && int.TryParse((string?)result[1], out var status) ? status : 200,
                result.Length > 2 ? (string?)result[2] : null),
            _ => new IdempotencyRecord(IdempotencyState.InProgress),
        };
    }

    public async ValueTask CompleteAsync(
        string key,
        int statusCode,
        string body,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        var redisKey = keyspace.Key("idem:" + key);
        var database = keyspace.Database;

        await database.HashSetAsync(redisKey,
        [
            new HashEntry("state", "completed"),
            new HashEntry("status", statusCode),
            new HashEntry("body", body),
        ]).WaitAsync(cancellationToken);

        await database.KeyExpireAsync(redisKey, retention).WaitAsync(cancellationToken);
    }

    public async ValueTask AbandonAsync(string key, CancellationToken cancellationToken = default)
        => await keyspace.Database.KeyDeleteAsync(keyspace.Key("idem:" + key)).WaitAsync(cancellationToken);
}
