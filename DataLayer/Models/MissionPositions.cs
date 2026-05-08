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
    /// Database entity representing how many soldiers of a specific position are required for a mission.
    /// Links a Mission to a Position enum value with a required count.
    /// </summary>
    public class MissionPositions
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int MissionId { get; set; }

        [Required]
        public Position Position { get; set; }

        [Required]
        public int Count { get; set; }

        public virtual Mission Mission { get; set; } = new Mission();
        public virtual ICollection<SoldierMission> Soldiers { get; set; } = [];
    }
}
