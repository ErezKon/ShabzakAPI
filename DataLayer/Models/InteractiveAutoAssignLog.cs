using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataLayer.Models
{
    public class InteractiveAutoAssignLog
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string SessionId { get; set; }

        [Required]
        public int MissionInstanceId { get; set; }

        [Required]
        public string Action { get; set; }

        public string? PicksJson { get; set; }

        [Required]
        public DateTime Timestamp { get; set; }
    }
}
