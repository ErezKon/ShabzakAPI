using Translators.Models;

namespace ShabzakAPI.ViewModels
{
    public class AddEditSoldierMission
    {
        public int Id { get; set; }
        public int SoldierId { get; set; }
        public Position Position { get; set; }
    }
}
