using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    /// <summary>
    /// Translates Vacation entities between DataLayer DB models and BL models.
    /// </summary>
    public abstract class VacationTranslator
    {
        /// <summary>
        /// Converts a DB Vacation entity to a BL Vacation model.
        /// </summary>
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


        /// <summary>
        /// Converts a BL Vacation model to a DB Vacation entity.
        /// </summary>
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
