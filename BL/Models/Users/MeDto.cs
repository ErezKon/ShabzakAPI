using Translators.Models;

namespace BL.Models.Users
{
    /// <summary>
    /// The authenticated caller's identity (GET /api/User/Me). Never contains the hashed username, password or salt.
    /// </summary>
    public class MeDto
    {
        public int Id { get; set; }
        public UserRole Role { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public bool Activated { get; set; }
        public int? SoldierId { get; set; }
        public string? SoldierName { get; set; }
    }
}
