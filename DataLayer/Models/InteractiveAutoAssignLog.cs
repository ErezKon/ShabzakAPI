using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataLayer.Models
{
    /// <summary>
    /// Database entity logging actions during an interactive auto-assign session.
    /// Records session ID, target mission instance, action type, and optional picks JSON.
    /// </summary>
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
