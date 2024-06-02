using Translators.Models;

namespace ShabzakAPI.ViewModels
{
    public class AddEditMission
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int SoldiersRequired { get; set; }
        public int CommandersRequired { get; set; }
        public int Duration { get; set; }
        public string? FromTime { get; set; }
        public string? ToTime { get; set; }
        public bool IsSpecial { get; set; }
        public List<MissionPosition> Positions { get; set; }
        public List<AddEditMissionInstance> MissionInstances { get; set; }
    }
}
