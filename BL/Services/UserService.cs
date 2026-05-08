using BL.Cache;
using BL.Extensions;
using DataLayer;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Translators.Encryption;
using Translators.Models;

namespace BL.Services
{
    /// <summary>
    /// Business logic service for user authentication and account management.
    /// Handles user creation from soldier records, login verification, and password operations.
    /// </summary>
    public class UserService
    {
        private readonly UsersCache _usersCache;
        /// <summary>
        /// Initializes the user service with the users cache.
        /// </summary>
        /// <param name="userCache">The users cache instance.</param>
        public UserService(UsersCache userCache)
        {
            _usersCache = userCache;
        }
        /// <summary>
        /// Creates a new user account in the database. Generates a salt, hashes the password
        /// with SHA-512(password + salt), and saves to DB.
        /// </summary>
        /// <param name="name">The username for the new user.</param>
        /// <param name="password">The password for the new user.</param>
        /// <param name="soldierId">The ID of the linked soldier (optional).</param>
        /// <param name="role">The role of the new user (default: Regular).</param>
        /// <returns>The created user entity.</returns>
        public User CreateUser(string name, string password, int? soldierId = null, UserRole role = UserRole.Regular)
        {
            var user = new User
            {
                Name = name,
                Password = password,
                Role = role,
                Activated = false,
                Enabled = true,
                SoldierId = soldierId
            };
            user.Salt = Guid.NewGuid().ToString();
            using var db = new DataLayer.ShabzakDB();
            user.Password = Sha512Encryptor.Encrypt($"{password}{user.Salt}");
            var dbUser = user.ToDB();//.Encrypt();
            db.Users.Add(dbUser);
            db.SaveChanges();
            user.Id = dbUser.Id;
            _usersCache.AddUser(dbUser);
            return user;
        }

        /// <summary>
        /// Creates a user account from a soldier record.
        /// Username = SHA-512(personalNumber), Password = SHA-512(phone).
        /// The user is linked to the soldier via SoldierId.
        /// </summary>
        /// <param name="soldier">The soldier to create an account for.</param>
        /// <returns>The created user entity.</returns>
        public User CreateFromSoldier(Soldier soldier)
        {
            var username = Sha512Encryptor.Encrypt(soldier.PersonalNumber);
            var password = Sha512Encryptor.Encrypt(soldier.Phone.Replace("-", string.Empty));
            return CreateUser(username, password, soldier.Id);
        }

        /// <summary>
        /// Authenticates a user. Looks up by hashed username, verifies password as
        /// SHA-512(password + salt). On success, loads linked soldier data and clears sensitive fields.
        /// </summary>
        /// <param name="username">SHA-512 hashed username (derived from personalNumber).</param>
        /// <param name="password">SHA-512 hashed password (derived from phone).</param>
        /// <param name="ip">The client's IP address (for logging).</param>
        /// <returns>The authenticated user with linked soldier data (password/salt cleared).</returns>
        /// <exception cref="ArgumentNullException">Thrown if username or password is null.</exception>
        public User? Login(string username, string password, string ip)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentNullException("Username or password cannot be null.");
            }
            var user = _usersCache.GetUser(username) ?? throw new ArgumentException("User not found.");
            var encPass = Sha512Encryptor.Encrypt($"{password}{user.Salt}");
            if (user.Password.Equals(encPass))
            {
                var ret = user.ToBL().LoadSoldier();
                ret.Password = "";
                ret.Salt = "";
                ret.Soldier.Missions = ret.Soldier?.Missions
                    ?.Where(m => DateTime.Parse(m.MissionInstance.FromTime) > DateTime.Now)
                    ?.ToList() ?? [];
                return ret;
            }
            return null;
        }
    }
}
