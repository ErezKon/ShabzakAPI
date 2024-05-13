﻿using BL.Extensions;
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
        public static IEnumerable<Soldier> Soldiers { get; private set; } = new List<Soldier>();
        public static IEnumerable<DataLayer.Models.Soldier> DbSoldiers { get; private set; }
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
            DbSoldiers = db.Soldiers
                .Include(s => s.Missions)
                .Include(s => s.Vacations)
                .ToList()
                .Select(s => s.Decrypt())
                .ToList();

            Soldiers = DbSoldiers
                .Select(s => SoldierTranslator.ToBL(s))
                .ToList();

            Logger.Log($"Loaded {Soldiers.Count()} soldiers to cache");
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
            Soldiers = Soldiers
                .Where(s => s.Id != soldier.Id)
                .Union([soldier])
                .ToList();
        }
        
        public void AddSoldier(Soldier soldier)
        {
            Soldiers = Soldiers
                .Union([soldier])
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
