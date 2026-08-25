using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Sama.Shared.Infrastructure.RateLimiting;

/// <summary>Names the rate-limit policy that guards an endpoint.</summary>
public sealed class RateLimitMetadata(string policyName)
{
    public string PolicyName { get; } = policyName;
}

/// <summary>Opts an endpoint out of rate limiting entirely (health and readiness probes).</summary>
public sealed class DisableRateLimitMetadata;

public static class RateLimitEndpointExtensions
{
    /// <summary>Applies a named policy: <c>app.MapGet("/products", …).RateLimit("read")</c>.</summary>
    public static TBuilder RateLimit<TBuilder>(this TBuilder builder, string policyName)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(b => b.Metadata.Add(new RateLimitMetadata(policyName)));
        return builder;
    }

    public static TBuilder DisableRateLimit<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(b => b.Metadata.Add(new DisableRateLimitMetadata()));
        return builder;
    }
}

/// <summary>
/// Applies the endpoint's rate-limit policy and annotates every response with RateLimit-*
/// headers, so a well-behaved client can back off before it starts getting rejected.
/// </summary>
public sealed class RateLimitMiddleware(
    RequestDelegate next,
    IRateLimiter limiter,
    IOptions<RateLimitOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<DisableRateLimitMetadata>() is not null)
        {
            await next(context);
            return;
        }

        var policyName = endpoint?.Metadata.GetMetadata<RateLimitMetadata>()?.PolicyName;
        if (policyName is null)
        {
            await next(context);
            return;
        }

        if (!options.Value.Policies.TryGetValue(policyName, out var policy))
        {
            throw new InvalidOperationException($"Rate-limit policy '{policyName}' is not configured.");
        }

        var partitionKey = RateLimitPartitionKey.Resolve(context, policy.Partition);
        var decision = await limiter.AcquireAsync(policyName, partitionKey, context.RequestAborted);

        RateLimitResponse.WriteHeaders(context, decision);

        if (decision.IsAllowed)
        {
            await next(context);
            return;
        }

        await RateLimitResponse.WriteTooManyRequestsAsync(context, decision);
    }
}

/// <summary>Shapes the 429 and the advisory headers, so the edge and the services agree.</summary>
public static class RateLimitResponse
{
    public static void WriteHeaders(HttpContext context, RateLimitDecision decision)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;
        headers["RateLimit-Limit"] = decision.Limit.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Remaining"] = decision.Remaining.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Reset"] =
            ((int)Math.Ceiling(decision.ResetAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
    }

    public static async Task WriteTooManyRequestsAsync(HttpContext context, RateLimitDecision decision)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "https://datatracker.ietf.org/doc/html/rfc6585#section-4",
            title = "Too Many Requests",
            status = StatusCodes.Status429TooManyRequests,
            detail = "You have sent too many requests. Retry after the interval in the Retry-After header.",
            traceId = context.TraceIdentifier,
        }));
    }
}

public static class RateLimitApplicationBuilderExtensions
{
    /// <summary>
    /// Must run after routing (it reads endpoint metadata) and after authentication
    /// (user-partitioned buckets need the resolved principal).
    /// </summary>
    public static IApplicationBuilder UseSamaRateLimiting(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<RateLimitMiddleware>();
    }
}
