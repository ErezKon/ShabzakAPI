using Translators.Models;

namespace ShabzakAPI.ViewModels
{
    /// <summary>
    /// Body of POST /api/User/UpdateUser. Only the provided fields are changed.
    /// Setting <see cref="Role"/> requires SuperAdmin.
    /// </summary>
    public class UpdateUserRequest
    {
        public int UserId { get; set; }
        public UserRole? Role { get; set; }
        public bool? Enabled { get; set; }
        public bool? Activated { get; set; }
    }
}
