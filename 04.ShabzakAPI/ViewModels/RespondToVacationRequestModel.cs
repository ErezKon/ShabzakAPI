using DataLayer.Models;

namespace ShabzakAPI.ViewModels
{
    public class RespondToVacationRequestModel
    {
        public int VacationId { get; set; }
        public VacationRequestStatus Response { get; set; }
    }
}
