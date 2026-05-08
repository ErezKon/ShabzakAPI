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
    /// Singleton in-memory cache for mission data. Stores both DB entities and BL models.
    /// Auto-reloads every 5 minutes. Decrypts mission data on load. Thread-safe via locking.
    /// </summary>
    public class MissionsCache
    {
        private static MissionsCache Instance { get; set; }
        public static IEnumerable<Mission> Missions { get; private set; } = new List<Mission>();
        public static IEnumerable<DataLayer.Models.Mission> DbMissions { get; private set; }
        private static Dictionary<int, Mission> missionDic = new();
        private MissionsCache()
        {
            Logger.Log("Creating Mission Cache");
            var timer = new Timer(state =>
            {
                ReloadCache();
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
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
        /// Reloads all missions from the database into memory.
        /// Includes mission positions and instances. Decrypts and translates to BL models.
        /// </summary>
        public static void ReloadCache()
        {
            using var db = new DataLayer.ShabzakDB();
            DbMissions = db.Missions
                .Include(m => m.MissionPositions)
                .Include(m => m.MissionInstances)
                .ThenInclude(mi => mi.Soldiers)
                .ToList()
                .Select(s => s.Decrypt())
                .ToList();

            Missions = DbMissions
                .Select(s => MissionTranslator.ToBL(s))
                .ToList();

            missionDic = Missions
                .GroupBy(m => m.Id)
                .ToDictionary(m => m.Key, m => m.Single());

            Logger.Log($"Loaded {Missions.Count()} Missions to cache");
        }

        /// <summary>
        /// Returns the singleton instance, creating it if necessary (double-checked locking).
        /// </summary>
        public static MissionsCache GetInstance()
        {
            lock (Missions)
            {
                if (Instance == null)
                {
                    lock (Missions)
                    {
                        Instance = new MissionsCache();
                    }
                }
                return Instance;
            }
        }

        /// <summary>
        /// Returns all cached missions as BL models.
        /// </summary>
        public List<Mission> GetMissions() => Missions.ToList();
        /// <summary>
        /// Returns all cached missions as raw DB entities.
        /// </summary>
        public List<DataLayer.Models.Mission> GetDBMissions() => DbMissions.ToList();

        /// <summary>
        /// Checks if a mission with the given ID exists in the cache.
        /// </summary>
        /// <param name="missionId">The mission ID to check.</param>
        /// <returns>True if the mission exists in cache.</returns>
        public bool ContainsKey(int missionId)
        {
            lock(missionDic)
            {
                return missionDic.ContainsKey(missionId);
            }
        }

        /// <summary>
        /// Retrieves a cached BL mission by ID. Optionally strips instance data.
        /// Returns null if not found.
        /// </summary>
        /// <param name="id">The mission ID.</param>
        /// <param name="includeInstances">If false, clears MissionInstances from the returned clone.</param>
        /// <returns>The cached mission or null.</returns>
        public Mission GetMissionById(int id, bool includeInstances = true)
        {
            lock (missionDic)
            {
                if (missionDic.ContainsKey(id))
                {
                    var mission = missionDic[id];
                    if(!includeInstances)
                    {
                        mission.MissionInstances = [];
                    }
                    return mission;
                }
            }
            return null;
        }

        /// <summary>
        /// Replaces an existing mission in the cache with updated data.
        /// </summary>
        /// <param name="Mission">The updated mission BL model.</param>
        public void UpdateMission(Mission Mission)
        {
            Missions = Missions
                .Where(s => s.Id != Mission.Id)
                .Union([Mission])
                .ToList();
        }

        /// <summary>
        /// Adds a new mission to the cache.
        /// </summary>
        /// <param name="Mission">The mission to add.</param>
        public void AddMission(Mission Mission)
        {
            Missions = Missions
                .Union([Mission])
                .ToList();
        }
    }

}
