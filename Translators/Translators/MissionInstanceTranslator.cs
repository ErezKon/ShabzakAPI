using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    public abstract class MissionInstanceTranslator
    {
        public static MissionInstance ToBL(DataLayer.Models.MissionInstance missionInstance, bool includeMission = true, bool includeSoldier = true)
        {
            if(missionInstance == null)
            {
                return null;
            }
            return new MissionInstance
            {
                Id = missionInstance.Id,
                FromTime = missionInstance.FromTime.ToString(),
                ToTime = missionInstance.ToTime.ToString(),
                SoldierMissions = missionInstance.Soldiers
                    ?.Select(s => SoldierMissionTranslator.ToBL(s, includeSoldier, includeMission))
                    ?.ToList() ?? []
            };
        }
        public static DataLayer.Models.MissionInstance ToDB(MissionInstance missionInstance)
        {
            if (missionInstance == null)
            {
                return null;
            }
            return new DataLayer.Models.MissionInstance
            {
                Id = missionInstance.Id,
                FromTime = Convert.ToDateTime(missionInstance.FromTime),
                ToTime = Convert.ToDateTime(missionInstance.ToTime),
                Soldiers = missionInstance.SoldierMissions
                    ?.Select(s => SoldierMissionTranslator.ToDB(s))
                    ?.ToList() ?? []
            };
        }
    }
}
