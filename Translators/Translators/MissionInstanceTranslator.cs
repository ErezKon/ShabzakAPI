using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    /// <summary>
    /// Translates MissionInstance entities between DataLayer DB models and BL models.
    /// Formats DateTime to/from "dd/MM/yyyy HH:mm" string representation.
    /// </summary>
    public abstract class MissionInstanceTranslator
    {
        /// <summary>
        /// Converts a DB MissionInstance to a BL model with formatted date strings.
        /// Optionally includes nested soldier and mission data.
        /// </summary>
        /// <returns>The translated BL mission instance, or null if input is null.</returns>
        public static MissionInstance ToBL(DataLayer.Models.MissionInstance missionInstance, bool includeMissionInstance = true, bool includeSoldier = true)
        {
            if(missionInstance == null)
            {
                return null;
            }
            var soldierMissions = new List<SoldierMission>();
            foreach(var s in missionInstance.Soldiers ?? []) 
            {
                var sm = SoldierMissionTranslator.ToBL(s, includeSoldier, includeMissionInstance);
                soldierMissions.Add(sm);
            }
            return new MissionInstance
            {
                Id = missionInstance.Id,
                FromTime = missionInstance.FromTime.ToString("dd/MM/yyyy HH:mm"),
                ToTime = missionInstance.ToTime.ToString("dd/MM/yyyy HH:mm"),
                IsFilled = missionInstance.IsFilled,
                SoldierMissions = soldierMissions
            };
        }
        /// <summary>
        /// Converts a BL MissionInstance to a DB entity, parsing date strings back to DateTime.
        /// </summary>
        /// <returns>The translated DB mission instance, or null if input is null.</returns>
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
                IsFilled = missionInstance.IsFilled,
                Soldiers = missionInstance.SoldierMissions
                    ?.Select(s => SoldierMissionTranslator.ToDB(s))
                    ?.ToList() ?? []
            };
        }
    }
}
