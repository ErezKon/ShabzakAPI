using DataLayer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Models
{
    public class CandidateSoldierAssignment
    {
        public Soldier Soldier { get; set; }
        public MissionInstance MissionInstance { get; set; }
        public double Rank { get; set; }
        public int? MissionsAssignedTo { get; set; }
        public Dictionary<string,double> RankBreakdown { get; set; }
    }
}
