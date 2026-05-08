using BL.Cache;
using BL.Extensions;
using BL.Logging;
using BL.Models;
using DataLayer;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Transactions;
using Translators.Models;

namespace BL.Services
{
    /// <summary>
    /// Core business logic service for mission management.
    /// Handles CRUD operations, soldier assignment/unassignment, availability computation,
    /// and replacement candidate ranking.
    /// </summary>
    public class MissionService
    {
        private readonly SoldiersCache _soldiersCache;
        public MissionService(SoldiersCache soldiersCache)
        {
            _soldiersCache = soldiersCache;
        }
        /// <summary>
        /// Retrieves all missions with their instances, assigned soldiers, and position requirements.
        /// Data is loaded from DB, translated to BL models, and decrypted.
        /// </summary>
        /// <returns>List of all missions with full nested data.</returns>
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

        /// <summary>
        /// Retrieves a raw DB mission entity by ID with optional eager loading of instances and positions.
        /// </summary>
        /// <param name="db">The DbContext to query.</param>
        /// <param name="missionId">The mission ID.</param>
        /// <param name="includeInstances">Whether to include mission instances and their soldiers.</param>
        /// <param name="includePositions">Whether to include mission position requirements.</param>
        /// <returns>The DB mission entity.</returns>
        /// <exception cref="ArgumentException">Thrown if the mission is not found.</exception>
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
        /// <summary>
        /// Creates a new mission from a BL model. Converts to DB model and delegates to the DB overload.
        /// </summary>
        public Mission AddMission(Mission mission) => AddMission(mission.ToDB());

        /// <summary>
        /// Creates a new mission in the database. Encrypts data, generates instances
        /// (for special missions: single instance from start + duration), saves, and reloads cache.
        /// </summary>
        /// <param name="mission">The DB mission entity to create.</param>
        /// <returns>The created mission as a BL model.</returns>
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

        /// <summary>
        /// Updates an existing mission from a BL model. Converts to DB model and delegates.
        /// </summary>
        public Mission UpdateMission(Mission mission) => UpdateMission(mission.ToDB());

