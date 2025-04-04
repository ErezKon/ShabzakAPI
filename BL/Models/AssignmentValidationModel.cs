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
        public string Id { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int ValidInstancesCount { get; set; }
        public int TotalInstancesCount { get; set; }
        public Dictionary<string, List<CandidateMissionInstance>> FaultyInstances { get; set; }
        public Dictionary<string, List<CandidateMissionInstance>> ValidInstances { get; set; }

    }
}
