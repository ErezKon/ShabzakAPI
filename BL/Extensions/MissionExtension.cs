using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;
using Translators.Translators;

namespace BL.Extensions
{
    public static class MissionExtension
    {
        private static readonly string naEncrypted;
        private static readonly AESEncryptor encryptor;

        static MissionExtension()
        {
            encryptor = new AESEncryptor();
            naEncrypted = encryptor.Encrypt("N/A");
        }

        public static Mission ToBL(this DataLayer.Models.Mission mission) => MissionTranslator.ToBL(mission);

        public static DataLayer.Models.Mission ToDB(this Mission mission) => MissionTranslator.ToDB(mission);

        public static DataLayer.Models.Mission Encrypt(this DataLayer.Models.Mission mission)
        {
            mission.Name = encryptor.Encrypt(mission.Name);
            mission.Description = encryptor.Encrypt(mission.Description);
            if(!string.IsNullOrEmpty(mission.FromTime))
            {
                mission.FromTime = encryptor.Encrypt(mission.FromTime);
            }
            if (!string.IsNullOrEmpty(mission.FromTime))
            {
                mission.ToTime = encryptor.Encrypt(mission.ToTime);
            }
            return mission;
        }
        public static DataLayer.Models.Mission Decrypt(this DataLayer.Models.Mission mission)
        {
            mission.Name = encryptor.Decrypt(mission.Name);
            mission.Description = encryptor.Decrypt(mission.Description);
            if (!string.IsNullOrEmpty(mission.FromTime))
            {
                mission.FromTime = encryptor.Decrypt(mission.FromTime);
            }
            if (!string.IsNullOrEmpty(mission.FromTime))
            {
                mission.ToTime = encryptor.Decrypt(mission.ToTime);
            }
            return mission;
        }
    }
}
