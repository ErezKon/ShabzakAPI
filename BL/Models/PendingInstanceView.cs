namespace BL.Models
{
    public class PendingInstanceView
    {
        public CandidateMissionInstance Instance { get; set; }
        public int MaxSelections { get; set; }
        public int CommandersRequired { get; set; }
        public int SoldiersRequired { get; set; }
    }
}
