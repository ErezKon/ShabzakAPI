using BL.Cache;
using BL.Extensions;
using BL.Logging;
using BL.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Reflection;
using Translators.Models;

namespace BL.Services
{
    public class MissionService
    {
        private readonly SoldiersCache _soldiersCache;
        public MissionService(SoldiersCache soldiersCache)
        {
            _soldiersCache = soldiersCache;
        }
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

        public DataLayer.Models.Mission GetDBMissionById(DataLayer.ShabzakDB db, int missionId)
        {
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
                var dbModel = GetDBMissionById(db, mission.Id);
                dbModel.Name = mission.Name;
                dbModel.Description = mission.Description;
                dbModel.Duration = mission.Duration;
                dbModel.SoldiersRequired = mission.SoldiersRequired;
                dbModel.CommandersRequired = mission.CommandersRequired;
                dbModel.FromTime = mission.FromTime;
                dbModel.ToTime = mission.ToTime;
                dbModel.IsSpecial = mission.IsSpecial;
                foreach (var mi in mission.MissionInstances)
                {
                    var miDbModel = db.MissionInstances
                        .FirstOrDefault(m => m.Id == mi.Id);
                    if (miDbModel != null)
                    {
                        miDbModel.FromTime = mi.FromTime;
                        miDbModel.ToTime = mi.ToTime;
                    }
                }
                dbModel.Encrypt();
                db.SaveChanges();
                MissionsCache.ReloadAsync();
                return GetDBMissionById(db, mission.Id)
                    .Decrypt()
                    .ToBL(false, false);
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
                var mission = GetDBMissionById(db, missionId);
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
            var mission = GetDBMissionById(db, missionId);
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
            return GetDBMissionById(db, missionId).ToBL();
        }

        public Mission UnassignSoldiersToMission(int missionId, int missionInstanceId, int soldierId)
        {
            var db = new DataLayer.ShabzakDB();
            var mission = GetDBMissionById(db, missionId);
            var instance = mission?.MissionInstances
                ?.FirstOrDefault(i => i.Id == missionInstanceId) ?? throw new ArgumentException("Mission Instance not found.");
            var soldierMission = instance.Soldiers
                ?.FirstOrDefault(s => s.SoldierId == soldierId) ?? throw new ArgumentException("Soldier Mission not found.");
            db.SoldierMission.Remove(soldierMission);
            db.SaveChanges();
            MissionsCache.ReloadAsync();
            return GetDBMissionById(db, missionId).ToBL();
        }

        public List<GetAvailableSoldiersModel> GetAvailableSoldiers(int missionInstanceId, List<int>? soldiersPool = null)
        {
            var ret = new List<GetAvailableSoldiersModel>();
            using var db = new DataLayer.ShabzakDB();

            var missionInstance = db.MissionInstances
                .First(mi => mi.Id == missionInstanceId);
            var startTime = missionInstance.FromTime;
            var endTime = missionInstance.ToTime;

            var pool = soldiersPool != null ? soldiersPool : db.Soldiers.Select(s => s.Id).ToList();

            foreach (var soldierId in pool)
            {
                var model = new GetAvailableSoldiersModel
                {
                    Soldier = _soldiersCache.GetSoldierById(soldierId),
                };
                model.Soldier.Missions = [];
                var soldierMissions = db.SoldierMission
                    .Where(mi => mi.SoldierId == soldierId)
                    .Include(sm => sm.MissionInstance)
                    .Include(sm => sm.Soldier)
                    .OrderBy(sm => sm.MissionInstance.FromTime)
                    .ToList();
                if(soldierMissions.Count > 0)
                {
                    foreach (var sm in soldierMissions)
                    {
                        if(sm.MissionInstance.ToTime <= startTime)
                        {
                            var diff = startTime - sm.MissionInstance.ToTime;
                            model.RestTimeBefore = diff.Hours;
                        }
                        if(sm.MissionInstance.FromTime > endTime)
                        {
                            var diff = sm.MissionInstance.FromTime - endTime;
                            model.RestTimeAfter = diff.Hours;
                        }
                    }
                }
                ret.Add(model);
            }

            return ret
                .OrderByDescending(r => r.RestTimeBefore)
                .ToList();
        }
    }
}
