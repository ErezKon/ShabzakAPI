using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
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
