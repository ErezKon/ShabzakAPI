namespace ShabzakAPI.ViewModels
{
    public class AutoAssignModel
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<int> Soldiers { get; set; }
        public List<int> Missions { get; set; }

    }
}
