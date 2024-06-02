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

        public static void ReloadCache()
        {
            using var db = new DataLayer.ShabzakDB();
            dbSoldiersDic = db.Soldiers
                .Include(s => s.Missions)
                .Include(s => s.Vacations)
                .ToList()
                .Select(s => s.Decrypt())
                .GroupBy(s => s.Id)
                .ToDictionary(k => k.Key, v => v.Single());

            soldiersDic = DbSoldiers
                .Select(s => SoldierTranslator.ToBL(s))
                .GroupBy(s => s.Id)
                .ToDictionary(k => k.Key, v => v.Single());

            Logger.Log($"Loaded {soldiersDic.Count()} soldiers to cache");
        }

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

        public List<Soldier> GetSoldiers() => Soldiers.ToList();
        public List<DataLayer.Models.Soldier> GetDBSoldiers() => DbSoldiers.ToList();

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
        
        public void AddSoldier(Soldier soldier)
        {
            lock (soldiersDic)
            {
                soldiersDic.Add(soldier.Id, soldier);
            }
        }

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

        public static Task ReloadAsync()
        {
            return Task.Factory.StartNew(() =>
            {
                ReloadCache();
            });
        }
    }
}
