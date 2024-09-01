using BL.Models;
using Translators.Models;

namespace ShabzakAPI.ViewModels
{
    public class BreakdownVM
    {
        public Mission Mission { get; set; }
        public int Count { get; set; }
    }

    public class AssignmentsBreakdownVM: DataPerSoldierBase
    {
        public List<BreakdownVM> Breakdown { get; set; }
    }
}
