using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sama.Identity.Domain;
using Sama.Identity.Features.Keys;
using Sama.Identity.Persistence;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Infrastructure.RateLimiting;
using Sama.Shared.Infrastructure.Redis;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Identity.Features.Auth;

public static class AuthEndpoints
{
    /// <summary>
    /// Returned for every failed sign-in regardless of cause. Distinguishing "no such user"
    /// from "wrong password" turns the login form into an account-enumeration oracle.
    /// </summary>
    private static Error InvalidCredentials =>
        Error.Unauthorized("auth.credentials.invalid", "The email address or password is incorrect.");

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RateLimit("auth-register")
            .WithSummary("Creates a customer account and returns a token pair.");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            // Metered inside the handler rather than by route metadata: the bucket has to be
            // keyed on the submitted username, which is in the body and not yet a claim.
            .RateLimit("auth-login-ip")
            .WithSummary("Exchanges credentials for a token pair.");

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .RateLimit("auth-refresh")
            .WithSummary("Rotates a refresh token for a new token pair.");

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .RateLimit("write")
            .WithSummary("Revokes the supplied refresh token and denylists the current access token.");

        group.MapPost("/logout-all", LogoutAllAsync)
            .RequireAuthorization()
            .RateLimit("write")
            .WithSummary("Signs the account out of every device.");

        group.MapGet("/me", GetProfileAsync)
            .RequireAuthorization()
            .RateLimit("read")
            .WithSummary("Returns the caller's profile, roles and effective permissions.");

        group.MapPost("/change-password", ChangePasswordAsync)
            .RequireAuthorization()
            .RateLimit("auth-login")
            .WithSummary("Changes the caller's password and signs out every other session.");

