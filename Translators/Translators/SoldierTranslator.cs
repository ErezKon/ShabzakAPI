using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    /// <summary>
    /// Translates Soldier entities between DataLayer DB models and BL models.
    /// Parses comma-separated position strings into Position enum lists.
    /// </summary>
    public abstract class SoldierTranslator
    {
        /// <summary>
        /// Converts a DB Soldier entity to a BL Soldier model.
        /// Parses position string, optionally includes missions and vacations.
        /// </summary>
        /// <param name="sol">The DB soldier entity.</param>
        /// <param name="includeMissions">Include assigned missions.</param>
        /// <param name="includeVacations">Include vacation records.</param>
        /// <param name="includeMissionInstance">Include mission instance data in missions.</param>
        /// <returns>The translated BL soldier model, or null if input is null.</returns>
        public static Soldier ToBL(DataLayer.Models.Soldier sol, bool includeMissions = true, bool includeVacations = true, bool includeMissionInstance = true)
        {
            if(sol == null)
            {
                return null;
            }
            return new Soldier()
            {
                Id = sol.Id,
                Name = sol.Name,
                PersonalNumber = sol.PersonalNumber,
                Phone = sol.Phone,
                Platoon = sol.Platoon,
                Company = sol.Company,
                Active = sol.Active,
                Positions = sol.Position.Split(",", StringSplitOptions.RemoveEmptyEntries)
                    .Select(s =>
                    {
                        if(int.TryParse(s, out int numericValue))
                        {
                            return (Position)numericValue;
                        }
                        return Position.Simple;
                    })
                    .ToList(),
                Vacations = includeVacations ? 
                            sol.Vacations
                                ?.Select(v => VacationTranslator.ToBL(v))
                                ?.ToList()
                                ?? [] :
                            [],
                Missions = includeMissions ?
                           sol.Missions
                               ?.Select(m => SoldierMissionTranslator.ToBL(m,includeSoldier: false, includeMissionInstance: includeMissionInstance))
                               ?.ToList() ?? [] :
                           []
            };
        }


        /// <summary>
        /// Converts a BL Soldier model to a DB Soldier entity.
        /// Joins Position enum list into a comma-separated string.
        /// </summary>
        /// <param name="sol">The BL soldier model.</param>
        /// <returns>The translated DB soldier entity.</returns>
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
                Active = sol.Active,
                Position = string.Join(",", sol.Positions
                .Order()
                .Select(p => ((int)p).ToString())
                .ToArray()),
                Vacations = sol.Vacations
                ?.Select(v => VacationTranslator.ToDB(v))
                ?.ToList()
                ?? []
            };
        }

    }
}
