namespace ShabzakAPI.ViewModels
{
    public class GetAvailableSoldiersRequest
    {
        public int MissionInstanceId { get; set; }
        public List<int>? SoldiersPool { get; set; }
    }
}
