using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;
using Translators.Translators;

namespace BL.Extensions
{
    public static class MissionInstanceExtension
    {
        public static MissionInstance ToBL(this DataLayer.Models.MissionInstance missionInstance, bool includeMission = true, bool includeSoldier = true) => MissionInstanceTranslator.ToBL(missionInstance,includeMission, includeSoldier);

        public static DataLayer.Models.MissionInstance ToDB(this MissionInstance missionInstance) => MissionInstanceTranslator.ToDB(missionInstance);

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
