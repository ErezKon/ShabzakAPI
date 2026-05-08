using DataLayer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Encryption;

namespace Translators.Extensions
{
    /// <summary>
    /// Low-level encryption/decryption extension methods for DB Soldier entities.
    /// Handles Name, Phone, PersonalNumber, Position, Platoon, and Company fields.
    /// </summary>
    public static class SoldierExtension
    {
        private static readonly string naEncrypted;
        private static readonly AESEncryptor encryptor;


        static SoldierExtension()
        {
            encryptor = new AESEncryptor();
            naEncrypted = encryptor.Encrypt("N/A");
        }

        /// <summary>
        /// Decrypts PII fields on a DB soldier entity using AES-256.
        /// </summary>
        public static Soldier Decrypt(this Soldier soldier)
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
        /// <summary>
        /// Encrypts PII fields on a DB soldier entity using AES-256.
        /// Null/empty fields are replaced with encrypted "N/A".
        /// </summary>
        public static DataLayer.Models.Soldier Encrypt(this DataLayer.Models.Soldier soldier)
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
    }
}
