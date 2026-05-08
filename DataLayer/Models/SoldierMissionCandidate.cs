using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace DataLayer.Models
{
    /// <summary>
    /// Database entity representing a candidate soldier for a mission instance position
    /// during the interactive auto-assign process. Groups candidates by CandidateId (session).
    /// </summary>
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
