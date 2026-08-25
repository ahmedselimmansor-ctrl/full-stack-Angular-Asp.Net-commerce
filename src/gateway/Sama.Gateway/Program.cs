using Microsoft.AspNetCore.Http.Features;
using Sama.Gateway;
using Sama.Shared.Infrastructure;
using Sama.Shared.Infrastructure.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// The gateway validates tokens against Identity's published keys exactly as a resource
// service does, so a forged or expired token is rejected at the edge rather than fanning out
// to a backend first.
builder.AddSamaServiceDefaults("sama-gateway");

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Caps the request body before it reaches a backend. Without a limit, a single large upload
// can occupy a proxy connection and a backend worker for as long as the client keeps sending.
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 8 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 8 * 1024 * 1024;

    // Slowloris defence: a client that opens a connection and dribbles bytes is disconnected
    // rather than holding a slot indefinitely.
    options.Limits.MinRequestBodyDataRate = new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(
        bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(10));
    options.Limits.MaxConcurrentConnections = 10_000;
    options.AddServerHeader = false;
});

var app = builder.Build();

// Paths that exist on a backend but must never be reachable through the public edge.
string[] InternalOnlyPaths = ["/api/v1/products/lookup"];

app.UseSamaServiceDefaults();

// Service-to-service endpoints are reachable inside the cluster but must not be routable from
// the internet. This runs ahead of the router: a proxied route carries an explicit Order and
// would otherwise win against any endpoint mapped alongside it.
app.Use(async (context, next) =>
{
    foreach (var blocked in InternalOnlyPaths)
    {
        if (context.Request.Path.StartsWithSegments(blocked, StringComparison.OrdinalIgnoreCase))
        {
            // 404 rather than 403: confirming the path exists would advertise a bulk
            // price-and-availability endpoint worth attacking.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
    }

    await next(context);
});

// Proxied routes carry their policy in YARP metadata, which the shared middleware cannot see.
app.UseGatewayRateLimiting();

app.MapReverseProxy();

app.MapSamaDefaultEndpoints();

await app.RunAsync();

public partial class Program;
