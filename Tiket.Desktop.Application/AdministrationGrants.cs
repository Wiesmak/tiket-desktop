using Duende.IdentityModel.OidcClient;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Tiket.Desktop.Application;

internal sealed record AdministrationGrants(string Subject, string[] Roles, string[] Events, string[] Players)
{
    internal DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.MaxValue;
    public override string ToString() => "Verified administration grants";
    internal static async Task<AdministrationGrants?> ValidateAsync(string accessToken, string subject,
        ConnectionProfile profile, OidcClientOptions options)
    {
        try
        {
            var validated = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(accessToken,
                new TokenValidationParameters
                {
                    ValidIssuer = profile.Issuer.OriginalString, ValidAudience = profile.ApiAudience,
                    IssuerSigningKeys = new JsonWebKeySet(options.ProviderInformation.KeySet.RawData!).GetSigningKeys(),
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256], RequireSignedTokens = true,
                    RequireExpirationTime = true, ValidateIssuerSigningKey = true, ValidateIssuer = true,
                    ValidateAudience = true, ValidateLifetime = true, ClockSkew = TimeSpan.Zero
                });
            if (!validated.IsValid || validated.SecurityToken is not JsonWebToken jwt || jwt.Subject != subject ||
                !jwt.TryGetPayloadValue<string[]>("tiket_roles", out var roles) ||
                !jwt.TryGetPayloadValue<string[]>("tiket_events", out var events)) return null;
            jwt.TryGetPayloadValue<string[]>("tiket_players", out var players);
            return new(subject, roles, events, players ?? [])
            { ExpiresAt = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero) };
        }
        catch { return null; }
    }
}
