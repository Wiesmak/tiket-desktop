using Duende.IdentityModel.OidcClient;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Tiket.Desktop.Application;

internal sealed record OperatingGrants(string Subject, string Cashier, string[] Events, string[] Players, int[] Poses)
{
    internal DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.MaxValue;
    public override string ToString() => "Verified operating grants (redacted)";
    internal static async Task<OperatingGrants?> ValidateAsync(string accessToken, string subject,
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
                !jwt.TryGetPayloadValue<string[]>("tiket_roles", out var roles) || !roles.Contains("CASHIER") ||
                !jwt.TryGetPayloadValue<string>("tiket_cashier", out var cashier) || string.IsNullOrWhiteSpace(cashier) || cashier.Length > 128 || cashier.Any(char.IsControl) ||
                !jwt.TryGetPayloadValue<string[]>("tiket_events", out var events) ||
                !jwt.TryGetPayloadValue<string[]>("tiket_players", out var players) ||
                !jwt.TryGetPayloadValue<string[]>("tiket_pos", out var pos)) return null;
            var poses = pos.Select(x => int.TryParse(x, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
                n > 0 && n.ToString(System.Globalization.CultureInfo.InvariantCulture) == x ? n : 0).Where(x => x > 0).Distinct().ToArray();
            return poses.Length == 0 ? null : new(subject, cashier, events, players, poses)
            { ExpiresAt = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero) };
        }
        catch { return null; }
    }
}
