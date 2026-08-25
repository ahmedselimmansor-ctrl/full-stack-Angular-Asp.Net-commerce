using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sama.Identity.Domain;
using Sama.Identity.Features.Auth;
using Sama.Identity.Features.Keys;
using Sama.Identity.Features.Users;
using Sama.Identity.Persistence;
using Sama.Shared.Infrastructure;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Messaging;
using Sama.Shared.Security;
using Sama.Shared.Security.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Identity holds the signing keys itself, so it must not point its bearer handler at its own
// HTTP metadata endpoint — that would make the service require itself to be already serving
// in order to authenticate its first request.
builder.AddSamaServiceDefaults("sama-identity", useRemoteAuthentication: false);

builder.Services.AddOptions<SigningOptions>()
    .Bind(builder.Configuration.GetSection(SigningOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<SigningKeyProvider>();
builder.Services.AddScoped<TokenService>();

// PBKDF2-HMAC-SHA512. The iteration count is deliberately above the framework default:
// hashing is the only thing standing between a leaked table and the plaintext passwords,
// and ~100ms per login is a cost users never notice and attackers pay per guess.
builder.Services.Configure<PasswordHasherOptions>(options =>
{
    options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
    options.IterationCount = 210_000;
});
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(ServiceLifetime.Singleton);

builder.Services.AddSamaDbContext<IdentityDbContext>(
    builder.Configuration, builder.Environment, IdentityDbContext.SchemaName);

builder.Services.AddSamaMessaging(builder.Configuration);
builder.Services.AddOutboxDispatcher<IdentityDbContext>(builder.Configuration);

// Local-key token validation, sharing the resource services' event handlers so the
// token-type and revocation checks stay identical everywhere.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Events = SamaJwtEvents.Create();
    });

// Hoisted out of the configure lambda: the options callback runs per scheme resolution, and
// allocating the same one-element array each time is pure waste.
string[] permittedAlgorithms = [SecurityAlgorithms.RsaSha256];

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<SigningKeyProvider, IOptionsMonitor<SigningOptions>>((options, keys, signing) =>
    {
        var signingOptions = signing.CurrentValue;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = signingOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = signingOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = SamaClaimTypes.Subject,
            RoleClaimType = SamaClaimTypes.Role,
            ValidAlgorithms = permittedAlgorithms,

            // Every key, not just the active one, so tokens signed before a rotation keep
            // validating until they expire.
            IssuerSigningKeys = keys.ValidationKeys,
        };
    });

var app = builder.Build();

app.UseSamaServiceDefaults();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapDiscoveryEndpoints();
app.MapSamaDefaultEndpoints();

await app.RunAsync();

/// <summary>Exposed so the integration test host can reference this entry point.</summary>
public partial class Program;
