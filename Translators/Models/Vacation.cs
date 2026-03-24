using DataLayer.Models;

namespace Translators.Models
{
    public class Vacation
    {
        public int Id { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public VacationRequestStatus Approved { get; set; } = VacationRequestStatus.Pending;
    }
}
