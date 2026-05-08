using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Translators;
using Translators.Models;
using Translators.Encryption;
using BL.Cache;

namespace BL.Extensions
{
    /// <summary>
    /// Extension methods for encrypting/decrypting and translating User entities.
    /// Also provides soldier-loading capability for authenticated users.
    /// </summary>
    public static class UserExtension
    {
        private static readonly string naEncrypted;
        private static readonly AESEncryptor encryptor;
        private static readonly SoldiersCache soldierCache;
        private static readonly MissionsCache missionCache;

        static UserExtension()
        {
            encryptor = new AESEncryptor();
            naEncrypted = encryptor.Encrypt("N/A");
            soldierCache = SoldiersCache.GetInstance();
            missionCache = MissionsCache.GetInstance();
        }

        /// <summary>
        /// Encrypts sensitive fields (Name, Password, Salt) on a BL user model using AES-256.
        /// </summary>
        public static User Encrypt(this User user)
        {
            user.Name = encryptor.Encrypt(user.Name);
            user.Password = encryptor.Encrypt(user.Password);
            user.Salt = encryptor.Encrypt(user.Salt);
            return user;
        }
        /// <summary>
        /// Decrypts sensitive fields on a BL user model using AES-256.
        /// </summary>
        public static User Decrypt(this User user)
        {
            user.Name = encryptor.Decrypt(user.Name);
            user.Password = encryptor.Decrypt(user.Password);
            user.Salt = encryptor.Decrypt(user.Salt);
            return user;
        }

        /// <summary>
        /// Encrypts sensitive fields on a DB user entity using AES-256.
        /// </summary>
        public static DataLayer.Models.User Encrypt(this DataLayer.Models.User user)
        {
            user.Name = encryptor.Encrypt(user.Name);
            user.Password = encryptor.Encrypt(user.Password);
            user.Salt = encryptor.Encrypt(user.Salt);
            return user;
        }
        /// <summary>
        /// Decrypts sensitive fields on a DB user entity using AES-256.
        /// </summary>
        public static DataLayer.Models.User Decrypt(this DataLayer.Models.User user)
        {
            user.Name = encryptor.Decrypt(user.Name);
            user.Password = encryptor.Decrypt(user.Password);
            user.Salt = encryptor.Decrypt(user.Salt);
            return user;
        }
        /// <summary>
        /// Translates a DB user entity to a BL model.
        /// </summary>
        public static User ToBL(this DataLayer.Models.User user) => UserTranslator.ToBL(user);
        /// <summary>
        /// Translates a BL user model to a DB entity.
        /// </summary>
        public static DataLayer.Models.User ToDB(this User user) => UserTranslator.ToDB(user);

        /// <summary>
        /// Loads the linked soldier data from cache into the user's Soldier property.
        /// Also enriches the soldier's missions from the missions cache.
        /// </summary>
        /// <param name="user">The user to enrich with soldier data.</param>
        /// <returns>The user with loaded soldier data.</returns>
        public static User LoadSoldier(this User user)
        {
            if (user.SoldierId.HasValue && soldierCache.Exist(user.SoldierId.Value))
            {
                user.Soldier = soldierCache.GetSoldierById(user.SoldierId.Value);
                var cachedMissions = user.Soldier.Missions
                    .Where(m => missionCache.ContainsKey(m.Id))
                    .Select(m => missionCache.GetMissionById(m.Id))
                    .ToList();
                //user.Soldier.Missions = cachedMissions;
            }
            return user;
        }
    }
}
