using DataLayer.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Translators.Models
{
    public class MissionPosition
    {
        public int Id { get; set; }
        public int MissionId { get; set; }
        public Position Position { get; set; }
        public int Count { get; set; }
    }
}
