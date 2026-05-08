using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DataLayer.Models
{
    /// <summary>
    /// Database entity representing an application user.
    /// Password is SHA-512 hashed with a per-user salt. Role determines access level.
    /// Optionally linked to a Soldier record via SoldierId.
    /// </summary>
    public class User
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string Password { get; set; }

        [Required]
        public string Salt { get; set; }

        [Required]
        public UserRole Role { get; set; }

        [Required]
        public bool Enabled { get; set; }

        [Required]
        public bool Activated { get; set; }
        public int? SoldierId { get; set; }
        //public virtual ICollection<UserToken> UserTokens { get; set; }
    }
}
