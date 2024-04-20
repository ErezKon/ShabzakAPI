using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
    public class SoldierMission
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required] 
        public int SoldierId { get; set; }

        [Required]
        public int MissionId { get; set; }

        [Required]
        public int MissionPositionId { get; set; }

        [Required]
        public DateTime Time { get; set; }

        public virtual Soldier Soldier { get; }

        public virtual Mission Mission { get; set; }

        public virtual MissionPositions MissionPosition { get; set; }
    }
}