        /// <summary>
        /// Updates an existing mission in the database. Replaces instances and positions,
        /// preserving existing soldier assignments where possible. Reloads cache after save.
        /// </summary>
        /// <param name="mission">The updated DB mission entity.</param>
        /// <returns>The updated mission as a BL model.</returns>
        public Mission UpdateMission(DataLayer.Models.Mission mission)
        {
            Logger.Log($"Updating Mission:\n {JsonConvert.SerializeObject(mission, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var dbModel = (db.Missions
                    .Include(m => m.MissionInstances)
                    .Include(m => m.MissionPositions)
                    .FirstOrDefault(m => m.Id == mission.Id)
                    ?.Decrypt()) ?? throw new ArgumentNullException("Mission not found");
                dbModel.Name = mission.Name;
                dbModel.Description = mission.Description;
                dbModel.Duration = mission.Duration;
                dbModel.SoldiersRequired = mission.SoldiersRequired;
                dbModel.CommandersRequired = mission.CommandersRequired;
                dbModel.FromTime = mission.FromTime;
                dbModel.ToTime = mission.ToTime;
                dbModel.IsSpecial = mission.IsSpecial;
                dbModel.ActualHours = mission.ActualHours;
                dbModel.RequiredRestAfter = mission.RequiredRestAfter;
                db.MissionInstances.RemoveRange(dbModel.MissionInstances);

                dbModel.MissionInstances = mission.MissionInstances
                    .Select(mi => new DataLayer.Models.MissionInstance
                    {
                        FromTime = mi.FromTime,
                        ToTime = mi.ToTime,
                        MissionId = mi.MissionId,
                        IsFilled = mi.IsFilled,
                    })
                    .ToList();
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

        /// <summary>
        /// Deletes a mission and all related data (instances, positions, assignments) via cascade delete.
        /// Reloads the missions cache after deletion.
        /// </summary>
        /// <param name="missionId">The ID of the mission to delete.</param>
        /// <returns>The deleted mission's ID.</returns>
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

        /// <summary>
        /// Assigns soldiers to a specific mission instance. Adds SoldierMission records
        /// and updates the instance's IsFilled flag based on total positions required.
        /// </summary>
        /// <param name="missionId">The mission ID.</param>
        /// <param name="missionInstanceId">The instance to assign to.</param>
        /// <param name="soldiers">The soldier-position assignments to create.</param>
        /// <returns>The updated mission with new assignments.</returns>
        public Mission AssignSoldiersToMission(int missionId, int missionInstanceId, IEnumerable<DataLayer.Models.SoldierMission> soldiers)
        {
            var db = new DataLayer.ShabzakDB();
            var mission = GetDBMissionById(db, missionId);
            var instance = db.MissionInstances
                .Include(m => m.Soldiers)
                .FirstOrDefault(i => i.Id == missionInstanceId) ?? throw new ArgumentException("Mission Instance not found.");

            instance.Soldiers ??= new();
            instance.Soldiers.AddRange(soldiers);
            instance.IsInstanceFilled();

            db.SaveChanges();
            MissionsCache.ReloadAsync();
            return GetDBMissionById(db, missionId).ToBL();
        }

        /// <summary>
        /// Removes a soldier's assignment from a mission instance.
        /// Updates the instance's IsFilled flag after removal.
        /// </summary>
        /// <param name="missionId">The mission ID.</param>
        /// <param name="missionInstanceId">The instance to remove from.</param>
        /// <param name="soldierId">The soldier to unassign.</param>
        /// <returns>The updated mission.</returns>
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

        /// <summary>
        /// Computes soldier availability for a specific mission instance.
        /// For each soldier: calculates rest-time-before/after, checks for overlapping assignments/vacations,
        /// and determines if the soldier is already assigned. Results sorted by best availability.
        /// </summary>
        /// <param name="missionInstanceId">The mission instance to check availability for.</param>
        /// <param name="soldiersPool">Optional: restrict to specific soldier IDs. Null = all active soldiers.</param>
        /// <returns>List of soldiers with availability metrics, sorted by rest time descending.</returns>
        public List<GetAvailableSoldiersModel> GetAvailableSoldiers(int missionInstanceId, List<int>? soldiersPool = null)
        {
            var ret = new List<GetAvailableSoldiersModel>();
            using var db = new DataLayer.ShabzakDB();

            var missionInstance = db.MissionInstances
                .Include(mi => mi.Mission)
                .First(mi => mi.Id == missionInstanceId);
            var startTime = missionInstance.FromTime;
            var endTime = missionInstance.ToTime;
            var currentMissionRest = missionInstance.Mission?.RequiredRestAfter;

            var pool = soldiersPool != null ? soldiersPool : db.Soldiers.Select(s => s.Id).ToList();

            foreach (var soldierId in pool)
            {
                var model = new GetAvailableSoldiersModel
                {
                    Soldier = _soldiersCache.GetSoldierById(soldierId),
                    RequiredRestAfterThreshold = currentMissionRest,
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
                    .Include(sm => sm.MissionInstance.Mission)
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
                            var diffHours = (int)diff.TotalHours;
                            if (model.RestTimeBefore == null || diffHours < model.RestTimeBefore)
                            {
                                model.RestTimeBefore = diffHours;
                                model.RequiredRestBeforeThreshold = sm.MissionInstance.Mission?.RequiredRestAfter;
                            }
                        }
                        if(sm.MissionInstance.FromTime > endTime)
                        {
                            var diff = sm.MissionInstance.FromTime - endTime;
                            var diffHours = (int)diff.TotalHours;
                            if (model.RestTimeAfter == null || diffHours < model.RestTimeAfter)
                            {
                                model.RestTimeAfter = diffHours;
                            }
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

        /// <summary>
        /// Retrieves all time-slot instances for a specific mission, including assigned soldiers.
        /// </summary>
        /// <param name="missionId">The mission ID.</param>
        /// <returns>List of mission instances with soldier assignments.</returns>
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

        /// <summary>
        /// Syncs soldier assignments for a mission instance. Compares provided list with existing
        /// assignments: adds new ones, removes ones no longer present. Updates IsFilled flag.
        /// All soldiers must target the same instance.
        /// </summary>
        /// <param name="soldiers">The desired soldier-mission assignments for one instance.</param>
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

        /// <summary>
        /// Retrieves mission instances within a date range. When fullDay is true,
        /// the range is expanded to cover entire days (midnight to midnight).
        /// Can optionally filter to only unfilled instances.
        /// </summary>
        /// <param name="from">Start of the date range.</param>
        /// <param name="to">End of the date range.</param>
        /// <param name="fullDay">If true, expand range to full days.</param>
        /// <param name="unassignedOnly">If true, only return instances where IsFilled is false.</param>
        /// <returns>Filtered list of mission instances.</returns>
        public List<MissionInstance> GetMissionInstancesInRange(DateTime from, DateTime to, bool fullDay = true, bool unassignedOnly = true)
        {
            using var db = new DataLayer.ShabzakDB();
            if(fullDay)
            {
                from = from.Date;
                to = to.Date.AddDays(1).AddTicks(-1);
            }
            var query = db.MissionInstances
                .Where(mi => mi.FromTime >= from && mi.ToTime <= to);
            if(unassignedOnly)
            {
                query = query
                    .Where(mi => mi.Soldiers.Count == 0);
            }
            var instances = query
                .ToList()
                .Select(mi => mi.ToBL())
                .ToList();

            return instances;
        }

        private bool OverlappingTimes(DateTime startTime1, DateTime endTime1, DateTime startTime2, DateTime endTime2)
        {
            return startTime1.IsBetweenDates(startTime2, endTime2) ||
                startTime2.IsBetweenDates(startTime1, endTime1) ||
                endTime1.IsBetweenDates(startTime2, endTime2) ||
                endTime2.IsBetweenDates(startTime1, endTime1);
        }

        /// <summary>
        /// Computes ranked replacement candidates for a soldier in a mission instance.
        /// Scoring: position match (exact/similar), rest-time availability (staircase),
        /// and fairness (assignment count vs average). Includes overlap detection for swap candidates.
        /// </summary>
        /// <param name="missionInstanceId">The instance where the replacement is needed.</param>
        /// <param name="excludeSoldierId">The soldier being replaced (excluded from candidates).</param>
        /// <returns>Ranked list of replacement candidates with scores, breakdowns, and overlap info.</returns>
        public List<ReplacementCandidateModel> GetReplacementCandidates(int missionInstanceId, int excludeSoldierId)
        {
            var ret = new List<ReplacementCandidateModel>();
            using var db = new DataLayer.ShabzakDB();

            var missionInstance = db.MissionInstances
                .Include(mi => mi.Mission)
                .Include(mi => mi.Mission.MissionPositions)
                .First(mi => mi.Id == missionInstanceId);
            var startTime = missionInstance.FromTime;
            var endTime = missionInstance.ToTime;
            var currentMissionRest = missionInstance.Mission?.RequiredRestAfter;

            var pool = db.Soldiers
                .Where(s => s.Id != excludeSoldierId)
                .Select(s => s.Id)
                .ToList();

            var avgAssignmentCount = 0.0;
            var totalSoldiers = pool.Count;
            if (totalSoldiers > 0)
            {
                var totalAssignments = db.SoldierMission.Count();
                avgAssignmentCount = (double)totalAssignments / totalSoldiers;
            }

            foreach (var soldierId in pool)
            {
                var model = new ReplacementCandidateModel
                {
                    Soldier = _soldiersCache.GetSoldierById(soldierId),
                };
                model.Soldier.Missions = [];

                var assignedForInstance = db.SoldierMission
                    .Count(sm => sm.SoldierId == soldierId && sm.MissionInstanceId == missionInstanceId) > 0;

                if (assignedForInstance)
                {
                    model.IsAssignedToThisInstance = true;
                    model.RestTimeAfter = 0;
                    model.RestTimeBefore = 0;
                    model.Score = 0;
                    ret.Add(model);
                    continue;
                }

                var soldierMissions = db.SoldierMission
                    .Where(mi => mi.SoldierId == soldierId)
                    .Include(sm => sm.MissionInstance)
                    .Include(sm => sm.MissionInstance.Mission)
                    .Include(sm => sm.Soldier)
                    .OrderBy(sm => sm.MissionInstance.FromTime)
                    .ToList();

                int? overlappingInstanceId = null;
                string? overlappingMissionName = null;

                if (soldierMissions.Count > 0)
                {
                    foreach (var sm in soldierMissions)
                    {
                        if (OverlappingTimes(startTime, endTime, sm.MissionInstance.FromTime, sm.MissionInstance.ToTime))
                        {
                            model.RestTimeBefore = 0;
                            model.RestTimeAfter = 0;
                            overlappingInstanceId = sm.MissionInstance.Id;
                            overlappingMissionName = sm.MissionInstance.Mission.Name;
                            break;
                        }
                        if (sm.MissionInstance.ToTime <= startTime)
                        {
                            var diff = startTime - sm.MissionInstance.ToTime;
                            var diffHours = (int)diff.TotalHours;
                            if (model.RestTimeBefore == null || diffHours < model.RestTimeBefore)
                            {
                                model.RestTimeBefore = diffHours;
                            }
                        }
                        if (sm.MissionInstance.FromTime > endTime)
                        {
                            var diff = sm.MissionInstance.FromTime - endTime;
                            var diffHours = (int)diff.TotalHours;
                            if (model.RestTimeAfter == null || diffHours < model.RestTimeAfter)
                            {
                                model.RestTimeAfter = diffHours;
                            }
                        }
                    }
                }

                model.HasOverlap = overlappingInstanceId.HasValue;
                model.OverlappingMissionInstanceId = overlappingInstanceId;
                model.OverlappingMissionName = overlappingMissionName;

                var positions = missionInstance.Mission?.MissionPositions?.ToList() ?? [];

                double positionScore = ComputePositionScore(model.Soldier, positions);
                double restScore = ComputeRestScore(model.RestTimeBefore, model.RestTimeAfter, currentMissionRest);
                double fairnessScore = ComputeFairnessScore(soldierId, avgAssignmentCount, db);

                model.Score = positionScore * restScore * fairnessScore;

                ret.Add(model);
            }

            foreach (var r in ret)
            {
                r.Soldier.Positions = r.Soldier.Positions
                    .OrderByDescending(p => p)
                    .ToList();
            }

            return ret
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => r.RestTimeBefore ?? int.MaxValue)
                .ThenByDescending(r => r.RestTimeAfter ?? int.MaxValue)
                .ToList();
        }

        private double ComputePositionScore(Soldier soldier, List<DataLayer.Models.MissionPositions> missionPositions)
        {
            if (missionPositions == null || missionPositions.Count == 0)
                return 1.0;

            var soldierPositions = soldier.Positions;
            if (soldierPositions == null || soldierPositions.Count == 0)
                return 0.1;

            var missionPositionEnums = missionPositions.Select(mp => (Translators.Models.Position)(int)mp.Position).ToList();

            foreach (var soldierPos in soldierPositions)
            {
                if (missionPositionEnums.Contains(soldierPos))
                    return 1.0;
            }

            var soldierHasCommanding = soldierPositions.Any(p => p.IsCommandingPosition());
            var missionRequiresCommanding = missionPositionEnums.Any(p => p.IsCommandingPosition());

            if (soldierHasCommanding && missionRequiresCommanding)
                return 0.7;

            if (soldierHasCommanding || missionRequiresCommanding)
                return 0.5;

            return 0.3;
        }

        private double ComputeRestScore(int? restTimeBefore, int? restTimeAfter, int? requiredRestThreshold)
        {
            var t = requiredRestThreshold ?? 8;
            if (t == 0) return 1.0;

            var beforeScore = ComputeRestSideScore(restTimeBefore, t);
            var afterScore = ComputeRestSideScore(restTimeAfter, t);

            if (beforeScore == 0.0 || afterScore == 0.0) return 0.0;
            return Math.Min(beforeScore, afterScore);
        }

        private static double ComputeRestSideScore(int? restTime, int requiredRestThreshold)
        {
            var rest = restTime ?? int.MaxValue;
            if (rest == 0) return 0.0;
            if (rest == int.MaxValue) return 1.0;
            if (rest >= (int)(requiredRestThreshold * 1.5)) return 0.9;
            if (rest >= (int)(requiredRestThreshold * 1.25)) return 0.8;
            if (rest >= requiredRestThreshold) return 0.7;
            return 0.1;
        }

        private double ComputeFairnessScore(int soldierId, double avgAssignmentCount, DataLayer.ShabzakDB db)
        {
            if (avgAssignmentCount == 0) return 1.0;

            var soldierCount = db.SoldierMission.Count(sm => sm.SoldierId == soldierId);
            var diff = soldierCount - avgAssignmentCount;

            if (diff <= 0) return 1.0;
            if (diff <= avgAssignmentCount * 0.1) return 0.9;
            if (diff <= avgAssignmentCount * 0.25) return 0.7;
            if (diff <= avgAssignmentCount * 0.5) return 0.5;
            return 0.2;
        }

        /// <summary>
        /// Replaces a soldier in a mission instance. In swap mode, the old and new soldiers
        /// exchange their assignments across two instances. In non-swap mode, the old soldier
        /// is simply removed and the new one is assigned to the same position.
        /// </summary>
        /// <param name="missionInstanceId">The instance where the replacement happens.</param>
        /// <param name="oldSoldierId">The soldier being replaced.</param>
        /// <param name="newSoldierId">The replacement soldier.</param>
        /// <param name="swap">If true, performs a two-way swap between instances.</param>
        /// <param name="swapMissionInstanceId">The second instance for the swap (required when swap=true).</param>
        /// <returns>All missions with updated assignments.</returns>
        public List<Mission> ReplaceSoldierInMissionInstance(int missionInstanceId, int oldSoldierId, int newSoldierId, bool swap, int? swapMissionInstanceId)
        {
            using var db = new DataLayer.ShabzakDB();

            var oldAssignment = db.SoldierMission
                .FirstOrDefault(sm => sm.SoldierId == oldSoldierId && sm.MissionInstanceId == missionInstanceId)
                ?? throw new ArgumentException("Old soldier assignment not found");

            var oldMissionPositionId = oldAssignment.MissionPositionId;

            db.SoldierMission.Remove(oldAssignment);

            if (swap && swapMissionInstanceId.HasValue)
            {
                var newAssignment = db.SoldierMission
                    .FirstOrDefault(sm => sm.SoldierId == newSoldierId && sm.MissionInstanceId == swapMissionInstanceId.Value)
                    ?? throw new ArgumentException("New soldier assignment in swap instance not found");

                var swapMissionPositionId = newAssignment.MissionPositionId;

                db.SoldierMission.Remove(newAssignment);

                db.SoldierMission.Add(new DataLayer.Models.SoldierMission
                {
                    Id = 0,
                    SoldierId = oldSoldierId,
                    MissionInstanceId = swapMissionInstanceId.Value,
                    MissionPositionId = swapMissionPositionId
                });

                var swapInstance = db.MissionInstances
                    .Include(mi => mi.Mission)
                    .Include(mi => mi.Mission.MissionPositions)
                    .FirstOrDefault(mi => mi.Id == swapMissionInstanceId.Value);
                if (swapInstance != null)
                {
                    swapInstance.IsInstanceFilled();
                }
            }

            db.SoldierMission.Add(new DataLayer.Models.SoldierMission
            {
                Id = 0,
                SoldierId = newSoldierId,
                MissionInstanceId = missionInstanceId,
                MissionPositionId = oldMissionPositionId
            });

            var originalInstance = db.MissionInstances
                .Include(mi => mi.Mission)
                .Include(mi => mi.Mission.MissionPositions)
                .FirstOrDefault(mi => mi.Id == missionInstanceId);
            if (originalInstance != null)
            {
                originalInstance.IsInstanceFilled();
            }

            db.SaveChanges();
            MissionsCache.ReloadAsync();

            return GetMissions();
        }
    }
}
