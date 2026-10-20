
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace FireSquad.Lobby.Security;

public class JwtValidator
{
    private readonly TokenValidationParameters _parameters;

    public JwtValidator(
        string base64Key,
        string issuer,
        string audience)
    {
        var keyBytes =
            Convert.FromBase64String(base64Key);

        if (keyBytes.Length < 32)
        {
            throw new ArgumentException(
                "JWT signing key must be at least 32 bytes.");
        }

        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,

            ValidIssuer = issuer,
            ValidAudience = audience,

            IssuerSigningKey =
                new SymmetricSecurityKey(keyBytes),

            ValidAlgorithms = new[]
            {
                SecurityAlgorithms.HmacSha256
            },

            ClockSkew = TimeSpan.Zero
        };
    }

    public (string UserId, string Username)?
        Validate(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler
            {
                MapInboundClaims = false
            };

            ClaimsPrincipal principal =
                handler.ValidateToken(
                    token,
                    _parameters,
                    out _);

            string? userId =
                principal.FindFirst("sub")?.Value;

            string? username =
                principal.FindFirst("unique_name")?.Value;

            if (string.IsNullOrWhiteSpace(userId) ||
                string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine("[JWT ERROR] Valid signature, but sub or unique_name claim is missing.");
                return null;
            }

            return (userId, username);
        }
        catch (SecurityTokenException ex)
        {
            Console.WriteLine($"[JWT ERROR] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"[JWT ERROR] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
