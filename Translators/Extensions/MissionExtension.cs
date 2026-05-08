using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Encryption;

namespace Translators.Extensions
{
    /// <summary>
    /// Low-level encryption/decryption extension methods for DB Mission entities.
    /// Handles Name, Description, FromTime, and ToTime fields.
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
        /// Encrypts PII fields (Name, Description, FromTime, ToTime) on a DB mission entity.
        /// Null/empty fields are replaced with encrypted "N/A".
        /// </summary>
        public static DataLayer.Models.Mission Encrypt(this DataLayer.Models.Mission mission)
        {
            mission.Name = encryptor.Encrypt(mission.Name);
            mission.Description = string.IsNullOrWhiteSpace(mission.Description) ? naEncrypted : encryptor.Encrypt(mission.Description);
            mission.FromTime = string.IsNullOrWhiteSpace(mission.FromTime) ? naEncrypted : encryptor.Encrypt(mission.FromTime);
            mission.ToTime = string.IsNullOrWhiteSpace(mission.ToTime) ? naEncrypted : encryptor.Encrypt(mission.ToTime);
            return mission;
        }
        /// <summary>
        /// Decrypts PII fields (Name, Description, FromTime, ToTime) on a DB mission entity.
        /// </summary>
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
