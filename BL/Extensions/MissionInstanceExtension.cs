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
    }
}
