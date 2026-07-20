using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Translators.Models;

namespace ShabzakAPI.Auth
{
    /// <summary>
    /// Generates signed JWT Bearer tokens for authenticated users.
    /// Configured once at startup from appsettings.json Jwt section.
    /// </summary>
    public static class JwtHelper
    {
        private static string _key = null!;
        private static string _issuer = null!;
        private static string _audience = null!;
        private static int _expiryMinutes;

        /// <summary>
        /// Initializes JWT signing parameters from configuration. Call once in Program.cs.
        /// </summary>
        /// <param name="key">HMAC-SHA256 signing key (≥ 256-bit / 32+ chars).</param>
        /// <param name="issuer">Token issuer claim.</param>
        /// <param name="audience">Token audience claim.</param>
        /// <param name="expiryMinutes">Token lifetime in minutes.</param>
        public static void Configure(string key, string issuer, string audience, int expiryMinutes)
        {
            _key = key;
            _issuer = issuer;
            _audience = audience;
            _expiryMinutes = expiryMinutes;
        }

        /// <summary>
        /// Generates a signed JWT containing the user's identity claims:
        /// userId, name (hashed), role, enabled, activated, and optional soldierId.
        /// </summary>
        /// <param name="user">Authenticated user (password/salt should already be cleared).</param>
        /// <returns>Compact serialized JWT string.</returns>
        public static string GenerateToken(User user)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.Name, user.Name),
                new(ClaimTypes.Role, user.Role.ToString()),
                new("enabled", user.Enabled.ToString()),
                new("activated", user.Activated.ToString()),
            };

            if (user.SoldierId.HasValue)
            {
                claims.Add(new Claim("soldierId", user.SoldierId.Value.ToString()));
            }

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_expiryMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
