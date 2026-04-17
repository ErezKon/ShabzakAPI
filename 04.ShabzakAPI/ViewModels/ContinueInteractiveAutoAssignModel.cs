using BL.Models;

namespace ShabzakAPI.ViewModels
{
    public class ContinueInteractiveAutoAssignModel
    {
        public string SessionId { get; set; }
        public List<CandidatePickDto> Picks { get; set; } = new();
        public bool SkipInstance { get; set; }
    }
}
