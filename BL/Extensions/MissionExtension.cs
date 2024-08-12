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

        public static Mission ToBL(this DataLayer.Models.Mission mission, bool includeSoldier = false, bool includeMission = false) => MissionTranslator.ToBL(mission, includeSoldier, includeMission);

        public static DataLayer.Models.Mission ToDB(this Mission mission) => MissionTranslator.ToDB(mission);

        public static DataLayer.Models.Mission Encrypt(this DataLayer.Models.Mission mission)
        {
            var ret = Translators.Extensions.MissionExtension.Encrypt(mission);
            return ret;
        }
        public static DataLayer.Models.Mission Decrypt(this DataLayer.Models.Mission mission)
        {
            var ret = Translators.Extensions.MissionExtension.Decrypt(mission);
            return ret;
        }
    }
}
