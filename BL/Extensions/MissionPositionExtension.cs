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
    /// Extension methods for translating MissionPosition entities between DB and BL layers.
    /// </summary>
    public static class MissionPositionExtension
    {
        /// <summary>
        /// Translates a DB mission position to a BL model.
        /// </summary>
        public static MissionPosition ToBL(this DataLayer.Models.MissionPositions missionPosition) => MissionPositionTranslator.ToBL(missionPosition);

        /// <summary>
        /// Translates a BL mission position to a DB entity.
        /// </summary>
        public static DataLayer.Models.MissionPositions ToDB(this MissionPosition missionPosition) => MissionPositionTranslator.ToDB(missionPosition);

    }
}
