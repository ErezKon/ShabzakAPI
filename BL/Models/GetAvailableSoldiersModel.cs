using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace BL.Models
{
    public class GetAvailableSoldiersModel
    {
        public Soldier Soldier { get; set; }
        public int? RestTimeBefore { get; set; }
        public int? RestTimeAfter { get; set; }
        public bool IsAssignedForQueriedInstance { get; set; }
    }
}
