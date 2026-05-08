using BL.Extensions;
using BL.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;
using Translators.Translators;

namespace BL.Cache
{
    /// <summary>
    /// Singleton in-memory cache for soldier data. Stores both DB entities and BL models.
    /// Auto-reloads every 5 minutes. Decrypts PII on load. Thread-safe via locking.
    /// </summary>
    public class SoldiersCache
    {
        private static SoldiersCache Instance { get; set; }
        public static IEnumerable<Soldier> Soldiers
        {
            get
            {
                return soldiersDic?.Values?.ToList() ?? [];
            }
        }
        public static IEnumerable<DataLayer.Models.Soldier> DbSoldiers
        {
            get
            {
                return dbSoldiersDic?.Values?.ToList() ?? [];
            }
        }

        private static Dictionary<int, Soldier> soldiersDic = [];

        private static Dictionary<int, DataLayer.Models.Soldier> dbSoldiersDic = [];
        private SoldiersCache() 
        {
            Logger.Log("Creating Soldier Cache");
            var timer = new Timer(state =>
            {
                ReloadCache();
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Reloads all soldiers from the database into memory.
        /// Decrypts PII fields and builds both DB and BL dictionaries keyed by soldier ID.
        /// </summary>
        public static void ReloadCache()
        {
            using var db = new DataLayer.ShabzakDB();
            

            dbSoldiersDic = db.Soldiers
                .Include(s => s.Missions)
                    .ThenInclude(sm => sm.MissionPosition)
                .Include(s => s.Missions)
                    .ThenInclude(m => m.MissionInstance)
                .Include(s => s.Vacations)
                .ToList()
                .Select(s => s.Decrypt())
                .GroupBy(s => s.Id)
                .ToDictionary(k => k.Key, v => v.Single());


            soldiersDic = DbSoldiers
                .Select(s => SoldierTranslator.ToBL(s, true, true, true))
                .GroupBy(s => s.Id)
                .ToDictionary(k => k.Key, v => v.Single());

            Logger.Log($"Loaded {soldiersDic.Count()} soldiers to cache");
        }

        /// <summary>
        /// Returns the singleton instance, creating it if necessary (double-checked locking).
        /// </summary>
        public static SoldiersCache GetInstance()
        {
            lock(Soldiers)
            {
                if(Instance == null)
                {
                    lock (Soldiers)
                    {
                        Instance = new SoldiersCache();
                    }
                }
                return Instance;
            }
        }

        /// <summary>
        /// Returns all cached soldiers as BL models. Optionally forces a cache reload first.
        /// </summary>
        /// <param name="reloadCache">If true, reloads the cache before returning.</param>
        /// <returns>List of all cached soldiers.</returns>
        public List<Soldier> GetSoldiers(bool reloadCache = false)
        {
            if (reloadCache)
            {
                ReloadCache();
            }
            return Soldiers.ToList();
        }
        /// <summary>
        /// Returns all cached soldiers as raw DB entities.
        /// </summary>
        public List<DataLayer.Models.Soldier> GetDBSoldiers() => DbSoldiers.ToList();

        /// <summary>
        /// Updates a soldier in the cache by removing the old entry and adding the new one.
        /// Thread-safe via lock.
        /// </summary>
        /// <param name="soldier">The updated soldier BL model.</param>
        public void UpdateSoldier(Soldier soldier)
        {
            lock(soldiersDic)
            {
                if (!soldiersDic.ContainsKey(soldier.Id))
                {
                    throw new ArgumentException("Soldier not found");
                }
                soldiersDic[soldier.Id] = soldier;
            }
        }
        
        /// <summary>
        /// Adds a new soldier to the BL cache dictionary.
        /// </summary>
        /// <param name="soldier">The soldier to add.</param>
        public void AddSoldier(Soldier soldier)
        {
            lock (soldiersDic)
            {
                soldiersDic.Add(soldier.Id, soldier);
            }
        }

        /// <summary>
        /// Retrieves a cached BL soldier by ID. Returns null if not found.
        /// </summary>
        /// <param name="id">The soldier's ID.</param>
        /// <returns>The cached soldier or null.</returns>
        public Soldier GetSoldierById(int id)
        {
            lock (soldiersDic)
            {
                if (soldiersDic.ContainsKey(id))
                {
                    return soldiersDic[id];
                }
            }
            return null;
        }

        /// <summary>
        /// Retrieves a cached DB soldier entity by ID. Returns null if not found.
        /// </summary>
        /// <param name="id">The soldier's ID.</param>
        /// <returns>The cached DB soldier entity or null.</returns>
        public DataLayer.Models.Soldier GetDbSoldierById(int id)
        {
            lock (dbSoldiersDic)
            {
                if (dbSoldiersDic.ContainsKey(id))
                {
                    return dbSoldiersDic[id];
                }
            }
            return null;
        }

        /// <summary>
        /// Asynchronously reloads the cache on a background thread.
        /// </summary>
        public static Task ReloadAsync()
        {
            return Task.Factory.StartNew(() =>
            {
                ReloadCache();
            });
        }

        /// <summary>
        /// Checks if a soldier with the given ID exists in the cache.
        /// </summary>
        /// <param name="soldierId">The soldier's ID to check.</param>
        /// <returns>True if the soldier exists in cache.</returns>
        public bool Exist(int soldierId)
        {
            return soldiersDic.ContainsKey(soldierId);
        }
        
    }
}
