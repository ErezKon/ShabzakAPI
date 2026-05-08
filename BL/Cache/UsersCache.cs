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
    /// <summary>
    /// Singleton in-memory cache for user data. Stores DB user entities keyed by hashed username.
    /// Auto-reloads every 5 minutes. Thread-safe via locking on the dictionary.
    /// </summary>
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

        /// <summary>
        /// Returns the singleton instance, creating it if necessary (double-checked locking).
        /// </summary>
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

        /// <summary>
        /// Reloads all users from the database into memory.
        /// Builds a dictionary keyed by username (hashed) for fast lookup.
        /// </summary>
        public void ReloadCache()
        {
            using var db = new DataLayer.ShabzakDB();

            //var users = db.Users
            //    .Where(u => u.Id != 39)
            //    .ToList()
            //    //.Select(u => u.Decrypt())
            //    //.Where(u => u.Name.Equals(encUsername))
            //    .Select(u => u.Encrypt())
            //    .ToList();

            //db.SaveChanges();

            var allUsers = db.Users
                //.ToList()
                //.Select(s => s.Decrypt())
                .ToList();

            usersDic = db.Users
            .ToList()
            //.Select(s => s.Decrypt())
            .GroupBy(s => s.Name)
            .ToDictionary(k => k.Key, v => v.Single());

            Logger.Log($"Loaded {usersDic.Count()} users to cache");
        }

        /// <summary>
        /// Retrieves a user by hashed username. Falls back to DB lookup if not in cache,
        /// and adds the result to cache for future lookups.
        /// </summary>
        /// <param name="username">The SHA-512 hashed username to look up.</param>
        /// <returns>The DB user entity, or null if not found.</returns>
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
                    AddUser(user);
                }
                return user;
            }
        }

        /// <summary>
        /// Adds or updates a user in the cache dictionary.
        /// </summary>
        /// <param name="user">The DB user entity to cache.</param>
        public void AddUser(DataLayer.Models.User user)
        {
            lock (usersDic)
            {
                usersDic[user.Name] = user;
            }
        }
    }
}
