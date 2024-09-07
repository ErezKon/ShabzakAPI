using BL.Cache;
using BL.Extensions;
using BL.Logging;
using BL.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Transactions;
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


            var ret = temp.Select(m => m.Decrypt().ToBL(includeSoldier: true))
                .ToList();
            return ret;
        }

        public DataLayer.Models.Mission GetDBMissionById(DataLayer.ShabzakDB db, int missionId, bool includeInstances = true, bool includePositions = true)
        {
            var query = db.Missions.AsQueryable();
            if(includeInstances)
            {
                query = query.Include(m => m.MissionInstances);
            }
            if (includePositions)
            {
                query = query.Include(m => m.MissionPositions);
            }
            var mission = query.FirstOrDefault(m => m.Id == missionId) ?? throw new ArgumentException("Mission not found.");
            return mission;
        }
        public Mission AddMission(Mission mission) => AddMission(mission.ToDB());

        public Mission AddMission(DataLayer.Models.Mission mission)
        {
            Logger.Log($"Adding Mission:\n {JsonConvert.SerializeObject(mission, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var instances = mission.MissionInstances.ToList();
                mission.MissionInstances = [];
                if(mission.IsSpecial)
                {
                    var startTime = Convert.ToDateTime(instances.First().FromTime);
                    var endTime = startTime.AddHours(mission.Duration);
                    var instance = new DataLayer.Models.MissionInstance
                    {
                        Id = 0,
                        FromTime = startTime,
                        ToTime = endTime,
                        MissionId = mission.Id,
                    };
                    instances = [instance];
                }
                db.Missions.Add(mission.Encrypt());
                db.SaveChanges();
                foreach(var instance in instances)
                {
                    instance.MissionId = mission.Id;
                    db.MissionInstances.Add(instance);
                }
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
                Logger.Log($"Found Mission:\n {JsonConvert.SerializeObject(mission.Clone().HideLists().Decrypt(), Formatting.Indented)}");
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
                var assignedForInstance = db.SoldierMission
                    .Count(sm => sm.SoldierId == soldierId && sm.MissionInstanceId == missionInstanceId) > 0;
                if(assignedForInstance)
                {
                    model.IsAssignedForQueriedInstance = true;
                    model.RestTimeAfter = 0;
                    model.RestTimeBefore = 0;
                    ret.Add(model);
                    continue;
                }
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
                        if(OverlappingTimes(startTime, endTime, sm.MissionInstance.FromTime, sm.MissionInstance.ToTime))
                        {
                            model.RestTimeBefore = 0;
                            model.RestTimeAfter = 0;
                            break;
                        }
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

            foreach (var r in ret)
            {
                r.Soldier.Positions = r.Soldier.Positions
                    .OrderByDescending(p => p)
                    .ToList();
            }

            return ret
                .OrderByDescending(r => r.RestTimeBefore ?? int.MaxValue)
                .ThenByDescending(r => r.RestTimeAfter ?? int.MaxValue)
                .ToList();
        }

        public List<MissionInstance> GetMissionInstances(int missionId)
        {
            using var db = new DataLayer.ShabzakDB();
            var instances = db.MissionInstances
                .Where(mi => mi.MissionId == missionId)
                .ToList()
                .Select(mi => mi.ToBL())
                .ToList();

            return instances;
        }

        public void AssignSoldiersToMissionInstance(List<SoldierMission> soldiers)
        {
            if (soldiers == null || soldiers.Count == 0)
            {
                throw new ArgumentNullException("No Soldiers provided.");
            }
            var instances = soldiers.Select(s => s.MissionInstance.Id).ToList().Distinct();
            if (instances.Count() != 1)
            {
                throw new ArgumentException("Different mission instances provided.");
            }
            var missionInstance = instances.First();
            var data = soldiers
                .Select(s => new DataLayer.Models.SoldierMission
                {
                    Id = 0,
                    SoldierId = s.Soldier.Id,
                    MissionInstanceId = s.MissionInstance.Id,
                    MissionPositionId = s.MissionPosition.Id,
                })
                .ToList();
            using var db = new DataLayer.ShabzakDB();
            var existingAssignments = db.SoldierMission
                .Where(mi => mi.MissionInstanceId == missionInstance)
                .ToList();

            foreach (var existingAssignment in existingAssignments)
            {
                var assignments = data
                    .Where(mi => mi.MissionInstanceId == existingAssignment.MissionInstanceId && mi.SoldierId == existingAssignment.SoldierId)
                    .Count();
                if (assignments == 0)
                {
                    db.SoldierMission.Remove(existingAssignment);
                }
            }
            foreach(var assignment in data)
            {
                var existingAssignment = existingAssignments
                    .Where(a => a.MissionInstanceId == assignment.MissionInstanceId && a.SoldierId == assignment.SoldierId)
                    .Count();
                if(existingAssignment == 0)
                {
                    db.SoldierMission.Add(assignment);
                }
            }

            db.SaveChanges();
        }

        private bool OverlappingTimes(DateTime startTime1, DateTime endTime1, DateTime startTime2, DateTime endTime2)
        {
            return startTime1.IsBetweenDates(startTime2, endTime2) ||
                startTime2.IsBetweenDates(startTime1, endTime1) ||
                endTime1.IsBetweenDates(startTime2, endTime2) ||
                endTime2.IsBetweenDates(startTime1, endTime1);
        }
    }
}
