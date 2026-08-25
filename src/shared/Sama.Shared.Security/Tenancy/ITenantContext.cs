using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Sama.Shared.Security.Authentication;

namespace Sama.Shared.Security.Tenancy;

/// <summary>
/// Which storefront the current request belongs to.
/// <para>
/// This is deliberately separate from <see cref="ICurrentUser"/>. A shopper browsing the
/// catalogue is anonymous but still belongs to a tenant, so deriving the tenant purely from
/// token claims would leave every public page with no tenant — and therefore, under the
/// model-level tenant filter, no products.
/// </para>
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    Guid RequireTenantId();
}

public sealed class TenantResolutionOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>Used when the request host maps to nothing. Single-storefront deployments only set this.</summary>
    [Required]
    public Guid DefaultTenantId { get; set; }

    /// <summary>
    /// Host-to-tenant map, e.g. <c>{"shop.sama.com": "0192…"}</c>.
    /// <para>
    /// Resolution is host-based rather than from a client-supplied header on purpose: a
    /// caller who could name their own tenant could read another storefront's catalogue, and
    /// on the authenticated paths could aim writes at it.
    /// </para>
    /// </summary>
    public Dictionary<string, Guid> Hosts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Lets a non-HTTP entry point declare which tenant a unit of work belongs to.
/// <para>
/// Resolved only by the message dispatcher, never by application code — a handler that could
/// re-point its own tenant would defeat the isolation the query filter provides.
/// </para>
/// </summary>
public interface ITenantScopeSetter
{
    void SetTenant(Guid tenantId);
}

public sealed class ScopedTenantContext : ITenantContext, ITenantScopeSetter
{
    /// <summary>
    /// Resolved once, in the constructor, rather than on each read.
    /// <para>
    /// This is load-bearing, not an optimisation. <c>IHttpContextAccessor</c> is backed by an
    /// AsyncLocal, and several things legitimately run application code off the request's
    /// execution context — most notably <c>HybridCache</c>, which executes a cache-miss
    /// factory on a shared task so concurrent callers collapse into one database query. Inside
    /// that factory the ambient HttpContext is gone. A lazily-evaluated tenant would therefore
    /// resolve to null exactly there, and because the tenant feeds a model-level query filter,
    /// the symptom is not an exception but an empty result set: every product silently
    /// disappearing from the storefront.
    /// </para>
    /// <para>
    /// Capturing at construction pins the value while the request context is still present.
    /// The context is scoped, and its first consumer is always resolved inside the request
    /// after authentication has run.
    /// </para>
    /// </summary>
    public ScopedTenantContext(IHttpContextAccessor accessor, IOptions<TenantResolutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(options);

        _fromRequest = Resolve(accessor.HttpContext, options.Value);
    }

    private readonly Guid? _fromRequest;
    private Guid? _explicit;

    /// <summary>
    /// An explicitly-declared tenant wins. That is the message-consumer path: an integration
    /// event arrives outside any request, carrying the tenant it was produced for, and the
    /// handler's queries must be scoped to that tenant rather than to none.
    /// </summary>
    public Guid? TenantId => _explicit ?? _fromRequest;

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("An empty tenant id cannot be established.", nameof(tenantId));
        }

        _explicit = tenantId;
    }

    private static Guid? Resolve(HttpContext? context, TenantResolutionOptions options)
    {
        if (context is null)
        {
            // Not an HTTP request (a background worker, a test). Such callers must opt out of
            // the tenant filter deliberately rather than inherit an arbitrary tenant.
            return null;
        }

        // The token wins when present. It is server-signed, so it is the most trustworthy
        // statement of which tenant this caller belongs to - and it stops a signed-in user of
        // storefront A operating against storefront B by changing the Host header.
        var claim = context.User.FindFirst(SamaClaimTypes.TenantId)?.Value;
        if (Guid.TryParse(claim, out var fromToken))
        {
            return fromToken;
        }

        return options.Hosts.TryGetValue(context.Request.Host.Host, out var fromHost)
            ? fromHost
            : options.DefaultTenantId;
    }

    public Guid RequireTenantId()
        => TenantId ?? throw new InvalidOperationException("The request could not be attributed to a tenant.");
}

/// <summary>Fixed tenant for background workers, migrations and tests.</summary>
public sealed class FixedTenantContext(Guid? tenantId) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;

    public Guid RequireTenantId()
        => TenantId ?? throw new InvalidOperationException("This context has no tenant.");

    /// <summary>No tenant at all — background code must opt out of the filter explicitly.</summary>
    public static FixedTenantContext None { get; } = new(null);
}
