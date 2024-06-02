namespace ShabzakAPI.ViewModels
{
    public class AddEditMissionInstance
    {
        public int Id { get; set; }
        public string FromTime { get; set; }
        public string ToTime { get; set; }
        public List<AddEditSoldierMission> SoldierMissions { get; set; }
    }
}
