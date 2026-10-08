using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace ReportsApi;

public static class KeycloakClaims
{
    public const string Username = "preferred_username";

    /// <summary>
    /// Checks that the token was issued to an allowed client (azp) and copies
    /// realm roles from realm_access.roles into standard role claims.
    /// </summary>
    public static Task OnTokenValidated(TokenValidatedContext context, KeycloakOptions keycloak)
    {
        var principal = context.Principal!;
        var azp = principal.FindFirstValue("azp");
        if (azp is null || !keycloak.AllowedClients.Contains(azp))
        {
            context.Fail($"Клиент '{azp}' не допущен к API отчётов");
            return Task.CompletedTask;
        }

        var realmAccess = principal.FindFirstValue("realm_access");
        if (realmAccess is not null)
        {
            using var doc = JsonDocument.Parse(realmAccess);
            if (doc.RootElement.TryGetProperty("roles", out var roles))
            {
                var identity = (ClaimsIdentity)principal.Identity!;
                foreach (var role in roles.EnumerateArray())
                {
                    identity.AddClaim(new Claim(identity.RoleClaimType, role.GetString()!));
                }
            }
        }
        return Task.CompletedTask;
    }
}
