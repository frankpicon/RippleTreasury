using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace EventTicketing.ServiceDefaults.Authentication;

internal sealed class KeycloakRolesClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity ||
            identity.FindFirst("realm_access") is not { Value: var realmAccess })
        {
            return Task.FromResult(principal);
        }

        using var document = JsonDocument.Parse(realmAccess);
        if (!document.RootElement.TryGetProperty("roles", out var roles))
        {
            return Task.FromResult(principal);
        }

        var existingRoles = identity.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var role in roles.EnumerateArray())
        {
            var roleName = role.GetString();
            if (!string.IsNullOrWhiteSpace(roleName) && existingRoles.Add(roleName))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
            }
        }

        return Task.FromResult(principal);
    }
}
