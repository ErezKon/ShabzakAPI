using BL.Extensions;
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
        public static IEnumerable<Soldier> Soldiers { get; private set; }
        public static IEnumerable<DataLayer.Models.Soldier> DbSoldiers { get; private set; }
        private SoldiersCache() 
        {
            var timer = new Timer(state =>
            {
                ReloadCache();
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        public static void ReloadCache()
        {
            using var db = new DataLayer.ShabzakDB();
            DbSoldiers = db.Soldiers
                .ToList()
                .Select(s => s.Decrypt());

            Soldiers = DbSoldiers
                .Select(s => SoldierTanslator.ToBL(s));
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


    }
}
