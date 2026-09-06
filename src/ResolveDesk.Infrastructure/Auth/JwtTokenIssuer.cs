using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ResolveDesk.Application;

namespace ResolveDesk.Infrastructure.Auth;

/// <summary>
/// Issues the API's own access tokens, whichever provider validated the credentials — so LDAP and local
/// logins produce the same token shape and downstream authorization has one thing to understand.
/// </summary>
public sealed class JwtTokenIssuer : ITokenIssuer
{
    /// <summary>HS256 needs at least 256 bits of key; anything shorter is rejected outright.</summary>
    private const int MinimumKeyBytes = 32;

    private readonly AuthOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenIssuer(AuthOptions options, bool isDevelopment)
    {
        _options = options;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(ResolveKey(options.Jwt.SigningKey, isDevelopment)),
            SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTime ExpiresAtUtc) Issue(AuthenticatedUser user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.Jwt.LifetimeMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Jwt.Issuer,
            Audience = _options.Jwt.Audience,
            Expires = expires,
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [JwtRegisteredClaimNames.Name] = user.FullName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("n"),
                [ClaimTypes.Role] = user.Role.ToString(),
            },
        };
        return (_handler.CreateToken(descriptor), expires);
    }

    /// <summary>
    /// The signing key comes from configuration or it does not exist. Development falls back to a random
    /// per-process key — tokens then stop working across a restart, which is the intended nudge — while
    /// a deployment with no key configured fails at startup rather than running on a guessable secret.
    /// </summary>
    internal static byte[] ResolveKey(string? configured, bool isDevelopment)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var bytes = Encoding.UTF8.GetBytes(configured);
            if (bytes.Length < MinimumKeyBytes)
                throw new InvalidOperationException(
                    $"Auth:Jwt:SigningKey must be at least {MinimumKeyBytes} bytes ({MinimumKeyBytes} ASCII characters). " +
                    "Generate one with: openssl rand -base64 48");
            return bytes;
        }

        if (!isDevelopment)
            throw new InvalidOperationException(
                "Auth:Jwt:SigningKey is required when authentication is enabled outside Development. " +
                "Supply it through the environment (Auth__Jwt__SigningKey) or a secret store — never in appsettings.");

        return RandomNumberGenerator.GetBytes(48);
    }

    /// <summary>The key the validator must trust; shared so issuing and validating cannot drift apart.</summary>
    public SecurityKey SigningKey => _credentials.Key;
}
