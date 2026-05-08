using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    /// <summary>
    /// Translates Mission entities between DataLayer DB models and BL models.
    /// Maps all mission properties including nested positions and instances.
    /// </summary>
    public abstract class MissionTranslator
    {
        /// <summary>
        /// Converts a DB Mission entity to a BL Mission model.
        /// Optionally includes nested soldier and mission instance data.
        /// </summary>
        /// <param name="mis">The DB mission entity.</param>
        /// <param name="includeSoldier">Include soldier data in nested instances.</param>
        /// <param name="includeMission">Include mission reference in nested instances.</param>
        /// <returns>The translated BL mission model.</returns>
        public static Mission ToBL(DataLayer.Models.Mission mis, bool includeSoldier = false, bool includeMission = false)
        {
            try
            {

                return new Mission()
                {
                    Id = mis.Id,
                    Name = mis.Name,
                    Description = mis.Description,
                    CommandersRequired = mis.CommandersRequired,
                    SoldiersRequired = mis.SoldiersRequired,
                    Duration = mis.Duration,
                    SimulateDuration = mis.SimulateDuration,
                    FromTime = mis.FromTime,
                    ToTime = mis.ToTime,
                    IsSpecial = mis.IsSpecial,
                    RequiredInstances = mis.RequiredInstances,
                    ActualHours = mis.ActualHours,
                    RequiredRestAfter = mis.RequiredRestAfter,
                    Positions = mis.MissionPositions
                        ?.Select(mp => MissionPositionTranslator.ToBL(mp))
                        ?.ToList()
                        ?? [],
                    MissionInstances = mis.MissionInstances
                    ?.Select(m => MissionInstanceTranslator.ToBL(m, includeMission, includeSoldier))
                    ?.ToList() ?? []
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }


        /// <summary>
        /// Converts a BL Mission model to a DB Mission entity.
        /// Maps positions and instances back to DB format.
        /// </summary>
        /// <param name="mis">The BL mission model.</param>
        /// <returns>The translated DB mission entity.</returns>
        public static DataLayer.Models.Mission ToDB(Mission mis)
        {
            return new DataLayer.Models.Mission()
            {
                Id = mis.Id,
                Name = mis.Name,
                Description = mis.Description,
                CommandersRequired = mis.CommandersRequired,
                SoldiersRequired = mis.SoldiersRequired,
                Duration = mis.Duration,
                SimulateDuration = mis.SimulateDuration,
                FromTime = mis.FromTime,
                ToTime = mis.ToTime,
                IsSpecial = mis.IsSpecial,
                RequiredInstances = mis.RequiredInstances,
                ActualHours = mis.ActualHours,
                RequiredRestAfter = mis.RequiredRestAfter,
                MissionPositions = mis.Positions
                    ?.Select(mp => MissionPositionTranslator.ToDB(mp))
                    ?.ToList()
                    ?? [],
                MissionInstances = mis.MissionInstances
                    ?.Select(m => MissionInstanceTranslator.ToDB(m))
                    ?.ToList() ?? []
            };
        }
    }
}
