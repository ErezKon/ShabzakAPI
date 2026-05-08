using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    /// <summary>
    /// Translates MissionPosition entities between DataLayer DB models and BL models.
    /// Converts Position enums between DB and BL enum types.
    /// </summary>
    public abstract class MissionPositionTranslator
    {
        /// <summary>
        /// Converts a DB MissionPositions entity to a BL MissionPosition model.
        /// </summary>
        /// <returns>The translated BL model, or null if input is null.</returns>
        public static MissionPosition ToBL(DataLayer.Models.MissionPositions mp)
        {
            if(mp == null)
            {
                return null;
            }
            return new MissionPosition()
            {
                Id = mp.Id,
                MissionId = mp.MissionId,
                Position = (Position) Enum.Parse(typeof(Position), mp.Position.ToString()),
                Count = mp.Count,
            };
        }


        /// <summary>
        /// Converts a BL MissionPosition model to a DB MissionPositions entity.
        /// </summary>
        /// <returns>The translated DB entity, or null if input is null.</returns>
        public static DataLayer.Models.MissionPositions ToDB(MissionPosition mp)
        {
            if (mp == null)
            {
                return null;
            }
            return new DataLayer.Models.MissionPositions()
            {
                Id = mp.Id,
                MissionId = mp.MissionId,
                Position = (DataLayer.Models.Position)Enum.Parse(typeof(DataLayer.Models.Position), mp.Position.ToString()),
                Count = mp.Count
            };
        }
    }
}
