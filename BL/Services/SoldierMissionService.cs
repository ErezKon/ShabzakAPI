using BL.Extensions;
using BL.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace BL.Services
{
    public class SoldierMissionService
    {
        public List<SoldierMission> GetSoldierMissions()
        {
            using var db = new DataLayer.ShabzakDB();
            var ret = db.SoldierMission
                .ToList()
                .Select(m => m.ToBL())
                .ToList();
            return ret;
        }
        public SoldierMission AddSoldierMission(SoldierMission soldierMission) => AddSoldierMission(soldierMission.ToDB());

        public SoldierMission AddSoldierMission(DataLayer.Models.SoldierMission soldierMission)
        {
            Logger.Log($"Adding SoldierMission:\n {JsonConvert.SerializeObject(soldierMission, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                db.SoldierMission.Add(soldierMission);
                db.SaveChanges();
                return soldierMission.ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while adding SoldierMission:\n{ex}");
                throw;
            }
        }

        public SoldierMission UpdateSoldierMission(SoldierMission soldierMission) => UpdateSoldierMission(soldierMission.ToDB());

        public SoldierMission UpdateSoldierMission(DataLayer.Models.SoldierMission soldierMission)
        {
            Logger.Log($"Updating SoldierMission:\n {JsonConvert.SerializeObject(soldierMission, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var dbModel = db.SoldierMission
                    .FirstOrDefault(m => m.Id == soldierMission.Id) ?? throw new ArgumentException("SoldierMission not found.");
                dbModel.MissionPositionId = soldierMission.MissionPositionId;
                dbModel.MissionInstanceId = soldierMission.MissionInstanceId;
                dbModel.SoldierId = soldierMission.SoldierId;
                db.SaveChanges();
                return dbModel.ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while updating SoldierMission:\n{ex}");
                throw;
            }
        }

        public int DeleteSoldierMission(int soldierMissionId)
        {
            Logger.Log($"Deleting SoldierMission {soldierMissionId}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var soldierMission = db.SoldierMission
                    .FirstOrDefault(m => m.Id == soldierMissionId) ?? throw new ArgumentException("SoldierMission not found.");
                Logger.Log($"Found SoldierMission:\n {JsonConvert.SerializeObject(soldierMission, Formatting.Indented)}");
                db.SoldierMission.Remove(soldierMission);
                db.SaveChanges();
                return soldierMissionId;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while deleting SoldierMission:\n{ex}");
                throw;
            }
        }
    }
}
