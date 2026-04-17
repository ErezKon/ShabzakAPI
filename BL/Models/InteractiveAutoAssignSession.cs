using DataLayer.Models;

namespace BL.Models
{
    public class InteractiveAutoAssignSession
    {
        public string SessionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastTouchedAt { get; set; }
        public SemaphoreSlim Lock { get; } = new SemaphoreSlim(1, 1);
        public RunContext Context { get; set; }
        public List<int> OrderedInstanceIds { get; set; } = new();
        public int CurrentIndex { get; set; }
        public Mission StartingMission { get; set; }
        public List<Soldier> SelectedSoldiers { get; set; } = new();
        public DateTime AbsoluteFrom { get; set; }
        public DateTime AbsoluteTo { get; set; }
        public InteractivePauseOn PauseOn { get; set; } = InteractivePauseOn.FaultyOnly;
        public bool ShowAllSoldiersOnPause { get; set; }
        public DateTime CacheSnapshotTime { get; set; }
    }
}
