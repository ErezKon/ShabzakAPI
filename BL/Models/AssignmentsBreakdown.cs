using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Models
{
    public class AssignmentsBreakdown: DataPerSoldierBase
    {
        /// <summary>
        /// Dictionary containing Mission Id as key, and the number of assignments to that mission as value.
        /// </summary>
        public Dictionary<int,int> AssignmentsPerMissionId { get; set; }
    }
}
