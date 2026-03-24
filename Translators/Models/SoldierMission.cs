using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Translators.Models
{
    public class SoldierMission
    {
        public int Id { get; set; }
        public Soldier Soldier { get; set; }
        public MissionInstance? MissionInstance { get; set; }
        public MissionPosition? MissionPosition { get; set; }
    }
}
