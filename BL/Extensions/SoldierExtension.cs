using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;
using Translators.Translators;

namespace BL.Extensions
{
    public static class SoldierExtension
    {
        private static readonly string naEncrypted;
        private static readonly AESEncryptor encryptor;


        static SoldierExtension() 
        {
            encryptor = new AESEncryptor();
            naEncrypted = encryptor.Encrypt("N/A");
        }

        public static DataLayer.Models.Soldier Encrypt(this DataLayer.Models.Soldier soldier)
        {
            if(string.IsNullOrEmpty(soldier.Name))
            {
                soldier.Name = naEncrypted;
            } else
            {
                soldier.Name = encryptor.Encrypt(soldier.Name);
            }
            if (string.IsNullOrEmpty(soldier.PersonalNumber))
            {
                soldier.PersonalNumber = naEncrypted;
            }
            else
            {
                soldier.PersonalNumber = encryptor.Encrypt(soldier.PersonalNumber);
            }
            if (string.IsNullOrEmpty(soldier.Phone))
            {
                soldier.Phone = naEncrypted;
            }
            else
            {
                soldier.Phone = encryptor.Encrypt(soldier.Phone);
            }
            if (string.IsNullOrEmpty(soldier.Position))
            {
                soldier.Position = naEncrypted;
            }
            else
            {
                soldier.Position = encryptor.Encrypt(soldier.Position);
            }
            if (string.IsNullOrEmpty(soldier.Platoon))
            {
                soldier.Platoon = naEncrypted;
            }
            else
            {
                soldier.Platoon = encryptor.Encrypt(soldier.Platoon);
            }
            if (string.IsNullOrEmpty(soldier.Company))
            {
                soldier.Company = naEncrypted;
            }
            else
            {
                soldier.Company = encryptor.Encrypt(soldier.Company);
            }
            return soldier;
        }


        public static Soldier Encrypt(this Soldier soldier)
        {
            if (string.IsNullOrEmpty(soldier.Name))
            {
                soldier.Name = naEncrypted;
            }
            else
            {
                soldier.Name = encryptor.Encrypt(soldier.Name);
            }
            if (string.IsNullOrEmpty(soldier.PersonalNumber))
            {
                soldier.PersonalNumber = naEncrypted;
            }
            else
            {
                soldier.PersonalNumber = encryptor.Encrypt(soldier.PersonalNumber);
            }
            if (string.IsNullOrEmpty(soldier.Phone))
            {
                soldier.Phone = naEncrypted;
            }
            else
            {
                soldier.Phone = encryptor.Encrypt(soldier.Phone);
            }
            if (string.IsNullOrEmpty(soldier.Platoon))
            {
                soldier.Platoon = naEncrypted;
            }
            else
            {
                soldier.Platoon = encryptor.Encrypt(soldier.Platoon);
            }
            if (string.IsNullOrEmpty(soldier.Company))
            {
                soldier.Company = naEncrypted;
            }
            else
            {
                soldier.Company = encryptor.Encrypt(soldier.Company);
            }
            return soldier;
        }



        public static DataLayer.Models.Soldier Decrypt(this DataLayer.Models.Soldier soldier)
        {
            if (soldier != null)
            {
                soldier.Name = encryptor.Decrypt(soldier.Name);
                soldier.Phone = encryptor.Decrypt(soldier.Phone);
                soldier.PersonalNumber = encryptor.Decrypt(soldier.PersonalNumber);
                soldier.Position = encryptor.Decrypt(soldier.Position);
                if (soldier.Platoon.Length > 1)
                {
                    soldier.Platoon = encryptor.Decrypt(soldier.Platoon);
                }
                soldier.Company = encryptor.Decrypt(soldier.Company);
            }
            return soldier;
        }

        public static Soldier Decrypt(this Soldier soldier)
        {
            if(soldier != null)
            {
                soldier.Name = encryptor.Decrypt(soldier.Name);
                soldier.Phone = encryptor.Decrypt(soldier.Phone);
                soldier.PersonalNumber = encryptor.Decrypt(soldier.PersonalNumber);
                soldier.Platoon = encryptor.Decrypt(soldier.Platoon);
                soldier.Company = encryptor.Decrypt(soldier.Company);
            }
            return soldier;
        }

        public static Soldier ToBL(this DataLayer.Models.Soldier soldier) => SoldierTranslator.ToBL(soldier);
        public static DataLayer.Models.Soldier ToDB(this Soldier soldier) => SoldierTranslator.ToDB(soldier);
    }
}
