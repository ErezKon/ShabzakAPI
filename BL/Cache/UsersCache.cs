using BL.Extensions;
using BL.Logging;
using DataLayer.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Encryption;
using Translators.Models;
using Translators.Translators;

namespace BL.Cache
{
    public class UsersCache
    {
        private static UsersCache Instance { get; set; }
        private static Object mutex = new Object();
        private Dictionary<string, DataLayer.Models.User> usersDic = [];
        private UsersCache()
        {
            Logger.Log("Creating Users Cache");
            var timer = new Timer(state =>
            {
                ReloadCache();
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        public static UsersCache GetInstance()
        {
            lock (mutex)
            {
                if (Instance == null)
                {
                    lock (mutex)
                    {
                        Instance = new UsersCache();
                    }
                }
                return Instance;
            }
        }

        public void ReloadCache()
        {
            using var db = new DataLayer.ShabzakDB();

            usersDic = db.Users
                .ToList()
                .Select(s => s.Decrypt())
                .GroupBy(s => s.Name)
                .ToDictionary(k => k.Key, v => v.Single());

            Logger.Log($"Loaded {usersDic.Count()} users to cache");
        }

        public DataLayer.Models.User? GetUser(string username)
        {
            lock (usersDic)
            {
                if (usersDic.ContainsKey(username))
                {
                    return usersDic[username];
                }
                var db = new DataLayer.ShabzakDB();
                var user = db.Users
                    .FirstOrDefault(u => u.Name.Equals(username));
                if(user != null)
                {
                    AddUser(user.Decrypt());
                }
                return user;
            }
        }

        public void AddUser(DataLayer.Models.User user)
        {
            lock (usersDic)
            {
                usersDic[user.Name] = user;
            }
        }
    }
}
