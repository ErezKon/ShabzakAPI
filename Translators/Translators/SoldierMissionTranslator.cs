using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;
using Translators.Extensions;

namespace Translators.Translators
{
    /// <summary>
    /// Translates SoldierMission (assignment) entities between DataLayer DB models and BL models.
    /// Links soldiers to mission instances with position assignments.
    /// </summary>
    public abstract class SoldierMissionTranslator
    {
        /// <summary>
        /// Converts a BL SoldierMission model to a DB entity.
        /// </summary>
        public static DataLayer.Models.SoldierMission ToDB(SoldierMission sol)
        {
            return new DataLayer.Models.SoldierMission
            {
                Id = sol.Id,
                MissionInstanceId = sol.MissionInstance?.Id ?? 0,
                SoldierId = sol.Soldier.Id,
                MissionPositionId = sol.MissionPosition.Id
            };
        }

        /// <summary>
        /// Converts a DB SoldierMission entity to a BL model.
        /// Optionally includes nested soldier and mission instance data.
        /// Decrypts soldier PII during translation.
        /// </summary>
        public static SoldierMission ToBL(DataLayer.Models.SoldierMission sol, bool includeSoldier = true, bool includeMissionInstance = true)
        {
            Soldier soldier = null;
            if (includeSoldier && sol.Soldier != null)
            {
                DataLayer.Models.Soldier dbSoldier = sol.Soldier;
                try
                {
                    dbSoldier = dbSoldier.Decrypt();
                }
                catch (Exception)
                {
                
                }
                soldier = SoldierTranslator.ToBL(dbSoldier);
            }
            return new SoldierMission
            {
                Id = sol.Id,
                Soldier = soldier,
                MissionInstance = includeMissionInstance ? MissionInstanceTranslator.ToBL(sol.MissionInstance, false, false) : null,
                MissionPosition = MissionPositionTranslator.ToBL(sol.MissionPosition)
            };
        }

        /// <summary>
        /// Converts a DB SoldierMission entity to a BL model using custom fetcher functions.
        /// Allows cache-based lookups instead of DB navigation for better performance.
        /// </summary>
        public static SoldierMission ToBL(DataLayer.Models.SoldierMission sol, Func<int, Soldier>? soldierFetcher, Func<int, MissionPosition>? missionPositonFetcher, Func<int, MissionInstance>? missionInstanceFetcher)
        {
            return new SoldierMission
            {
                Id = sol.Id,
                Soldier = soldierFetcher != null ? soldierFetcher(sol.SoldierId) : SoldierTranslator.ToBL(sol.Soldier),
                MissionInstance = missionInstanceFetcher != null ? missionInstanceFetcher(sol.MissionInstanceId) : MissionInstanceTranslator.ToBL(sol.MissionInstance),
                MissionPosition = missionPositonFetcher != null ? missionPositonFetcher(sol.MissionPositionId) : MissionPositionTranslator.ToBL(sol.MissionPosition)
            };
        }

    }
}
