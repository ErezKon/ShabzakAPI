using Translators.Models;

namespace BL.Models
{
    public class ReplacementCandidateModel
    {
        public Soldier Soldier { get; set; }
        public double Score { get; set; }
        public bool HasOverlap { get; set; }
        public int? OverlappingMissionInstanceId { get; set; }
        public string? OverlappingMissionName { get; set; }
        public int? RestTimeBefore { get; set; }
        public int? RestTimeAfter { get; set; }
        public bool IsAssignedToThisInstance { get; set; }
    }
}
