namespace ShabzakAPI.ViewModels
{
    public class AssignSoldierToMissionRequest
    {
        public int MissionId { get; set; }
        public int MissionInstanceId { get; set; }
        public List<SoldierMissionPositionRequest> Soldiers { get; set; }

    }
}
