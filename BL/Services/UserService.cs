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
    public class UserService
    {
        private readonly UsersCache _usersCache;
        public UserService(UsersCache userCache) 
        {
            _usersCache = userCache;
        }
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
            var dbUser = user.ToDB().Encrypt();
            db.Users.Add(dbUser);
            db.SaveChanges();
            user.Id = dbUser.Id;
            _usersCache.AddUser(dbUser);
            return user;
        }

        public User CreateFromSoldier(Soldier soldier)
        {
            var username = Sha512Encryptor.Encrypt(soldier.PersonalNumber);
            var password = Sha512Encryptor.Encrypt(soldier.Phone.Replace("-", string.Empty));
            return CreateUser(username, password, soldier.Id);
        }

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
