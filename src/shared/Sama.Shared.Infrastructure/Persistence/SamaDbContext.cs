using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Sama.Shared.Kernel;
using Sama.Shared.Kernel.Domain;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Shared.Infrastructure.Persistence;

/// <summary>
/// Base context for every Sama service. It carries the three cross-cutting guarantees that
/// must not be left to individual queries to remember:
/// <list type="number">
///   <item>tenant and soft-delete filters applied at the model level, not per query;</item>
///   <item>audit columns stamped on save;</item>
///   <item>domain events written to the outbox inside the same transaction as the change.</item>
/// </list>
/// </summary>
public abstract class SamaDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;

    protected SamaDbContext(
        DbContextOptions options,
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IClock clock)
        : base(options)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>PostgreSQL schema this service owns. Its DB role is granted on this schema alone.</summary>
    protected abstract string Schema { get; }

    /// <summary>
    /// Tenant used by the query filter. Read as a property (not captured in a closure) so EF
    /// re-evaluates it per query instead of baking the first request's tenant into the model.
    /// </summary>
    public Guid CurrentTenantId => _tenantContext.TenantId ?? Guid.Empty;

    /// <summary>
    /// Lets a trusted background worker read across tenants — the outbox dispatcher and
    /// migration jobs run with no user context. Never set this from a request path: the
    /// tenant filter is the last line of defence against cross-tenant reads.
    /// </summary>
    public bool TenantFilterEnabled { get; set; } = true;

    /// <summary>Service-specific entity configuration.</summary>
    protected abstract void ConfigureDomain(ModelBuilder modelBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        ConfigureDomain(modelBuilder);

        // Snapshot first: configuring an entity can add types to the model, and mutating the
        // collection while enumerating it would throw.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            // Derived types in a TPH hierarchy inherit the root's filter; adding another
            // would produce a contradictory AND.
            if (entityType.BaseType is not null || entityType.IsOwned())
            {
                continue;
            }

            ApplyFiltersMethod
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder]);

            ConfigureConcurrencyToken(entityType);
        }

        modelBuilder.UseSnakeCaseNames();
    }

    private static readonly MethodInfo ApplyFiltersMethod =
        typeof(SamaDbContext).GetMethod(nameof(ApplyEntityFilters), BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ApplyEntityFilters could not be located by reflection.");

    /// <summary>
    /// Installs the model-level row filters. These are the teeth of data access control: a
    /// hand-written LINQ query that forgets its tenant predicate still cannot see another
    /// tenant's rows, because the filter is welded to the model rather than to the call site.
    /// </summary>
    private void ApplyEntityFilters<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class
    {
        Expression<Func<TEntity, bool>>? filter = null;

        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
        {
            filter = entity => !EF.Property<bool>(entity, nameof(ISoftDeletable.IsDeleted));
        }

        if (typeof(ITenantScoped).IsAssignableFrom(typeof(TEntity)))
        {
            Expression<Func<TEntity, bool>> tenantFilter = entity =>
                !TenantFilterEnabled
                || EF.Property<Guid>(entity, nameof(ITenantScoped.TenantId)) == CurrentTenantId;

            filter = filter is null ? tenantFilter : filter.AndAlso(tenantFilter);
        }

        if (filter is not null)
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(filter);
        }
    }

    /// <summary>
    /// Maps an aggregate's <c>Version</c> to PostgreSQL's <c>xmin</c> system column, giving
    /// optimistic concurrency without a column of our own to maintain.
    /// </summary>
    private static void ConfigureConcurrencyToken(IMutableEntityType entityType)
    {
        var version = entityType.FindProperty("Version");
        if (version is null || version.ClrType != typeof(uint))
        {
            return;
        }

        version.SetColumnName("xmin");
        version.SetColumnType("xid");
        version.ValueGenerated = ValueGenerated.OnAddOrUpdate;
        version.IsConcurrencyToken = true;
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var actor = _currentUser.UserId;

        StampTenant();
        StampAuditColumns(now, actor);
        ConvertDeletesToSoftDeletes(now, actor);
        EnqueueDomainEvents(now);

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Backstop for new tenant-scoped rows that were constructed without a tenant. Without it,
    /// a row saved with an empty tenant id becomes invisible to every subsequent query.
    /// </summary>
    private void StampTenant()
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added || entry.Entity is not ITenantScoped)
            {
                continue;
            }

            var property = entry.Property(nameof(ITenantScoped.TenantId));
            if (property.CurrentValue is Guid current && current == Guid.Empty)
            {
                property.CurrentValue = tenantId;
            }
        }
    }

    private void StampAuditColumns(DateTimeOffset now, Guid? actor)
    {
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.SetCreated(now, actor);
                    break;
                case EntityState.Modified:
                    entry.Entity.SetUpdated(now, actor);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Turns <c>Remove()</c> on a soft-deletable entity into an update. Orders and products
    /// are referenced by financial records and by other services' read models; physically
    /// deleting them turns those references into dangling ids.
    /// </summary>
    private void ConvertDeletesToSoftDeletes(DateTimeOffset now, Guid? actor)
    {
        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            entry.State = EntityState.Modified;
            entry.Entity.MarkDeleted(now, actor);
        }
    }

    /// <summary>
    /// Queues an event that no aggregate raised, to be published by the same transaction as
    /// the surrounding change.
    /// <para>
    /// Most events belong to an aggregate and are raised on it. A few genuinely do not — a
    /// saga step that <i>declines</i> has, by definition, written no aggregate to hang an
    /// event off, yet the refusal still has to reach the orchestrator exactly as durably as a
    /// success would.
    /// </para>
    /// </summary>
    public void EnqueueIntegrationEvent(IDomainEvent integrationEvent, Guid? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        OutboxMessages.Add(new OutboxMessage
        {
            Id = SamaId.New(),
            Type = integrationEvent.EventType,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SamaJson.Default),
            OccurredAt = integrationEvent.OccurredAt,
            NextAttemptAt = _clock.UtcNow,
            TenantId = tenantId ?? _tenantContext.TenantId,
            TraceParent = Activity.Current?.Id,
        });
    }

    private void EnqueueDomainEvents(DateTimeOffset now)
    {
        var traceParent = Activity.Current?.Id;

        // Snapshot before building the messages. Adding an OutboxMessage attaches a new entity
        // to the change tracker, and mutating it while enumerating its entries throws.
        var sources = ChangeTracker.Entries<IHasDomainEvents>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .ToList();

        if (sources.Count == 0)
        {
            return;
        }

        var messages = new List<OutboxMessage>();

        foreach (var entry in sources)
        {
            var tenantId = entry.Entity is ITenantScoped scoped ? scoped.TenantId : (Guid?)null;

            foreach (var domainEvent in entry.Entity.DomainEvents)
            {
                messages.Add(new OutboxMessage
                {
                    Id = SamaId.New(),
                    Type = domainEvent.EventType,

                    // Serialised against the runtime type so the concrete event's members are
                    // written, not just those declared on IDomainEvent.
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SamaJson.Default),
                    OccurredAt = domainEvent.OccurredAt,
                    NextAttemptAt = now,
                    TenantId = tenantId,
                    TraceParent = traceParent,
                });
            }

            entry.Entity.ClearDomainEvents();
        }

        OutboxMessages.AddRange(messages);
    }
}

internal static class ExpressionCombiner
{
    /// <summary>
    /// ANDs two single-parameter predicates. The second lambda's parameter is rewritten to the
    /// first's — combining them without this produces an expression referencing a parameter
    /// that is not in scope, which EF rejects at translation time.
    /// </summary>
    public static Expression<Func<T, bool>> AndAlso<T>(
        this Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rewrittenRight = new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body);

        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(left.Body, rewrittenRight),
            parameter);
    }

    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
