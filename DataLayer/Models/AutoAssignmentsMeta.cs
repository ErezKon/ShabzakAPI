using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
    /// <summary>
    /// Database entity storing metadata for an auto-assignment run.
    /// Records the GUID, date range, and JSON-serialized soldier/mission IDs used.
    /// </summary>
    public class AutoAssignmentsMeta
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string Guid { get; set; }

        [Required]
        public DateTime From { get; set; }

        [Required]
        public DateTime To { get; set; }

        [Required]
        public string Soldiers { get; set; }

        [Required]
        public string Missions { get; set; }

    }
}
