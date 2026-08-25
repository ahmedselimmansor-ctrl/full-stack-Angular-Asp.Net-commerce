using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Sama.Shared.Infrastructure.RateLimiting;
using Yarp.ReverseProxy.Model;

namespace Sama.Gateway;

/// <summary>
/// Applies a rate-limit policy named in a YARP route's metadata.
/// <para>
/// The shared middleware reads endpoint metadata attached by minimal-API route builders;
/// proxied routes carry theirs on <see cref="RouteConfig.Metadata"/> instead, so the lookup
/// differs even though the limiter behind it is the same.
/// </para>
/// <para>
/// Limiting here as well as in each service is deliberate. The edge sheds abuse before it
/// costs a backend request at all, while the per-service limits still hold for anything
/// reaching a service by another path.
/// </para>
/// </summary>
public sealed class GatewayRateLimitMiddleware(
    RequestDelegate next,
    IRateLimiter limiter,
    IOptions<RateLimitOptions> options)
{
    public const string MetadataKey = "RateLimitPolicy";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var policyName = context.GetEndpoint()?
            .Metadata.GetMetadata<RouteModel>()?
            .Config.Metadata?
            .GetValueOrDefault(MetadataKey);

        if (string.IsNullOrEmpty(policyName))
        {
            await next(context);
            return;
        }

        if (!options.Value.Policies.TryGetValue(policyName, out var policy))
        {
            throw new InvalidOperationException(
                $"Route names rate-limit policy '{policyName}', which is not configured.");
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

public static class GatewayRateLimitExtensions
{
    public static IApplicationBuilder UseGatewayRateLimiting(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<GatewayRateLimitMiddleware>();
    }
}
