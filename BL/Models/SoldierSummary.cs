using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Models
{
    public class SoldierSummary
    {
        public int TotalMissions { get; set; }
        public double TotalHours { get; set; }
        public List<SoldierMissionBreakdown> MissionBreakdown { get; set; }
    }
}
