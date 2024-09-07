using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace BL.Models
{
    public class AssignmentValidationModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int ValidInstancesCount { get; set; }
        public int TotalInstancesCount { get; set; }
        public List<MissionInstance> FaultyInstances { get; set; }
        public List<MissionInstance> ValidInstances { get; set; }

    }
}
