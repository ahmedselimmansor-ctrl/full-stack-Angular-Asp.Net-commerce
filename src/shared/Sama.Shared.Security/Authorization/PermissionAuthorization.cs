using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Sama.Shared.Security.Authentication;

namespace Sama.Shared.Security.Authorization;

/// <summary>
/// Requires that the caller's token carries at least one of <see cref="Permissions"/>.
/// A single-element set is the common case; multi-element sets express the
/// <c>.own</c>/<c>.any</c> pairs, where the row filter narrows what is actually returned.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentOutOfRangeException.ThrowIfZero(permissions.Length);
        Permissions = permissions;
    }

    public IReadOnlyList<string> Permissions { get; }
}

/// <summary>
/// Checks permission claims directly off the token — no database round trip on the hot path.
/// That is what makes a short access-token lifetime the right trade: the window in which a
/// revoked grant still works is bounded by token expiry, not by a cache TTL.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        foreach (var claim in context.User.FindAll(SamaClaimTypes.Permission))
        {
            for (var i = 0; i < requirement.Permissions.Count; i++)
            {
                if (string.Equals(claim.Value, requirement.Permissions[i], StringComparison.Ordinal))
                {
                    context.Succeed(requirement);
                    return Task.CompletedTask;
                }
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Manufactures a policy per permission (or per permission set) on first use, so endpoints
/// can name any permission without each one being pre-registered in <c>AddAuthorization</c>.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    /// <summary>Policy-name prefix for "must hold this one permission".</summary>
    public const string Prefix = "perm:";

    /// <summary>Policy-name prefix for "must hold any of these pipe-separated permissions".</summary>
    public const string AnyPrefix = "permany:";

    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);

        string[] permissions;
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            permissions = [policyName[Prefix.Length..]];
        }
        else if (policyName.StartsWith(AnyPrefix, StringComparison.Ordinal))
        {
            permissions = policyName[AnyPrefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        if (permissions.Length == 0)
        {
            return Task.FromResult<AuthorizationPolicy?>(null);
        }

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissions))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

/// <summary>Controller-side counterpart of <c>RequirePermission</c> on minimal-API routes.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute : AuthorizeAttribute
{
    public RequiresPermissionAttribute(string permission)
        => Policy = PermissionPolicyProvider.Prefix + permission;
}
