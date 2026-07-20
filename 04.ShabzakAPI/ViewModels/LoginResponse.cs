using Translators.Models;

namespace ShabzakAPI.ViewModels
{
    /// <summary>
    /// Response model for successful login. Contains the user profile data
    /// and a signed JWT token for subsequent authenticated API requests.
    /// </summary>
    public class LoginResponse
    {
        public User User { get; set; } = null!;

        /// <summary>
        /// Signed JWT Bearer token. The frontend stores this and sends it
        /// in the Authorization header for all protected API calls.
        /// </summary>
        public string Token { get; set; } = string.Empty;
    }
}
