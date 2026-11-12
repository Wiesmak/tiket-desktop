using System.Security.Claims;
using Duende.IdentityModel.OidcClient;
using Duende.IdentityModel.OidcClient.Results;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Tiket.Desktop.Application;

/// <summary>Duende 7 requires an explicit signature validator. Delegate JWT validation to Microsoft IdentityModel.</summary>
internal sealed class SignedIdentityValidator(string nonce) : IIdentityTokenValidator
{
    public async Task<IdentityTokenValidationResult> ValidateAsync(string identityToken, OidcClientOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keys = new JsonWebKeySet(options.ProviderInformation.KeySet.RawData!).GetSigningKeys();
        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        var result = await handler.ValidateTokenAsync(identityToken, new TokenValidationParameters
        {
            ValidIssuer = options.ProviderInformation.IssuerName,
            ValidAudience = options.ClientId,
            IssuerSigningKeys = keys,
            ValidAlgorithms = options.Policy.ValidSignatureAlgorithms,
            RequireSignedTokens = true, RequireExpirationTime = true,
            ValidateIssuerSigningKey = true, ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, ClockSkew = options.ClockSkew
        });
        if (!result.IsValid) return new IdentityTokenValidationResult { Error = "invalid_identity_token" };
        var user = new ClaimsPrincipal(result.ClaimsIdentity);
        if (user.FindFirst("nonce")?.Value != nonce || string.IsNullOrWhiteSpace(user.FindFirst("sub")?.Value) ||
            (user.FindAll("aud").Count() > 1 && user.FindFirst("azp")?.Value != options.ClientId) ||
            (user.FindFirst("azp") is { } azp && azp.Value != options.ClientId))
            return new IdentityTokenValidationResult { Error = "invalid_identity_binding" };
        return new IdentityTokenValidationResult { User = user, SignatureAlgorithm = ((JsonWebToken)result.SecurityToken).Alg };
    }
}
