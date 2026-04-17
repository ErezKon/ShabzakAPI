using BL.Models;

namespace ShabzakAPI.ViewModels
{
    public class StartInteractiveAutoAssignModel
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<int> Soldiers { get; set; }
        public List<int> Missions { get; set; }
        public AutoAssignScoringOptions? ScoringOptions { get; set; }
        public InteractivePauseOn PauseOn { get; set; } = InteractivePauseOn.FaultyOnly;
        public bool ShowAllSoldiersOnPause { get; set; } = false;
    }
}
