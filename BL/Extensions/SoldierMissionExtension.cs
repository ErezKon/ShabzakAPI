using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;
using Translators.Translators;

namespace BL.Extensions
{
    public static class SoldierMissionExtension
    {
        public static SoldierMission ToBL(this DataLayer.Models.SoldierMission soldierMission) => SoldierMissionTranslator.ToBL(soldierMission);

        public static DataLayer.Models.SoldierMission ToDB(this SoldierMission soldierMission) => SoldierMissionTranslator.ToDB(soldierMission);
    }
}
