using DataLayer.Models;

namespace ShabzakAPI.ViewModels
{
    public class GetVacationsFilterModel
    {
        public int? SoldierId { get; set; }
        public VacationRequestStatus? Status { get; set; }
    }
}
