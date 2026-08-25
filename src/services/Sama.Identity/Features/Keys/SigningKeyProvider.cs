using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Sama.Identity.Features.Keys;

public sealed class SigningKeyMaterial
{
    /// <summary>Published as <c>kid</c> so a validator can pick the right key during rotation.</summary>
    [Required]
    public string KeyId { get; set; } = string.Empty;

    /// <summary>PKCS#8 PEM RSA private key. Delivered from Secret Manager, never committed.</summary>
    [Required]
    public string PrivateKeyPem { get; set; } = string.Empty;
}

public sealed class SigningOptions
{
    public const string SectionName = "Signing";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = "sama-api";

    /// <summary>
    /// Access-token lifetime. Short by design: permissions are baked into the token, so this
    /// is also the worst-case delay before a revoked grant stops working.
    /// </summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh-token lifetime. Rotation means a stolen one is usable only until its next legitimate use.</summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>
    /// Which key signs new tokens. Rotation is: publish the new key here while leaving the old
    /// one in <see cref="Keys"/>, so tokens signed by the old key keep validating until they expire.
    /// </summary>
    public string ActiveKeyId { get; set; } = string.Empty;

    public List<SigningKeyMaterial> Keys { get; init; } = [];
}

/// <summary>
/// Owns the RSA keys that sign access tokens and publishes their public halves as a JWKS.
/// <para>
/// Asymmetric (RS256) rather than symmetric (HS256) is the load-bearing choice here: with a
/// shared secret, every one of the six resource services would need the key that <i>mints</i>
/// tokens, so compromising any of them would let an attacker forge an administrator. With
/// RSA they hold only the public key and can verify but never issue.
/// </para>
/// </summary>
public sealed class SigningKeyProvider : IDisposable
{
    private readonly List<(string KeyId, RSA Rsa)> _keys = [];
    private readonly SigningCredentials _activeCredentials;
    private readonly SigningOptions _options;

    public SigningKeyProvider(
        IOptions<SigningOptions> options,
        IHostEnvironment environment,
        ILogger<SigningKeyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;

        if (_options.Keys.Count == 0)
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "No signing keys are configured. Sama.Identity cannot issue tokens. " +
                    "Provide Signing:Keys from Secret Manager before starting the service.");
            }

            // Development only. An ephemeral key means tokens die with the process, which is
            // fine locally and catastrophic in production — hence the hard failure above.
            var ephemeral = RSA.Create(2048);
            _keys.Add(("dev-ephemeral", ephemeral));
            _options.ActiveKeyId = "dev-ephemeral";

            logger.LogWarning(
                "No signing keys configured; generated an ephemeral development key. " +
                "Every restart invalidates all issued tokens.");
        }
        else
        {
            foreach (var material in _options.Keys)
            {
                var rsa = RSA.Create();
                rsa.ImportFromPem(material.PrivateKeyPem);

                if (rsa.KeySize < 2048)
                {
                    throw new InvalidOperationException(
                        $"Signing key '{material.KeyId}' is {rsa.KeySize}-bit; RSA keys below 2048 bits are not acceptable.");
                }

                _keys.Add((material.KeyId, rsa));
            }
        }

        var activeKeyId = string.IsNullOrWhiteSpace(_options.ActiveKeyId)
            ? _keys[0].KeyId
            : _options.ActiveKeyId;

        var active = _keys.FirstOrDefault(k => string.Equals(k.KeyId, activeKeyId, StringComparison.Ordinal));
        if (active.Rsa is null)
        {
            throw new InvalidOperationException(
                $"Signing:ActiveKeyId is '{activeKeyId}', which is not among the configured keys.");
        }

        _activeCredentials = new SigningCredentials(
            new RsaSecurityKey(active.Rsa) { KeyId = active.KeyId },
            SecurityAlgorithms.RsaSha256);
    }

    public SigningCredentials ActiveCredentials => _activeCredentials;

    /// <summary>Every key a token might legitimately be signed with, including retired ones.</summary>
    public IReadOnlyList<SecurityKey> ValidationKeys =>
        [.. _keys.Select(k => new RsaSecurityKey(k.Rsa) { KeyId = k.KeyId })];

    /// <summary>Public halves, in the JWKS shape other services fetch and cache.</summary>
    public JsonWebKeySet BuildJsonWebKeySet()
    {
        var keySet = new JsonWebKeySet();

        foreach (var (keyId, rsa) in _keys)
        {
            // false: export the public parameters only. Exporting the private key here would
            // publish the ability to mint tokens at a public, unauthenticated endpoint.
            var parameters = rsa.ExportParameters(includePrivateParameters: false);

            keySet.Keys.Add(new JsonWebKey
            {
                Kty = "RSA",
                Use = "sig",
                Alg = SecurityAlgorithms.RsaSha256,
                Kid = keyId,
                N = Base64UrlEncoder.Encode(parameters.Modulus),
                E = Base64UrlEncoder.Encode(parameters.Exponent),
            });
        }

        return keySet;
    }

    public void Dispose()
    {
        foreach (var (_, rsa) in _keys)
        {
            rsa.Dispose();
        }

        _keys.Clear();
    }
}
