using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
    public class SoldierMissionCandidate
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int SoldierId { get; set; }

        [Required]
        public int MissionInstanceId { get; set; }

        [Required]
        public int MissionPositionId { get; set; }
        [Required]
        public string CandidateId { get; set; }
    }
}
