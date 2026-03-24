namespace ShabzakAPI.ViewModels
{
    public class GetMissionInstancesRangesVM
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public bool FullDay { get; set; } = true;
        public bool UnassignedOnly { get; set; } = true;
    }
}
