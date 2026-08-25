using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sama.Catalog.Persistence;
using Sama.Shared.Infrastructure.Redis;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Sama.IntegrationTests;

/// <summary>
/// Starts a real PostgreSQL and Redis once for the whole run.
///
/// These tests exist to prove things an in-memory substitute cannot: that a model-level query
/// filter actually translates to SQL, that a partial unique index behaves as intended, that a
/// Lua script is atomic under concurrency, and that <c>xmin</c> raises a concurrency conflict.
/// A fake would pass while production failed.
/// </summary>
public sealed class SamaFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("sama")
        .WithUsername("sama")
        .WithPassword("sama_test_password")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "sama"))
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public IRedisKeyspace Keyspace { get; private set; } = null!;

    public TestClock Clock { get; } = new();

    public async ValueTask InitializeAsync()
    {
        // In parallel: two container pulls in sequence is most of this suite's start-up cost.
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // allowAdmin permits FLUSHDB between tests. Safe here and nowhere else: this is a
        // throwaway container, whereas the production client deliberately withholds admin
        // commands so no application code can flush a shared cache.
        var redisOptions = ConfigurationOptions.Parse(_redis.GetConnectionString());
        redisOptions.AllowAdmin = true;

        Redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
        Keyspace = new TestKeyspace(Redis);

        await using var context = CreateCatalogContext(TestTenants.Acme);
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Redis.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    /// <summary>
    /// Builds a context scoped to one tenant and user, which is what a request would have.
    /// Passing null for the tenant models a background worker.
    /// </summary>
    public CatalogDbContext CreateCatalogContext(Guid? tenantId, Guid? userId = null)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;

        return new CatalogDbContext(
            options,
            new TestCurrentUser(userId, tenantId),
            new FixedTenantContext(tenantId),
            Clock);
    }

    /// <summary>Clears Redis between tests so one suite's keys cannot influence the next.</summary>
    public async Task ResetRedisAsync()
    {
        var endpoint = Redis.GetEndPoints().First();
        await Redis.GetServer(endpoint).FlushDatabaseAsync();
    }
}

public static class TestTenants
{
    public static readonly Guid Acme = Guid.Parse("0192f000-0000-7000-8000-00000000ac01");
    public static readonly Guid Globex = Guid.Parse("0192f000-0000-7000-8000-00000000b10b");
}

/// <summary>A clock the tests move by hand, so window and expiry behaviour is deterministic.</summary>
public sealed class TestClock : IClock
{
    private DateTimeOffset _now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);

    public void Set(DateTimeOffset to) => _now = to;
}

internal sealed class TestCurrentUser(Guid? userId, Guid? tenantId) : ICurrentUser
{
    public bool IsAuthenticated => userId is not null;
    public Guid? UserId => userId;
    public Guid? TenantId => tenantId;
    public string? Email => userId is null ? null : "test@sama.test";
    public IReadOnlySet<string> Roles => System.Collections.Frozen.FrozenSet<string>.Empty;
    public IReadOnlySet<string> Permissions => System.Collections.Frozen.FrozenSet<string>.Empty;
    public bool HasPermission(string permission) => false;
    public bool IsInRole(string role) => false;
    public Guid RequireUserId() => userId ?? throw new InvalidOperationException("no user");
    public Guid RequireTenantId() => tenantId ?? throw new InvalidOperationException("no tenant");
}

internal sealed class TestKeyspace(IConnectionMultiplexer multiplexer) : IRedisKeyspace
{
    public IDatabase Database => multiplexer.GetDatabase();

    public string Key(string suffix) => "test:" + suffix;
}

/// <summary>
/// One fixture shared by every test class. Starting a container pair per class would multiply
/// an already slow suite by the number of classes.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SamaCollection : ICollectionFixture<SamaFixture>
{
    public const string Name = "sama-infrastructure";
}
