using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace BL.Models
{
    public class CandidateSoldierAssignmentVM
    {
        public Soldier Soldier { get; set; }
        public double Rank { get; set; }
        public int? MissionsAssignedTo { get; set; }
        public Dictionary<string, double> RankBreakdown { get; set; }
    }
}
