using BL.Cache;
using BL.Extensions;
using BL.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Translators.Models;

namespace BL.Services
{
    public class SoldierService
    {
        public Soldier AddSoldier(Soldier soldier)
        {
            return AddSoldier(soldier.ToDB());
        }

        public Soldier AddSoldier(DataLayer.Models.Soldier soldier)
        {
            Logger.Log($"Adding Soldier:\n {JsonConvert.SerializeObject(soldier, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                db.Soldiers.Add(soldier.Encrypt());
                db.SaveChanges();
                SoldiersCache.ReloadAsync();
                return soldier.Decrypt().ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while adding soldier:\n{ex}");
                throw;
            }
        }
        public Soldier Update(Soldier soldier) => Update(soldier.ToDB());
        public Soldier Update(DataLayer.Models.Soldier soldier)
        {
            Logger.Log($"Updating Soldier:\n {JsonConvert.SerializeObject(soldier, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var sol = db.Soldiers
                    .FirstOrDefault(s => s.Id == soldier.Id)
                    ?? throw new ArgumentException("Soldier not found.");

                sol.Name = soldier.Name;
                sol.Phone = soldier.Phone;
                sol.Position = soldier.Position;
                sol.Company = soldier.Company;
                sol.PersonalNumber = soldier.PersonalNumber;
                sol.Platoon = soldier.Platoon;
                sol.Encrypt();
                db.SaveChanges();
                SoldiersCache.ReloadAsync();
                return sol.Decrypt().ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while adding soldier:\n{ex}");
                throw;
            }
        }

        public void DeleteSoldier(int id)
        {
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var soldier = db.Soldiers.FirstOrDefault(s => s.Id == id);
                if(soldier == null)
                {
                    throw new ArgumentException("Can't find soldier.");
                }
                db.Soldiers.Remove(soldier);
                SoldiersCache.ReloadAsync();
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while deleting soldier:\n{ex}");
                throw;
            }
        }
    }
}
