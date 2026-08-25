using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Sama.Shared.Security.Authentication;

namespace Sama.Shared.Infrastructure.RateLimiting;

/// <summary>
/// Derives the bucket a request counts against. Shared by the in-service middleware and the
/// gateway so both partition traffic identically — otherwise the edge and the service would
/// disagree about who a caller is, and one of the two limits would be trivially evadable.
/// </summary>
public static class RateLimitPartitionKey
{
    /// <summary>
    /// Builds the bucket key. This reads <see cref="ConnectionInfo.RemoteIpAddress"/>, which is
    /// only the true client address once <c>ClientIpMiddleware</c> has applied the load
    /// balancer's trusted header — otherwise a caller could vary a spoofable header and mint a
    /// fresh bucket on every request.
    /// </summary>
    public static string Resolve(HttpContext context, RateLimitPartition partition)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (partition)
        {
            case RateLimitPartition.ApiKey:
                var apiKey = context.Request.Headers["X-Api-Key"].ToString();
                return string.IsNullOrEmpty(apiKey)
                    ? "ip:" + ClientIp(context)
                    : "key:" + Fingerprint(apiKey);

            case RateLimitPartition.IpAddress:
                return "ip:" + ClientIp(context);

            case RateLimitPartition.IpAndSubject:
                var subject = context.User.FindFirst(SamaClaimTypes.Subject)?.Value ?? "anon";
                return $"ip:{ClientIp(context)}|s:{subject}";

            case RateLimitPartition.User:
            default:
                var userId = context.User.FindFirst(SamaClaimTypes.Subject)?.Value;
                return string.IsNullOrEmpty(userId)
                    ? "ip:" + ClientIp(context)
                    : "u:" + userId;
        }
    }

    private static string ClientIp(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return "unknown";
        }

        // Collapse IPv6 clients to their /64. A single residential IPv6 allocation hands out
        // billions of addresses, so limiting per-address would be no limit at all.
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes).ToString() + "/64";
        }

        return address.ToString();
    }

    /// <summary>Hashes the API key so the raw secret never lands in a Redis key or a log line.</summary>
    private static string Fingerprint(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}
