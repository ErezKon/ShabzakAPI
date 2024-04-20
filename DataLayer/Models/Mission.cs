using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
    public class Mission
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string Description { get; set; }

        [Required]
        public int SoldiersRequired { get; set; }

        [Required]
        public int CommandersRequired { get; set; }

        [Required]
        public int Duration { get; set; }

        public string? FromTime { get; set; }

        public string? ToTime { get; set;}

        public bool IsSpecial { get; set; }

        public virtual ICollection<SoldierMission> SoldierMissions{ get; set; }
        public virtual ICollection<MissionPositions> MissionPositions { get; set; }

    }
}
