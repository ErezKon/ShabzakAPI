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
            var soldierMissions = new List<SoldierMission>();
            foreach(var s in missionInstance.Soldiers ?? []) 
            {
                var sm = SoldierMissionTranslator.ToBL(s, includeSoldier, includeMission);
                soldierMissions.Add(sm);
            }
            return new MissionInstance
            {
                Id = missionInstance.Id,
                FromTime = missionInstance.FromTime.ToString("dd/MM/yyyy HH:mm"),
                ToTime = missionInstance.ToTime.ToString("dd/MM/yyyy HH:mm"),
                SoldierMissions = soldierMissions
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
