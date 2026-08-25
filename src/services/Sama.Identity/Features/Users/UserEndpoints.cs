using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sama.Identity.Features.Auth;
using Sama.Identity.Persistence;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Infrastructure.RateLimiting;
using Sama.Shared.Kernel.Paging;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Permissions;

namespace Sama.Identity.Features.Users;

public sealed record UserSummary(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    bool EmailConfirmed,
    DateTimeOffset? LastLoginAt,
    IReadOnlyCollection<string> Roles);

public sealed record AssignRoleRequest(string Role);

/// <summary>
/// Back-office user administration. Every route here is permission-guarded, and the tenant
/// filter on <c>User</c> means an administrator of one storefront cannot enumerate another's
/// customers even by guessing ids.
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/v1/users").WithTags("Users");

        group.MapGet("/", ListAsync)
            .RequirePermission(SamaPermissions.Identity.ReadUser)
            .RateLimit("read")
            .WithSummary("Lists users in the caller's tenant.");

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(SamaPermissions.Identity.ReadUser)
            .RateLimit("read")
            .WithSummary("Returns one user.");

        group.MapPost("/{id:guid}/roles", AssignRoleAsync)
            .RequirePermission(SamaPermissions.Identity.AssignRole)
            .RateLimit("write")
            .WithSummary("Grants a role.");

        group.MapDelete("/{id:guid}/roles/{role}", RemoveRoleAsync)
            .RequirePermission(SamaPermissions.Identity.AssignRole)
            .RateLimit("write")
            .WithSummary("Revokes a role.");

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .RequirePermission(SamaPermissions.Identity.WriteUser)
            .RateLimit("write")
            .WithSummary("Deactivates an account and ends all of its sessions.");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        IdentityDbContext database,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20,
        string? search = null)
    {
        var request = new PageRequest(page, pageSize);

        // No IgnoreQueryFilters: the tenant filter on the model scopes this automatically.
        var query = database.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = Domain.User.Normalize(search);
            query = query.Where(u => u.NormalizedEmail.Contains(normalized) || u.DisplayName.Contains(search));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(u => new UserSummary(
                u.Id,
                u.Email,
                u.DisplayName,
                u.IsActive,
                u.EmailConfirmed,
                u.LastLoginAt,
                u.Roles.Select(r => r.Role).ToList()))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedResult<UserSummary>(users, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IdentityDbContext database,
        CancellationToken cancellationToken)
    {
        var user = await database.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserSummary(
                u.Id,
                u.Email,
                u.DisplayName,
                u.IsActive,
                u.EmailConfirmed,
                u.LastLoginAt,
                u.Roles.Select(r => r.Role).ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return user is null
            ? Error.NotFound("identity.user.notFound", "No such user.").ToProblem()
            : Results.Ok(user);
    }

    private static async Task<IResult> AssignRoleAsync(
        Guid id,
        AssignRoleRequest request,
        IdentityDbContext database,
        TokenService tokens,
        CancellationToken cancellationToken)
    {
        if (!SamaRoles.IsKnownRole(request.Role))
        {
            return Error.Validation(
                    "identity.role.unknown",
                    $"'{request.Role}' is not a known role. Valid roles: {string.Join(", ", SamaRoles.AllRoles)}.")
                .ToProblem();
        }

        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("identity.user.notFound", "No such user.").ToProblem();
        }

        user.AssignRole(request.Role);
        await database.SaveChangesAsync(cancellationToken);

        // The grant is only real once the holder's token reflects it; the aggregate rotated
        // the security stamp, and publishing it forces every existing token to be rejected.
        await tokens.PublishSecurityStampAsync(user, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> RemoveRoleAsync(
        Guid id,
        string role,
        IdentityDbContext database,
        ICurrentUser currentUser,
        TokenService tokens,
        CancellationToken cancellationToken)
    {
        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("identity.user.notFound", "No such user.").ToProblem();
        }

        // Guard against an administrator removing their own last administrative role and
        // locking the tenant out of its own back office.
        if (user.Id == currentUser.UserId
            && string.Equals(role, SamaRoles.Administrator, StringComparison.Ordinal))
        {
            return Error.Conflict(
                    "identity.role.selfRevoke",
                    "You cannot revoke your own administrator role. Ask another administrator to do it.")
                .ToProblem();
        }

        user.RemoveRole(role);
        await database.SaveChangesAsync(cancellationToken);
        await tokens.PublishSecurityStampAsync(user, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        IdentityDbContext database,
        ICurrentUser currentUser,
        TokenService tokens,
        CancellationToken cancellationToken)
    {
        if (id == currentUser.UserId)
        {
            return Error.Conflict("identity.user.selfDeactivate", "You cannot deactivate your own account.")
                .ToProblem();
        }

        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("identity.user.notFound", "No such user.").ToProblem();
        }

        user.Deactivate();
        await tokens.RevokeAllSessionsAsync(user, cancellationToken);

        return Results.NoContent();
    }
}
