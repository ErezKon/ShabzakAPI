using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace BL.Models
{
    public class DataPerSoldierBase
    {
        public Soldier Soldier { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
    }
}
