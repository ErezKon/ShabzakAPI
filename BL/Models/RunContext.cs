using DataLayer.Models;

namespace BL.Models
{
    public class RunContext
    {
        public List<int> SelectedSoldierIds { get; set; } = new();
        public List<MissionInstance> AllInstances { get; set; } = new();
        public List<Mission> SelectedMissionAssignments { get; set; } = new();
        public Dictionary<int, List<SoldierMission>> OriginalSoldiersByInstanceId { get; set; } = new();
        public Dictionary<int, List<(DateTime From, DateTime To, int? RequiredRestAfter)>> SoldierIntervalsById { get; set; } = new();
        public Dictionary<int, int> TotalAssignmentsBySoldier { get; set; } = new();
        public Dictionary<int, double> TotalHoursBySoldier { get; set; } = new();
        public Dictionary<int, HashSet<int>> SoldiersAssignedByMissionId { get; set; } = new();
        public Dictionary<(int MissionId, int SoldierId), int> PerMissionAssignmentsBySoldier { get; set; } = new();
        public Dictionary<(int MissionId, int SoldierId), double> PerMissionHoursBySoldier { get; set; } = new();
        public Dictionary<int, int> PerMissionAssignmentCount { get; set; } = new();
        public Dictionary<int, double> PerMissionHoursSum { get; set; } = new();
        public int TotalAssignmentsCount { get; set; }
        public double TotalHoursSum { get; set; }
        public Dictionary<(int SoldierId, int MissionId), double> PositionMultiplierCache { get; set; } = new();
        public AutoAssignScoringOptions ScoringOptions { get; set; } = new();
        public int JitterSeed { get; set; }
        public Dictionary<int, List<CandidateSoldierAssignment>> CandidatesPerInstance { get; set; } = new();
        public HashSet<int> SkippedInstanceIds { get; set; } = new();
    }
}
