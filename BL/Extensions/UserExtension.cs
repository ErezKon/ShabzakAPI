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
    public static class UserExtension
    {
        private static readonly string naEncrypted;
        private static readonly AESEncryptor encryptor;
        private static readonly SoldiersCache soldierCache;

        static UserExtension()
        {
            encryptor = new AESEncryptor();
            naEncrypted = encryptor.Encrypt("N/A");
            soldierCache = SoldiersCache.GetInstance();
        }

        public static User Encrypt(this User user)
        {
            user.Name = encryptor.Encrypt(user.Name);
            user.Password = encryptor.Encrypt(user.Password);
            user.Salt = encryptor.Encrypt(user.Salt);
            return user;
        }
        public static User Decrypt(this User user)
        {
            user.Name = encryptor.Decrypt(user.Name);
            user.Password = encryptor.Decrypt(user.Password);
            user.Salt = encryptor.Decrypt(user.Salt);
            return user;
        }

        public static DataLayer.Models.User Encrypt(this DataLayer.Models.User user)
        {
            user.Name = encryptor.Encrypt(user.Name);
            user.Password = encryptor.Encrypt(user.Password);
            user.Salt = encryptor.Encrypt(user.Salt);
            return user;
        }
        public static DataLayer.Models.User Decrypt(this DataLayer.Models.User user)
        {
            user.Name = encryptor.Decrypt(user.Name);
            user.Password = encryptor.Decrypt(user.Password);
            user.Salt = encryptor.Decrypt(user.Salt);
            return user;
        }
        public static User ToBL(this DataLayer.Models.User user) => UserTranslator.ToBL(user);
        public static DataLayer.Models.User ToDB(this User user) => UserTranslator.ToDB(user);

        public static User LoadSoldier(this User user)
        {
            if (user.SoldierId.HasValue && soldierCache.Exist(user.SoldierId.Value))
            {
                user.Soldier = soldierCache.GetSoldierById(user.SoldierId.Value);
            }
            return user;
        }
    }
}
