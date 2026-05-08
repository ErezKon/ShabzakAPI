using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Encryption;
using Translators.Models;
using Translators.Translators;

namespace BL.Extensions
{
    /// <summary>
    /// Extension methods for encrypting/decrypting, translating, and querying Soldier entities.
    /// Handles AES-256 encryption of PII fields (Name, Phone, PersonalNumber, Platoon, Company).
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
        /// Encrypts PII fields on a DB soldier entity using AES-256.
        /// </summary>
        public static DataLayer.Models.Soldier Encrypt(this DataLayer.Models.Soldier soldier)
        {
            var ret = Translators.Extensions.SoldierExtension.Encrypt(soldier);
            return ret;
        }


        /// <summary>
        /// Encrypts PII fields on a BL soldier model using AES-256.
        /// Null/empty fields are replaced with encrypted "N/A".
        /// </summary>
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



        /// <summary>
        /// Decrypts PII fields on a DB soldier entity using AES-256.
        /// </summary>
        public static DataLayer.Models.Soldier Decrypt(this DataLayer.Models.Soldier soldier)
        {
            var ret = Translators.Extensions.SoldierExtension.Decrypt(soldier);
            return ret;
        }

        /// <summary>
        /// Decrypts PII fields on a BL soldier model using AES-256.
        /// </summary>
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

        /// <summary>
        /// Translates a DB soldier entity to a BL model with optional missions and vacations.
        /// </summary>
        public static Soldier ToBL(this DataLayer.Models.Soldier soldier, bool includeMissions = true, bool includeVacations = true) => SoldierTranslator.ToBL(soldier, includeMissions, includeVacations);
        /// <summary>
        /// Translates a BL soldier model to a DB entity.
        /// </summary>
        public static DataLayer.Models.Soldier ToDB(this Soldier soldier) => SoldierTranslator.ToDB(soldier);

        /// <summary>
        /// Checks if the soldier holds any commanding position.
        /// </summary>
        public static bool IsCommander(this Soldier soldier)
        {
            return soldier.Positions.Any(p => p.IsCommandingPosition());
        }

        /// <summary>
        /// Checks if the soldier holds any officer position.
        /// </summary>
        public static bool IsOfficer(this Soldier soldier)
        {
            return soldier.Positions.Any(p => p.IsOfficerPosition());
        }

        /// <summary>
        /// Parses the comma-separated position string from a DB soldier entity
        /// into a list of Position enums.
        /// </summary>
        public static List<DataLayer.Models.Position> GetSoldierPositions(this DataLayer.Models.Soldier soldier)
        {
            return soldier.Position.Split(",", StringSplitOptions.RemoveEmptyEntries)
                    .Select(s =>
                    {
                        if (int.TryParse(s, out int numericValue))
                        {
                            return (DataLayer.Models.Position)numericValue;
                        }
                        return DataLayer.Models.Position.Simple;
                    })
                    .ToList();
        }
    }
}
