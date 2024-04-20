using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Translators.Models
{
    public class Soldier
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string PersonalNumber { get; set; }
        public string Phone { get; set; }
        public string Platoon { get; set; }
        public string Company { get; set; }

        public List<Position> Positions { get; set; }
        public List<Vacation> Vacations { get; set; }
    }
}
