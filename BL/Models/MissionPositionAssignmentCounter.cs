using DataLayer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Models
{
    public class MissionPositionAssignmentCounter
    {
        public int MissionPositionId { get; set; }
        public Position Position { get; set; }
        public int Count { get; set; }
    }
}
