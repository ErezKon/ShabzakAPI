using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
    public class Mission
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        public string Description { get; set; }

        [Required]
        public int SoldiersRequired { get; set; }

        [Required]
        public int CommandersRequired { get; set; }

        [Required]
        public int Duration { get; set; }
        public int? SimulateDuration { get; set; }

        public string? FromTime { get; set; }

        public string? ToTime { get; set;}

        public bool IsSpecial { get; set; }

        public bool RequiredInstances { get; set; }

        public virtual ICollection<MissionInstance> MissionInstances { get; set; }
        public virtual ICollection<MissionPositions> MissionPositions { get; set; }

        public Mission Clone()
        {
            var ret = new Mission();
            ret.Id = Id;
            ret.Name = Name;
            ret.Description = Description;
            ret.SoldiersRequired = SoldiersRequired;
            ret.CommandersRequired = CommandersRequired;
            ret.Duration = Duration;
            ret.FromTime = FromTime;
            ret.ToTime = ToTime;
            ret.IsSpecial = IsSpecial;
            ret.MissionInstances = MissionInstances?.ToList() ?? [];
            ret.MissionPositions = MissionPositions?.ToList() ?? [];
            return ret;
        }

        public Mission HideLists()
        {
            var ret = this.Clone();
            ret.MissionInstances = [];
            ret.MissionPositions = [];
            return ret;
        }

    }
}
