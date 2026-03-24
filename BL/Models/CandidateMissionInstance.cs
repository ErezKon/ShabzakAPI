using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace BL.Models
{
    public class CandidateMissionInstance: MissionInstance
    {
        public List<CandidateSoldierAssignmentVM> Candidates { get; set; }
        public Dictionary<Position, int>? MissingPositions { get; set; }
    }
}
