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
    /// Singleton in-memory cache for user data. Stores DB user entities keyed by hashed username
    /// (plus a secondary index by user ID). Auto-reloads every 5 minutes. Thread-safe via a single lock object.
    /// Updated entities replace the cached instance (replace-on-update) instead of being mutated in place.
    /// </summary>
    public class UsersCache
    {
        private static UsersCache Instance { get; set; }
        private static readonly object mutex = new();
        private readonly object _usersLock = new();
        private Timer? _reloadTimer; // FIX: Store timer reference to prevent GC collection.
        private Dictionary<string, DataLayer.Models.User> usersDic = [];
        private Dictionary<int, DataLayer.Models.User> usersById = [];
        private UsersCache()
        {
            Logger.Log("Creating Users Cache");
            // FIX: Store timer in instance field — a local variable can be GC'd.
            _reloadTimer = new Timer(state =>
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
                Instance ??= new UsersCache();
                return Instance;
            }
        }

        /// <summary>
        /// Reloads all users from the database into memory.
        /// Builds a dictionary keyed by username (hashed) for fast lookup.
        /// </summary>
        public void ReloadCache()
        {
            using var db = DataLayer.DbFactory.Create();

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

            var byName = allUsers
            //.Select(s => s.Decrypt())
            .GroupBy(s => s.Name)
            .ToDictionary(k => k.Key, v => v.Single());
            var byId = allUsers.ToDictionary(u => u.Id);

            lock (_usersLock)
            {
                usersDic = byName;
                usersById = byId;
            }

            Logger.Log($"Loaded {byName.Count} users to cache");
        }

        /// <summary>
        /// Returns a snapshot of all cached users (DB entities). Callers must not mutate the returned entities.
        /// </summary>
        public List<DataLayer.Models.User> GetAllUsers()
        {
            lock (_usersLock)
            {
                return usersById.Values.ToList();
            }
        }

        /// <summary>
        /// Retrieves a user by ID. Falls back to a DB lookup (and caches the result) when not cached.
        /// </summary>
        /// <param name="userId">The user's ID.</param>
        /// <returns>The DB user entity, or null if not found.</returns>
        public DataLayer.Models.User? GetUserById(int userId)
        {
            lock (_usersLock)
            {
                if (usersById.TryGetValue(userId, out var cached))
                {
                    return cached;
                }
            }
            using var db = DataLayer.DbFactory.Create();
            var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId);
            if (user != null)
            {
                AddOrReplaceUser(user);
            }
            return user;
        }

        /// <summary>
        /// Adds a user to the cache or replaces the cached instance with the same ID (replace-on-update).
        /// </summary>
        /// <param name="user">The fresh DB user entity.</param>
        public void AddOrReplaceUser(DataLayer.Models.User user)
        {
            lock (_usersLock)
            {
                if (usersById.TryGetValue(user.Id, out var previous) && previous.Name != user.Name)
                {
                    usersDic.Remove(previous.Name);
                }
                usersDic[user.Name] = user;
                usersById[user.Id] = user;
            }
        }

        /// <summary>
        /// Removes a user (by ID) from the cache, e.g. after deletion.
        /// </summary>
        /// <param name="userId">The deleted user's ID.</param>
        public void RemoveUser(int userId)
        {
            lock (_usersLock)
            {
                if (usersById.Remove(userId, out var previous))
                {
                    usersDic.Remove(previous.Name);
                }
            }
        }

        /// <summary>
        /// Retrieves a user by hashed username. Falls back to DB lookup if not in cache,
        /// and adds the result to cache for future lookups.
        /// </summary>
        /// <param name="username">The SHA-512 hashed username to look up.</param>
        /// <returns>The DB user entity, or null if not found.</returns>
        public DataLayer.Models.User? GetUser(string username)
        {
            lock (_usersLock)
            {
                if (usersDic.TryGetValue(username, out var cached))
                {
                    return cached;
                }
            }
            using var db = DataLayer.DbFactory.Create();
            var user = db.Users
                .FirstOrDefault(u => u.Name.Equals(username));
            if(user != null)
            {
                AddUser(user);
            }
            return user;
        }

        /// <summary>
        /// Adds or updates a user in the cache dictionary.
        /// </summary>
        /// <param name="user">The DB user entity to cache.</param>
        public void AddUser(DataLayer.Models.User user) => AddOrReplaceUser(user);
    }
}
