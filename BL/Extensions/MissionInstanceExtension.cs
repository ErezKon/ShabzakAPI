using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;
using Translators.Translators;

namespace BL.Extensions
{
    /// <summary>
    /// Extension methods for translating MissionInstance entities between DB and BL layers,
    /// and checking fill status.
    /// </summary>
    public static class MissionInstanceExtension
    {
        /// <summary>
        /// Translates a DB mission instance to a BL model.
        /// </summary>
        public static MissionInstance ToBL(this DataLayer.Models.MissionInstance missionInstance, bool includeMission = true, bool includeSoldier = true) => MissionInstanceTranslator.ToBL(missionInstance,includeMission, includeSoldier);

        /// <summary>
        /// Translates a BL mission instance to a DB entity.
        /// </summary>
        public static DataLayer.Models.MissionInstance ToDB(this MissionInstance missionInstance) => MissionInstanceTranslator.ToDB(missionInstance);

        /// <summary>
        /// Checks if a mission instance has all required positions filled.
        /// Compares total position count from MissionPositions against actual assigned soldiers.
        /// Updates the IsFilled property on the instance.
        /// </summary>
        /// <param name="instance">The DB mission instance to check.</param>
        /// <returns>True if all positions are assigned.</returns>
        public static bool IsInstanceFilled(this DataLayer.Models.MissionInstance instance)
        {
            var totalPositions = instance.Mission.MissionPositions.Sum(mp => mp.Count);
            using var db = new DataLayer.ShabzakDB();
            var totalAssignments = db.SoldierMission
                .Count(sm => sm.MissionInstanceId == instance.Id);
            instance.IsFilled = totalPositions == totalAssignments;
            return instance.IsFilled;
        }
    }
}
