using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Translators.Models;

namespace ShabzakAPI.Auth
{
    /// <summary>
    /// Helpers for reading the Shabzak JWT claims from the authenticated principal.
    /// </summary>
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// The caller's user ID. The JWT <c>sub</c> claim is mapped to <see cref="ClaimTypes.NameIdentifier"/>
        /// by the default inbound claim mapping; the raw <c>sub</c> is used as a fallback.
        /// </summary>
        public static int? GetUserId(this ClaimsPrincipal user)
        {
            var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return int.TryParse(value, out var id) ? id : null;
        }

        /// <summary>The soldier linked to the caller's account, if any (<c>soldierId</c> claim).</summary>
        public static int? GetSoldierId(this ClaimsPrincipal user) =>
            int.TryParse(user.FindFirst("soldierId")?.Value, out var id) ? id : null;

        /// <summary>The caller's SHA-512 hashed username (<see cref="ClaimTypes.Name"/>). Never expose it in responses.</summary>
        public static string? GetHashedUsername(this ClaimsPrincipal user) => user.FindFirst(ClaimTypes.Name)?.Value;

        /// <summary>True when the caller has the Admin or SuperAdmin role.</summary>
        public static bool IsAdminOrAbove(this ClaimsPrincipal user) =>
            user.IsInRole(nameof(UserRole.Admin)) || user.IsInRole(nameof(UserRole.SuperAdmin));

        /// <summary>True when the caller has the SuperAdmin role.</summary>
        public static bool IsSuperAdmin(this ClaimsPrincipal user) => user.IsInRole(nameof(UserRole.SuperAdmin));

        /// <summary>The caller's highest role (Regular when no known role claim is present).</summary>
        public static UserRole GetRole(this ClaimsPrincipal user) =>
            user.IsSuperAdmin() ? UserRole.SuperAdmin : user.IsInRole(nameof(UserRole.Admin)) ? UserRole.Admin : UserRole.Regular;
    }
}
