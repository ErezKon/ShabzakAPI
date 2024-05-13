using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Translators.Models
{
    public class MissionInstance
    {
        public int Id { get; set; }
        public string FromTime { get; set; }
        public string ToTime { get; set; }

        public List<SoldierMission> SoldierMissions { get; set; }
    }
}
