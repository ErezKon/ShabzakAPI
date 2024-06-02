using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    public abstract class SoldierMissionTranslator
    {
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

        public static SoldierMission ToBL(DataLayer.Models.SoldierMission sol, bool includeSoldier = true, bool includeMission = false)
        {
            return new SoldierMission
            {
                Id = sol.Id,
                Soldier = includeSoldier ? SoldierTranslator.ToBL(sol.Soldier) : null,
                MissionInstance = includeMission ? MissionInstanceTranslator.ToBL(sol.MissionInstance, includeMission) : null,
                MissionPosition = MissionPositionTranslator.ToBL(sol.MissionPosition)
            };
        }

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
