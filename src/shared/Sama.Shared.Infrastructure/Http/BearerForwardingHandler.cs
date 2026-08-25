using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Sama.Shared.Infrastructure.Http;

/// <summary>
/// Forwards the caller's bearer token on service-to-service calls.
/// <para>
/// The alternative — each service holding a machine credential with broad rights — means an
/// internal call is no longer bounded by what the original caller was allowed to do, so one
/// compromised service can read anything any service can. Propagating the user's token keeps
/// the downstream check meaningful: the callee applies its own permission policy and its own
/// tenant filter to the real principal.
/// </para>
/// <para>
/// This is only safe because every hop stays inside the cluster and the token's audience is
/// the Sama API as a whole. It must never be attached to a client that calls a third party.
/// </para>
/// </summary>
public sealed class BearerForwardingHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Do not overwrite a token the caller set deliberately.
        if (request.Headers.Authorization is null)
        {
            var inbound = accessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();

            if (!string.IsNullOrEmpty(inbound))
            {
                request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, inbound);
            }
        }

        // Keep the trace joined across the hop even when the callee is not OTel-instrumented.
        var traceParent = System.Diagnostics.Activity.Current?.Id;
        if (!string.IsNullOrEmpty(traceParent) && !request.Headers.Contains("traceparent"))
        {
            request.Headers.TryAddWithoutValidation("traceparent", traceParent);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
