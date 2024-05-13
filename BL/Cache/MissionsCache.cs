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
    public class MissionsCache
    {
        private static MissionsCache Instance { get; set; }
        public static IEnumerable<Mission> Missions { get; private set; } = new List<Mission>();
        public static IEnumerable<DataLayer.Models.Mission> DbMissions { get; private set; }
        private MissionsCache()
        {
            Logger.Log("Creating Mission Cache");
            var timer = new Timer(state =>
            {
                ReloadCache();
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        public static void ReloadCache()
        {
            using var db = new DataLayer.ShabzakDB();
            DbMissions = db.Missions
                .Include(m => m.MissionInstances)
                .Include(m => m.MissionPositions)
                .ToList()
                .Select(s => s.Decrypt())
                .ToList();

            Missions = DbMissions
                .Select(s => MissionTranslator.ToBL(s))
                .ToList();

            Logger.Log($"Loaded {Missions.Count()} Missions to cache");
        }

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

        public List<Mission> GetMissions() => Missions.ToList();
        public List<DataLayer.Models.Mission> GetDBMissions() => DbMissions.ToList();

        public void UpdateMission(Mission Mission)
        {
            Missions = Missions
                .Where(s => s.Id != Mission.Id)
                .Union([Mission])
                .ToList();
        }

        public void AddMission(Mission Mission)
        {
            Missions = Missions
                .Union([Mission])
                .ToList();
        }

        public static Task ReloadAsync()
        {
            return Task.Factory.StartNew(() =>
            {
                ReloadCache();
            });
        }
    }

}
