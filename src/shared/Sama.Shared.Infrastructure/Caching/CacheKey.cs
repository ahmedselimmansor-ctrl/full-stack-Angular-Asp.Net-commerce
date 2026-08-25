using System.Globalization;

namespace Sama.Shared.Infrastructure.Caching;

/// <summary>
/// Builds cache keys. Every key is namespaced by service and, when the data is tenant-scoped,
/// by tenant — a key that omits the tenant is a cross-tenant data leak waiting for two
/// tenants to request the same product id.
/// </summary>
public sealed class CacheKey
{
    private readonly string _service;

    public CacheKey(string service) => _service = service;

    /// <summary>Key for data that is identical for every tenant (reference data, feature flags).</summary>
    public string Global(string resource, params ReadOnlySpan<object> parts)
        => Compose(_service, "global", resource, parts);

    /// <summary>Key for tenant-scoped data.</summary>
    public string ForTenant(Guid tenantId, string resource, params ReadOnlySpan<object> parts)
        => Compose(_service, "t:" + tenantId.ToString("N"), resource, parts);

    /// <summary>Key for data private to one user (their basket, their order list).</summary>
    public string ForUser(Guid tenantId, Guid userId, string resource, params ReadOnlySpan<object> parts)
        => Compose(_service, $"t:{tenantId:N}:u:{userId:N}", resource, parts);

    /// <summary>Invalidation tag for a single entity.</summary>
    public string Tag(string resource, object id)
        => string.Create(CultureInfo.InvariantCulture, $"{_service}:tag:{resource}:{id}");

    /// <summary>Invalidation tag covering every cached entry of a resource type.</summary>
    public string TagAll(string resource) => $"{_service}:tag:{resource}:*all";

    private static string Compose(string service, string scope, string resource, ReadOnlySpan<object> parts)
    {
        var builder = new System.Text.StringBuilder(64)
            .Append(service).Append(':')
            .Append(scope).Append(':')
            .Append(resource);

        foreach (var part in parts)
        {
            builder.Append(':').Append(Convert.ToString(part, CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
