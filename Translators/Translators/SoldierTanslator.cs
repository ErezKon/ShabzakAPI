using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    public abstract class SoldierTanslator
    {
        public static Soldier ToBL(DataLayer.Models.Soldier sol)
        {
            return new Soldier()
            {
                Id = sol.Id,
                Name = sol.Name,
                PersonalNumber = sol.PersonalNumber,
                Phone = sol.Phone,
                Platoon = sol.Platoon,
                Company = sol.Company,
                Vacations = sol.Vacations?
                .Select(v => VacationTranslator.ToBL(v))
                .ToList()
                ?? []
            };
        }


        public static DataLayer.Models.Soldier ToDB(Soldier sol)
        {
            return new DataLayer.Models.Soldier()
            {
                Id = sol.Id,
                Name = sol.Name,
                PersonalNumber = sol.PersonalNumber,
                Phone = sol.Phone,
                Platoon = sol.Platoon,
                Company = sol.Company,
                Vacations = sol.Vacations?
                .Select(v => VacationTranslator.ToDB(v))
                .ToList()
                ?? []
            };
        }
    }
}
