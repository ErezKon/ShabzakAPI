using BL.Cache;
using BL.Extensions;
using BL.Logging;
using Microsoft.EntityFrameworkCore;
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
            var temp = db.Missions
                .Include(m => m.MissionInstances)
                .Include("MissionInstances.Soldiers")
                .Include("MissionInstances.Soldiers.Soldier")
                .Include(m => m.MissionPositions)
                .ToList();


            var ret = temp.Select(m => m.Decrypt().ToBL())
                .ToList();

            foreach (var mission in ret)
            {
                foreach(var instance in mission.MissionInstances)
                {
                    foreach(var soldierMission in instance.SoldierMissions)
                    {
                        try
                        {
                            soldierMission.Soldier = soldierMission.Soldier.Decrypt();
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("Can't decrypt soldier.");
                        }
                    }
                }
            }
            return ret;
        }

        public DataLayer.Models.Mission GetDBMissionById(int missionId)
        {
            using var db = new DataLayer.ShabzakDB();
            var mission = db.Missions
                .Include(m => m.MissionInstances)
                .Include(m => m.MissionPositions)
                .FirstOrDefault(m => m.Id == missionId) ?? throw new ArgumentException("Mission not found.");
            return mission;
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
                MissionsCache.ReloadAsync();
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
                var dbModel = GetDBMissionById(mission.Id);
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
                MissionsCache.ReloadAsync();
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
                var mission = GetDBMissionById(missionId);
                Logger.Log($"Found Mission:\n {JsonConvert.SerializeObject(mission.Decrypt(), Formatting.Indented)}");
                db.Missions.Remove(mission);
                db.SaveChanges();
                MissionsCache.ReloadAsync();
                return missionId;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while deleting mission:\n{ex}");
                throw;
            }
        }

        public Mission AssignSoldiersToMission(int missionId, int missionInstanceId, IEnumerable<DataLayer.Models.SoldierMission> soldiers)
        {
            var db = new DataLayer.ShabzakDB();
            var mission = GetDBMissionById(missionId);
            var instance = db.MissionInstances
                .Include(m => m.Soldiers)
                .FirstOrDefault(i => i.Id == missionInstanceId) ?? throw new ArgumentException("Mission Instance not found.");
            
            //foreach (var soldierMission in soldiers)
            //{
            //    var soldier = db.Soldiers
            //    .FirstOrDefault(s => s.Id == soldierMission.SoldierId) ?? throw new ArgumentException("Soldier not found");
            //    var missionPosition = db.MissionPositions
            //        .FirstOrDefault(pos => pos.Id == soldierMission.MissionPositionId) ?? throw new ArgumentException("Mission Position not found");

            //    soldierMission.MissionPosition = missionPosition;
            //    soldierMission.Soldier = soldier;
            //    soldierMission.MissionInstance = instance;

            //    instance.Soldiers.Add(soldierMission);
            //}

            instance.Soldiers ??= new();
            instance.Soldiers.AddRange(soldiers);

            db.SaveChanges();
            MissionsCache.ReloadAsync();
            return GetDBMissionById(missionId).ToBL();
        }

        public Mission UnassignSoldiersToMission(int missionId, int missionInstanceId, int soldierId)
        {
            var db = new DataLayer.ShabzakDB();
            var mission = GetDBMissionById(missionId);
            var instance = mission?.MissionInstances
                ?.FirstOrDefault(i => i.Id == missionInstanceId) ?? throw new ArgumentException("Mission Instance not found.");
            var soldierMission = instance.Soldiers
                ?.FirstOrDefault(s => s.SoldierId == soldierId) ?? throw new ArgumentException("Soldier Mission not found.");
            db.SoldierMission.Remove(soldierMission);
            db.SaveChanges();
            MissionsCache.ReloadAsync();
            return GetDBMissionById(missionId).ToBL();
        }
    }
}
