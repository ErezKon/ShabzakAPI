using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Translators.Models
{
    public class Mission
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string? Description { get; set; }
        public int SoldiersRequired { get; set; }
        public int CommandersRequired { get; set; }
        public int Duration { get; set; }
        public int? SimulateDuration { get; set; }
        public string? FromTime { get; set; }
        public string? ToTime { get; set; }
        public bool IsSpecial { get; set; }
        public bool RequiredInstances { get; set; }

        public List<MissionPosition> Positions { get; set;}
        public List<MissionInstance> MissionInstances { get; set; }
    }
}
