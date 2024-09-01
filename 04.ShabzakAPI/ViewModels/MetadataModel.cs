using BL.Models;

namespace ShabzakAPI.ViewModels
{
    public class MetadataModel
    {
        public DateTime? From { get; set; }
        public DateTime To { get; set; }
        public SoldierMetadataType SoldierType { get; set; }
    }
}
