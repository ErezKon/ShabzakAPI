using BL.Extensions;
using BL.Logging;
using Newtonsoft.Json;
using System.Reflection;
using Translators.Models;

namespace BL.Services
{
    public class MissionService
    {
        public List<Mission> GetMissions()
        {
            using var db = new DataLayer.ShabzakDB();
            var ret = db.Missions
                .ToList()
                .Select(m => m.Decrypt().ToBL())
                .ToList();
            return ret;
        }
        public Mission AddMission(Mission mission) => AddMission(mission.ToDB());

        public Mission AddMission(DataLayer.Models.Mission mission)
        {
            Logger.Log($"Adding Mission:\n {JsonConvert.SerializeObject(mission, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                db.Missions.Add(mission.Encrypt());
                db.SaveChanges();
                return mission.Decrypt().ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while adding mission:\n{ex}");
                throw;
            }
        }

        public Mission UpdateMission(Mission mission) => UpdateMission(mission.ToDB());

        public Mission UpdateMission(DataLayer.Models.Mission mission)
        {
            Logger.Log($"Updating Mission:\n {JsonConvert.SerializeObject(mission, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var dbModel = db.Missions
                    .FirstOrDefault(m => m.Id == mission.Id) ?? throw new ArgumentException("Mission not found.");
                dbModel.Name = mission.Name;
                dbModel.Description = mission.Description;
                dbModel.Duration = mission.Duration;
                dbModel.SoldiersRequired = mission.SoldiersRequired;
                dbModel.CommandersRequired = mission.CommandersRequired;
                dbModel.FromTime = mission.FromTime;
                dbModel.ToTime = mission.ToTime;
                dbModel.IsSpecial = mission.IsSpecial;
                var ret = dbModel.ToBL();
                dbModel.Encrypt();
                db.SaveChanges();
                return ret;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while updating mission:\n{ex}");
                throw;
            }
        }

        public int DeleteMission(int missionId)
        {
            Logger.Log($"Deleting Mission {missionId}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var mission = db.Missions
                    .FirstOrDefault(m => m.Id == missionId) ?? throw new ArgumentException("Mission not found.");
                Logger.Log($"Found Mission:\n {JsonConvert.SerializeObject(mission.Decrypt(), Formatting.Indented)}");
                db.Missions.Remove(mission);
                db.SaveChanges();
                return missionId;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while deleting mission:\n{ex}");
                throw;
            }
        }
    }
}
