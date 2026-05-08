using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
    /// <summary>
    /// Database entity representing a soldier's vacation/leave request.
    /// Tracks date range and approval status. Linked to a Soldier via SoldierId.
    /// </summary>
    public class Vacation
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public int SoldierId { get; set; }

        [Required]
        public DateTime From { get; set; }

        [Required]
        public DateTime To { get; set; }

        [Required]
        public VacationRequestStatus Approved { get; set; } = VacationRequestStatus.Pending;

        public virtual Soldier Soldier { get; set; } = new Soldier();
    }
}
