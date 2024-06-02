using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    public abstract class MissionPositionTranslator
    {
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
