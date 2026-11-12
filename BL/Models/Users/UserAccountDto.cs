using Translators.Models;

namespace BL.Models.Users
{
    /// <summary>
    /// A login account as exposed to admins (GET /api/User/GetUsers, UpdateUser).
    /// Whitelisted fields only: never the hashed username, password or salt.
    /// </summary>
    public class UserAccountDto
    {
        public int Id { get; set; }
        public int? SoldierId { get; set; }
        public string? SoldierName { get; set; }
        public string? Platoon { get; set; }
        public string? Company { get; set; }
        public UserRole Role { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public bool Activated { get; set; }
    }
}
