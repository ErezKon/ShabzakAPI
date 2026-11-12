using Microsoft.AspNetCore.Authorization;
using Translators.Models;

namespace ShabzakAPI.Auth
{
    /// <summary>
    /// Role-based authorization policy names and their registration.
    /// Use with <c>[Authorize(Policy = Policies.AdminOrAbove)]</c>.
    /// </summary>
    public static class Policies
    {
        /// <summary>Requires the Admin or SuperAdmin role.</summary>
        public const string AdminOrAbove = "AdminOrAbove";

        /// <summary>Requires the SuperAdmin role.</summary>
        public const string SuperAdminOnly = "SuperAdminOnly";

        /// <summary>
        /// Registers all Shabzak policies. Call via <c>builder.Services.AddAuthorization(Policies.Configure)</c>.
        /// </summary>
        public static void Configure(AuthorizationOptions options)
        {
            options.AddPolicy(AdminOrAbove, policy => policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.SuperAdmin)));
            options.AddPolicy(SuperAdminOnly, policy => policy.RequireRole(nameof(UserRole.SuperAdmin)));
        }
    }
}
