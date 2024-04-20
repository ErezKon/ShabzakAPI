using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
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
        public Position Position { get; set; } = new Position();

        public virtual ICollection<SoldierMission> Missions { get; set; } = new HashSet<SoldierMission>();

        public virtual ICollection<Vacation> Vacations { get; set; } = new HashSet<Vacation>();
    } 
}
