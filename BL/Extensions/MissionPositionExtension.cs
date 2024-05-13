using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;
using Translators.Translators;

namespace BL.Extensions
{
    public static class MissionPositionExtension
    {
        public static MissionPosition ToBL(this DataLayer.Models.MissionPositions missionPosition) => MissionPositionTranslator.ToBL(missionPosition);

        public static DataLayer.Models.MissionPositions ToDB(this MissionPosition missionPosition) => MissionPositionTranslator.ToDB(missionPosition);

    }
}
