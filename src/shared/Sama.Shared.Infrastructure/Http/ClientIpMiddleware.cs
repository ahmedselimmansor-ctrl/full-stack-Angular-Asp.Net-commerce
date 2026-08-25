using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Sama.Shared.Infrastructure.Http;

public sealed class ClientIpOptions
{
    public const string SectionName = "ClientIp";

    /// <summary>
    /// Header the load balancer writes the true client address into. On GCP this is set by a
    /// backend-service custom request header (<c>{client_ip_address}</c>), which the LB
    /// <i>overwrites</i> on every request — unlike X-Forwarded-For, which it merely appends to.
    /// </summary>
    public string TrustedHeader { get; set; } = "X-Client-IP";

    /// <summary>
    /// Whether to trust that header at all. Off in local development, where nothing sets it
    /// and anything present would be attacker-supplied.
    /// </summary>
    public bool TrustProxyHeader { get; set; }
}

/// <summary>
/// Resolves the real client address and writes it back onto the connection, so rate limiting,
/// audit logs and Cloud Armor decisions all agree on who the caller is.
/// <para>
/// This deliberately does not use X-Forwarded-For. Google's external load balancer
/// <i>appends</i> to any X-Forwarded-For the client supplied, so the leftmost entry — the one
/// normally treated as the client — is fully attacker-controlled. A caller could mint a fresh
/// per-IP rate-limit bucket on every request just by varying that header. The custom header
/// the LB overwrites has no such hole; we trust that one and nothing else.
/// </para>
/// </summary>
public sealed class ClientIpMiddleware(RequestDelegate next, IOptions<ClientIpOptions> options)
{
    private readonly ClientIpOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_options.TrustProxyHeader)
        {
            var value = context.Request.Headers[_options.TrustedHeader].ToString();

            if (!string.IsNullOrEmpty(value) && IPAddress.TryParse(value.Trim(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }
        }

        await next(context);
    }
}

public static class ClientIpApplicationBuilderExtensions
{
    /// <summary>Must run before rate limiting and before request logging.</summary>
    public static IApplicationBuilder UseSamaClientIp(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ClientIpMiddleware>();
    }
}
