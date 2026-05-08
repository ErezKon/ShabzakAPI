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
    /// Extension methods for translating SoldierMission (assignment) entities between DB and BL layers.
    /// </summary>
    public static class SoldierMissionExtension
    {
        /// <summary>
        /// Translates a DB soldier-mission assignment to a BL model.
        /// </summary>
        public static SoldierMission ToBL(this DataLayer.Models.SoldierMission soldierMission) => SoldierMissionTranslator.ToBL(soldierMission);

        /// <summary>
        /// Translates a BL soldier-mission assignment to a DB entity.
        /// </summary>
        public static DataLayer.Models.SoldierMission ToDB(this SoldierMission soldierMission) => SoldierMissionTranslator.ToDB(soldierMission);
    }
}
