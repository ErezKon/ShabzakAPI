using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
    /// <summary>
    /// Database entity representing a soldier. All PII fields (Name, PersonalNumber, Phone,
    /// Position, Platoon, Company) are AES-encrypted at rest.
    /// Position is stored as a comma-separated string of numeric enum values.
    /// </summary>
    public class Soldier
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string PersonalNumber { get; set; }

        [Required]
        public string Phone { get; set; }

        [Required]
        public string Platoon { get; set; }

        [Required]
        public string Company { get; set; }

        [Required]
        public string Position { get; set; }

        public bool Active { get; set; }

        public virtual ICollection<SoldierMission> Missions { get; set; } = [];

        public virtual ICollection<Vacation> Vacations { get; set; } = [];
    } 
}
