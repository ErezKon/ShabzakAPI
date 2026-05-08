using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Translators.Encryption;
using Translators.Models;
using Translators.Translators;

namespace BL.Extensions
{
    /// <summary>
    /// Extension methods for translating and encrypting/decrypting Mission entities
    /// between DB and BL layers.
    /// </summary>
    public static class MissionExtension
    {
        private static readonly string naEncrypted;
        private static readonly AESEncryptor encryptor;

        static MissionExtension()
        {
            encryptor = new AESEncryptor();
            naEncrypted = encryptor.Encrypt("N/A");
        }

        /// <summary>
        /// Translates a DB mission entity to a BL model.
        /// </summary>
        public static Mission ToBL(this DataLayer.Models.Mission mission, bool includeSoldier = false, bool includeMission = false) => MissionTranslator.ToBL(mission, includeSoldier, includeMission);

        /// <summary>
        /// Translates a BL mission model to a DB entity.
        /// </summary>
        public static DataLayer.Models.Mission ToDB(this Mission mission) => MissionTranslator.ToDB(mission);

        /// <summary>
        /// Encrypts PII fields on a DB mission entity using AES-256.
        /// </summary>
        public static DataLayer.Models.Mission Encrypt(this DataLayer.Models.Mission mission)
        {
            var ret = Translators.Extensions.MissionExtension.Encrypt(mission);
            return ret;
        }
        /// <summary>
        /// Decrypts PII fields on a DB mission entity using AES-256.
        /// </summary>
        public static DataLayer.Models.Mission Decrypt(this DataLayer.Models.Mission mission)
        {
            var ret = Translators.Extensions.MissionExtension.Decrypt(mission);
            return ret;
        }
    }
}
