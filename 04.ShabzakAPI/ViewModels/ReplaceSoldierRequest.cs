namespace ShabzakAPI.ViewModels
{
    public class ReplaceSoldierRequest
    {
        public int MissionInstanceId { get; set; }
        public int OldSoldierId { get; set; }
        public int NewSoldierId { get; set; }
        public bool Swap { get; set; }
        public int? SwapMissionInstanceId { get; set; }
    }
}