        return routes;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        HttpContext context,
        IValidator<RegisterRequest> validator,
        IdentityDbContext database,
        IPasswordHasher<User> passwordHasher,
        ITenantContext tenantContext,
        TokenService tokens,
        IOptions<SigningOptions> signingOptions,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (await Validate(validator, request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var tenantId = tenantContext.RequireTenantId();
        var normalized = User.Normalize(request.Email);

        var exists = await database.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == tenantId && u.NormalizedEmail == normalized && !u.IsDeleted, cancellationToken);

        if (exists)
        {
            // Same generic shape as a validation failure: a distinct "already registered"
            // response would confirm which addresses hold accounts.
            return Error.Conflict(
                    "auth.register.unavailable",
                    "That email address cannot be registered. Try signing in or resetting your password.")
                .ToProblem();
        }

        var user = User.Register(
            tenantId,
            request.Email.Trim(),
            request.DisplayName.Trim(),
            passwordHash: string.Empty);

        user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password), invalidateSessions: false);

        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);

        var issued = await tokens.IssueAsync(
            user,
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString(),
            familyId: null,
            cancellationToken);

        RefreshTokenCookie.Issue(context, issued.RefreshToken, signingOptions.Value.RefreshTokenDays, environment);

        return Results.Created($"/api/v1/users/{user.Id}", ToResponse(issued));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        IValidator<LoginRequest> validator,
        IdentityDbContext database,
        IPasswordHasher<User> passwordHasher,
        ITenantContext tenantContext,
        TokenService tokens,
        IRateLimiter limiter,
        IOptions<AuthOptions> authOptions,
        IOptions<SigningOptions> signingOptions,
        IHostEnvironment environment,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (await Validate(validator, request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var options = authOptions.Value;
        var now = clock.UtcNow;
        var tenantId = tenantContext.RequireTenantId();
        var normalized = User.Normalize(request.Email);

        // Credential stuffing rotates the username on one address and the address on one
        // username, so the bucket has to be keyed on both. The username comes from the body:
        // there is no token on a sign-in request, so a claim-based partition would silently
        // collapse every attempt from one address into a single shared bucket — throttling a
        // whole office together while doing nothing to slow an attacker who varies the name.
        var attemptKey = $"ip:{context.Connection.RemoteIpAddress}|u:{normalized}";
        var attempt = await limiter.AcquireAsync("auth-login", attemptKey, cancellationToken);

        if (!attempt.IsAllowed)
        {
            RateLimitResponse.WriteHeaders(context, attempt);
            await RateLimitResponse.WriteTooManyRequestsAsync(context, attempt);
            return Results.Empty;
        }

        // IgnoreQueryFilters: there is no authenticated tenant yet, so the model-level tenant
        // filter would match nothing. The tenant is instead pinned explicitly in the predicate.
        var user = await database.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                u => u.TenantId == tenantId && u.NormalizedEmail == normalized && !u.IsDeleted,
                cancellationToken);

        if (user is null)
        {
            // Verify against a throwaway hash anyway. Returning immediately would make the
            // "unknown account" path measurably faster and leak account existence by timing.
            passwordHasher.VerifyHashedPassword(null!, DummyHash.Value, request.Password);
            return InvalidCredentials.ToProblem();
        }

        if (user.IsLockedOut(now))
        {
            return Error.TooManyRequests(
                    "auth.account.locked",
                    "This account is temporarily locked after repeated failed sign-in attempts.")
                .ToProblem();
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedLogin(now, options.MaxFailedAccessAttempts, TimeSpan.FromMinutes(options.LockoutMinutes));
            await database.SaveChangesAsync(cancellationToken);
            return InvalidCredentials.ToProblem();
        }

        if (!user.IsActive)
        {
            return Error.Forbidden("auth.account.inactive", "This account has been deactivated.").ToProblem();
        }

        if (options.RequireConfirmedEmail && !user.EmailConfirmed)
        {
            return Error.Forbidden("auth.email.unconfirmed", "Confirm your email address before signing in.").ToProblem();
        }

        // The hashing parameters have been strengthened since this hash was written; upgrade
        // it now that the plaintext is in hand and verified.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(passwordHasher.HashPassword(user, request.Password), invalidateSessions: false);
        }

        user.RegisterSuccessfulLogin(now);
        await database.SaveChangesAsync(cancellationToken);

        var issued = await tokens.IssueAsync(
            user,
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString(),
            familyId: null,
            cancellationToken);

        RefreshTokenCookie.Issue(context, issued.RefreshToken, signingOptions.Value.RefreshTokenDays, environment);

        return Results.Ok(ToResponse(issued));
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest? request,
        HttpContext context,
        TokenService tokens,
        IOptions<SigningOptions> signingOptions,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        // Body first for native clients, cookie for the browser. The web client sends an empty
        // body precisely because it never holds the token.
        var presented = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(presented))
        {
            presented = RefreshTokenCookie.Read(context);
        }

        if (string.IsNullOrWhiteSpace(presented))
        {
            return Error.Validation("auth.refresh.missing", "A refresh token is required.").ToProblem();
        }

        var result = await tokens.RefreshAsync(
            presented,
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString(),
            cancellationToken);

        if (result.IsFailure)
        {
            // The presented token is dead; drop the cookie so the browser stops replaying it
            // and the user is sent cleanly back to sign-in.
            RefreshTokenCookie.Clear(context, environment);
            return result.Error.ToProblem();
        }

        // Rotation replaced the token, so the cookie has to follow it.
        RefreshTokenCookie.Issue(context, result.Value.RefreshToken, signingOptions.Value.RefreshTokenDays, environment);

        return Results.Ok(ToResponse(result.Value));
    }

    private static async Task<IResult> LogoutAsync(
        LogoutRequest? request,
        HttpContext context,
        TokenService tokens,
        RedisTokenRevocationStore revocations,
        IHostEnvironment environment,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var presented = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(presented))
        {
            presented = RefreshTokenCookie.Read(context);
        }

        if (!string.IsNullOrWhiteSpace(presented))
        {
            await tokens.RevokeAsync(presented, cancellationToken);
        }

        RefreshTokenCookie.Clear(context, environment);

        // Revoking the refresh token alone would leave the current access token usable for up
        // to its full lifetime; denylist it for whatever remains of that.
        var (tokenId, expiresAt) = TokenService.ReadTokenMetadata(context.User);
        if (tokenId is { } id && expiresAt is { } expiry)
        {
            await revocations.RevokeTokenAsync(id, expiry - clock.UtcNow, cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAllAsync(
        ICurrentUser currentUser,
        IdentityDbContext database,
        TokenService tokens,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Error.NotFound("identity.user.notFound", "The account no longer exists.").ToProblem();
        }

        await tokens.RevokeAllSessionsAsync(user, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetProfileAsync(
        ICurrentUser currentUser,
        IdentityDbContext database,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is null
            ? Error.NotFound("identity.user.notFound", "The account no longer exists.").ToProblem()
            : Results.Ok(TokenService.ToProfile(user));
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        IValidator<ChangePasswordRequest> validator,
        ICurrentUser currentUser,
        IdentityDbContext database,
        IPasswordHasher<User> passwordHasher,
        TokenService tokens,
        CancellationToken cancellationToken)
    {
        if (await Validate(validator, request, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var userId = currentUser.RequireUserId();
        var user = await database.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Error.NotFound("identity.user.notFound", "The account no longer exists.").ToProblem();
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Error.Unauthorized("auth.password.incorrect", "The current password is incorrect.").ToProblem();
        }

        user.SetPasswordHash(passwordHasher.HashPassword(user, request.NewPassword));

        // Rotating the stamp above already invalidates access tokens; this also kills the
        // refresh tokens, so a session opened with the old password cannot renew itself.
        await tokens.RevokeAllSessionsAsync(user, cancellationToken);

        return Results.NoContent();
    }

    private static TokenResponse ToResponse(IssuedTokens issued)
        => new(issued.AccessToken, issued.RefreshToken, "Bearer", issued.ExpiresInSeconds);

    private static async Task<IResult?> Validate<T>(
        IValidator<T> validator,
        T request,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        if (result.IsValid)
        {
            return null;
        }

        var details = result.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        return Error.Validation("request.invalid", "One or more fields are invalid.", details).ToProblem();
    }

    /// <summary>
    /// A real hash of a throwaway password, computed once. Used to spend the same work on an
    /// unknown account as on a known one.
    /// </summary>
    private static class DummyHash
    {
        public static string Value { get; } =
            new PasswordHasher<User>().HashPassword(null!, "not-a-real-password-000000");
    }
}
