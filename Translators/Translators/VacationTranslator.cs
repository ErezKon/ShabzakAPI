using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    public abstract class VacationTranslator
    {
        public static Vacation ToBL(DataLayer.Models.Vacation vac)
        {
            return new Vacation()
            {
                Id = vac.Id,
                From = vac.From,
                To = vac.To,
                Approved = vac.Approved,
            };
        }


        public static DataLayer.Models.Vacation ToDB(Vacation vac)
        {
            return new DataLayer.Models.Vacation()
            {
                Id = vac.Id,
                From = vac.From,
                To = vac.To,
                Approved = vac.Approved
            };
        }
    }
}
