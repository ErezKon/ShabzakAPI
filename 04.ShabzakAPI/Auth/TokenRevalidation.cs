using BL.Cache;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;

namespace ShabzakAPI.Auth
{
    /// <summary>
    /// JwtBearer <c>OnTokenValidated</c> hook: re-checks every request against the current account state
    /// (in-memory <see cref="UsersCache"/>, DB fallback) so account changes take effect immediately instead of at token expiry:
    /// deleted or disabled accounts are rejected (401) and the role claim is replaced with the account's current role.
    /// </summary>
    public static class TokenRevalidation
    {
        public static Task OnTokenValidated(TokenValidatedContext context)
        {
            var principal = context.Principal;
            var userId = principal?.GetUserId();
            var usersCache = context.HttpContext.RequestServices.GetRequiredService<UsersCache>();
            var user = userId.HasValue ? usersCache.GetUserById(userId.Value) : null;
            if (user == null || !user.Enabled)
            {
                context.Fail("The account no longer exists or is disabled.");
                return Task.CompletedTask;
            }

            if (principal!.Identity is ClaimsIdentity identity)
            {
                foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList())
                {
                    identity.TryRemoveClaim(claim);
                }
                identity.AddClaim(new Claim(identity.RoleClaimType, user.Role.ToString()));
            }
            return Task.CompletedTask;
        }
    }
}
