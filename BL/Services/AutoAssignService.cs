using BL.Cache;
using DataLayer.Models;
using BL.Models;
using BL.Logging;
using BL.Extensions;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using DataLayer;
using System.Linq;
using System.Reflection;

namespace BL.Services
{
    public class AutoAssignService
    {
        private readonly SoldiersCache _soldiersCache;
        private readonly MissionsCache _missionsCache;
        private readonly MissionService _missionService;
        private readonly PositionHelper posHelper;

        private Dictionary<int, List<Position>> soldierPositionCache = new Dictionary<int, List<Position>>();
        private List<Mission> missionAssignments;
        private List<Mission> selectedMissionAssignments;
        private Dictionary<int, Dictionary<int, List<SoldierMission>>> soldierMissionsByMissionIdBySoldierId;
        private Dictionary<int, int> missionCountBySoldier;

        private Dictionary<int, MissionInstance> missionInstanceDic;

        private readonly Random rand = new Random();
        public AutoAssignService(SoldiersCache soldiersCache, MissionsCache missionsCache, MissionService missionService)
        {
            _soldiersCache = soldiersCache;
            _missionsCache = missionsCache;
            _missionService = missionService;

            posHelper = new PositionHelper();

            SoldiersCache.ReloadCache();
            MissionsCache.ReloadCache();

            using var db = new DataLayer.ShabzakDB();

            ReloadCache();
        }

        public List<Translators.Models.Mission> AcceptAutoAssignCandidate(string candidateId)
        {
            using var db = new ShabzakDB();
            var candidates = db.SoldierMissionCandidates
                .Where(sm => sm.CandidateId == candidateId)
                .ToList();
            var soldierMissions = candidates
                .Select(sm => new SoldierMission
                {
                    Id = 0,
                    MissionInstanceId = sm.MissionInstanceId,
                    MissionPositionId = sm.MissionPositionId,
                    SoldierId = sm.SoldierId
                })
                .ToList();
            db.SoldierMission.AddRange(soldierMissions);
            db.SaveChanges();

            var instances = candidates
                .Select(c => c.MissionInstanceId)
                .Distinct()
                .ToList();
            foreach (var instanceId in instances)
            {
                var instance = db.MissionInstances
                    .Include(mi => mi.Mission)
                    .ThenInclude(m => m.MissionPositions)
                    .First(mi => mi.Id == instanceId);
                instance.IsInstanceFilled();
            }

            var reload = MissionsCache.ReloadAsync();
            db.SoldierMissionCandidates.RemoveRange(candidates);
            db.SaveChanges();
            reload.Wait();
            return _missionsCache.GetMissions();
        }

        private void ReloadCache()
        {
            using var db = new ShabzakDB();

            soldierMissionsByMissionIdBySoldierId = db.SoldierMission
                .GroupBy(sm => sm.MissionInstance.MissionId)
                .ToDictionary(k => k.Key, v => v.GroupBy(s => s.SoldierId)
                                                .ToDictionary(k => k.Key, v => v.ToList()));

            missionInstanceDic = db.MissionInstances
                .Include(mi => mi.Soldiers)
                .ThenInclude(s => s.MissionPosition)
                .ToList()
                .GroupBy(mi => mi.Id)
                .ToDictionary(k => k.Key, v => v.First());

            missionAssignments = db.Missions
                .Include(m => m.MissionInstances)
                .Include(m => m.MissionPositions)
                .ToList()
                .Select(m => m.Decrypt())
                .ToList();
            
            missionCountBySoldier = db.SoldierMission
                .GroupBy(sm => sm.SoldierId)
                .ToDictionary(k => k.Key, v => v.Count());
        }

        public AssignmentValidationModel AutoAssign(DateTime? from, DateTime? to, List<int>? missions = null, List<int>? soldiers = null)
        {
            var watch = new Stopwatch();
            watch.Start();

            ReloadCache();

            List<Soldier> selectedSoldiers;
            if (soldiers == null || soldiers.Count == 0)
            {
                selectedSoldiers = _soldiersCache.GetDBSoldiers();
                soldiers = selectedSoldiers
                    .Select(s => s.Id)
                    .ToList();
            } else
            {
                selectedSoldiers = _soldiersCache.GetDBSoldiers()
                    .Where(s => soldiers.Any(sol => sol == s.Id))
                    .ToList();
            }
            if (missions == null || missions.Count == 0)
            {
                selectedMissionAssignments = missionAssignments;
            }
            else
            {
                selectedMissionAssignments = missionAssignments
                    .Where(ma => missions.Contains(ma.Id))
                    .ToList();
            }
            DateTime absoluteFrom;
            DateTime absoluteTo;
            var flatInstances = selectedMissionAssignments.SelectMany(m => m.MissionInstances).ToList();
            if (from == null)
            {
                absoluteFrom = flatInstances.Min(mi => mi.FromTime);
            } 
            else
            {
                absoluteFrom = from.Value;
            }
            if (to == null)
            {
                absoluteTo = flatInstances.Max(mi => mi.FromTime);
            }
            else
            {
                absoluteTo = to.Value;
            }
            var candidatesPerInstance = new Dictionary<int, List<CandidateSoldierAssignment>>();
            var instances = GetMissionInstances(absoluteFrom, absoluteTo, selectedMissionAssignments);
            var skippedInstances = 0;
            foreach (var instance in instances)
            {
                Logger.LogToMemory($"Assigning to instance: {instance.Id}", LogLevel.Info);
                if(instance.IsInstanceFilled())
                {
                    candidatesPerInstance.Add(instance.Id, []);
                    Logger.LogToMemory($"Instance already filled, skipping.", LogLevel.Info);
                    continue;
                }
                var positions = instance.Mission.MissionPositions;
                var availableSoldiers = _missionService.GetAvailableSoldiers(instance.Id, soldiers);
                var candidateSoldiers = new List<CandidateSoldierAssignment>();

                foreach (var soldier in availableSoldiers)
                {
                    var candidate = new CandidateSoldierAssignment
                    {
                        Soldier = _soldiersCache.GetDbSoldierById(soldier.Soldier.Id),
                        MissionInstance = instance,
                        Rank = 0,
                        RankBreakdown = []
                    };
                    //Logger.LogToMemory($"Ranking soldier: {soldier.Soldier.Id}-{soldier.Soldier.Name}:", LogLevel.Info, ConsoleColor.Red);
                    var rank = RankSoldierForPosition(soldier, instance, candidate);
                    //Logger.LogToMemory($"Soldier {soldier.Soldier.Id} Total score : {rank}", LogLevel.Info ,ConsoleColor.Green);
                    candidate.Rank = rank;
                    candidateSoldiers.Add(candidate);
                }
                candidateSoldiers = candidateSoldiers
                    .OrderByDescending(cs => cs.Rank)
                    .Where(cs => cs.Rank > 0)
                    .ToList();

                candidatesPerInstance.Add(instance.Id, candidateSoldiers);

                var logCandidates = candidateSoldiers
                    .Select(c => new
                    {
                        Soldier = $"{c.Soldier.Id}-{c.Soldier.Name}",
                        Mission = $"{c.MissionInstance.Mission.Name}, Instance: {c.MissionInstance.Id}",
                        c.Rank,
                        c.MissionsAssignedTo,
                        Breakdown = string.Join(", ", c.RankBreakdown.Select(rb => $"{rb.Key}: {rb.Value}").ToArray())
                    })
                    .ToList();
                Logger.LogToMemory($"Candidates for mission: {instance.Mission.Name}, instance: {instance.Id}:\n{JsonConvert.SerializeObject(logCandidates, Formatting.Indented)}", LogLevel.Info, ConsoleColor.Green);
                ///TODO: remove me
                //if (skippedInstances < 5)
                //{
                //    skippedInstances++;
                //}
                //else
                //{
                //    AssignSoldiersToInstance(candidateSoldiers, instance);
                //}
                AssignSoldiersToInstance(candidateSoldiers, instance);
            }
            watch.Stop();
            var span = watch.Elapsed;
            var assignments = selectedMissionAssignments
                .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                .ToList();
            Logger.DumpMemoryLogs();
            var ret = ValidateAssignments(absoluteFrom, absoluteTo, candidatesPerInstance);
            using var db = new ShabzakDB();
            var candidateId = Guid.NewGuid().ToString();
            ret.Id = candidateId;
            db.SoldierMissionCandidates.AddRange(assignments
                .Select(ass => new SoldierMissionCandidate
                {
                    Id = 0,
                    SoldierId = ass.SoldierId,
                    MissionInstanceId = ass.MissionInstanceId,
                    MissionPositionId = ass.MissionPositionId,
                    CandidateId = candidateId,
                })
                .ToList());
            db.SaveChanges();

            var assignmentsJson = JsonConvert.SerializeObject(ret, Formatting.Indented);
            var jsonDirectory = Path.Combine(Directory.GetCurrentDirectory(), "AssignmentsResults");
            if(!Directory.Exists(jsonDirectory))
            {
                Directory.CreateDirectory(jsonDirectory);
            }
            var jsonPath = Path.Combine(jsonDirectory, $"{ret.Id}.json");
            File.WriteAllText(jsonPath, assignmentsJson);

            return ret;
        }



        public AssignmentValidationModel ValidateAssignments(DateTime? from, DateTime to, Dictionary<int, List<CandidateSoldierAssignment>> candidates)
        {
            DateTime absoluteFrom;
            var instances = selectedMissionAssignments
                    .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                    .ToList();
            if (!from.HasValue)
            {
                absoluteFrom = selectedMissionAssignments
                    .SelectMany(ma => ma.MissionInstances)
                    .Min(mi => mi.FromTime);
            }
            else
            {
                absoluteFrom = from.Value;
            }
            var ret = new AssignmentValidationModel
            {
                From = absoluteFrom,
                To = to,
                ValidInstancesCount = 0,
                TotalInstancesCount = 0,
                FaultyInstances = [],
                ValidInstances = []
            };
            if (!instances.Any())
            {
                return ret;
            }

            var missions = selectedMissionAssignments
                .OrderByDescending(m => m.SoldiersRequired)
                .ThenByDescending(m => m.CommandersRequired)
                .ToList();

            foreach (var mission in missions)
            {
                var requiredSoldiers = mission.SoldiersRequired;
                var requiredCommanders = mission.CommandersRequired;
                var total = requiredSoldiers + requiredCommanders;

                var relevantInstances = mission.MissionInstances
                    .Where(mi => mi.FromTime >= absoluteFrom && mi.ToTime <= to);
                foreach (var instance in relevantInstances)
                {
                    ret.TotalInstancesCount++;
                    var assignedCommanders = 0;
                    var assignedNonCommanders = 0;
                    foreach (var soldierMission in instance.Soldiers)
                    {
                        var position = soldierMission.MissionPosition;
                        if (position.Position.IsCommandingPosition())
                        {
                            assignedCommanders++;
                        }
                        else
                        {
                            assignedNonCommanders++;
                        }
                    }
                    if (assignedNonCommanders + assignedCommanders  == requiredSoldiers + requiredCommanders)
                    {
                        ret.ValidInstancesCount++;
                        var mi = instance.ToBL(false, true);

                        if (!ret.ValidInstances.ContainsKey(instance.Mission.Name))
                        {
                            ret.ValidInstances[instance.Mission.Name] = [];
                        }

                        ret.ValidInstances[instance.Mission.Name].Add(new CandidateMissionInstance
                        {
                            Id = mi.Id,
                            FromTime = mi.FromTime,
                            ToTime = mi.ToTime,
                            SoldierMissions = mi.SoldierMissions,
                            Candidates = candidates[instance.Id]
                                    .Select(c => new CandidateSoldierAssignmentVM
                                        {
                                            Soldier = c.Soldier.ToBL(false, false),
                                            MissionsAssignedTo = c.MissionsAssignedTo,
                                            Rank = c.Rank,
                                            RankBreakdown = c.RankBreakdown,
                                        })
                                    .ToList()
                        });
                    }
                    else
                    {
                        var mi = instance.ToBL(false, true);
                        if(!ret.FaultyInstances.ContainsKey(instance.Mission.Name))
                        {
                            ret.FaultyInstances[instance.Mission.Name] = [];
                        }
                        ret.FaultyInstances[instance.Mission.Name].Add(new CandidateMissionInstance
                        {
                            Id = mi.Id,
                            FromTime = mi.FromTime,
                            ToTime = mi.ToTime,
                            SoldierMissions = mi.SoldierMissions,
                            Candidates = candidates.ContainsKey(instance.Id) ? candidates[instance.Id]
                                .Select(c => new CandidateSoldierAssignmentVM
                                    {
                                        Soldier = c.Soldier.ToBL(false, false),
                                        MissionsAssignedTo = c.MissionsAssignedTo,
                                        Rank = c.Rank,
                                        RankBreakdown = c.RankBreakdown
                                    })
                                .ToList() : []
                        });
                    }
                }
            }

            return ret;
        }

        //public bool IsInstanceFilled(MissionInstance instance)
        //{
        //    var totalPositions = instance.Mission.MissionPositions.Sum(mp => mp.Count);
        //    using var db = new DataLayer.ShabzakDB();
        //    var totalAssignments = db.SoldierMission
        //        .Count(sm => sm.MissionInstanceId == instance.Id);
        //    instance.IsFilled = totalPositions == totalAssignments;
        //    return instance.IsFilled;
        //}

        public List<string> GetAllCandidates()
        {
            using var db = new ShabzakDB();
            var candidates = db.SoldierMissionCandidates
                .Select(sm => sm.CandidateId)
                .Distinct()
                .ToList();
            return candidates ?? [];
        }

        public AssignmentValidationModel GetCandidate(string guid)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "AssignmentsResults", $"{guid}.json");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Candidate not found.");
            }
            var json = File.ReadAllText(path);
            var ret = JsonConvert.DeserializeObject<AssignmentValidationModel>(json);
            return ret;
        }

        private void AssignSoldiersToInstance(List<CandidateSoldierAssignment> soldiers, MissionInstance missionInstance)
        {
            foreach (var soldier in soldiers)
            {
                soldier.MissionsAssignedTo = selectedMissionAssignments
                    .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                    .Count(sm => sm.SoldierId == soldier.Soldier.Id);
            }

            soldiers = soldiers
                .OrderByDescending(cs => cs.Rank)
                .ThenBy(cs => cs.MissionsAssignedTo)
                .ToList();

            var missionPositions = missionInstance.Mission.MissionPositions
                .OrderByDescending(mp => mp.Position)
                .Select(mp => new MissionPositionAssignmentCounter
                {
                    MissionPositionId = mp.Id,
                    Position = mp.Position,
                    MissionPosition = mp,
                    Count = mp.Count
                })
                .ToList();
            var assignments = new List<SoldierMission>();
            foreach (var soldier in soldiers)
            {
                if (!missionPositions.Any(mp => mp.Count > 0))
                {
                    break;
                }
                var soldierPositions = GetSoldierSimilarPositions(soldier.Soldier)
                    .OrderDescending()
                    .ToList();
                var soldierAssigned = false;
                foreach (var soldierPosition in soldierPositions)
                {
                    var position = missionPositions.FirstOrDefault(mp => mp.Position == soldierPosition);
                    if (position != null && position.Count - 1 >= 0)
                    {
                        assignments.Add(new SoldierMission
                        {
                            Id = 0,
                            SoldierId = soldier.Soldier.Id,
                            MissionInstanceId = missionInstance.Id,
                            MissionPositionId = position.MissionPositionId,
                            MissionInstance = missionInstance,
                            MissionPosition = position.MissionPosition,
                            Soldier = soldier.Soldier
                        });
                        soldierAssigned = true;
                        position.Count--;
                        break;
                    }
                }
                if (soldierAssigned)
                {
                    continue;
                }
            }
            if (assignments.Count < (missionInstance.Mission.CommandersRequired + missionInstance.Mission.SoldiersRequired))
            {
                foreach (var soldier in soldiers)
                {
                    var isAssigned = assignments
                        .Count(ass => ass.SoldierId == soldier.Soldier.Id);
                    if(isAssigned > 0)
                    {
                        continue;
                    }
                    if (!missionPositions.Any(mp => mp.Count > 0))
                    {
                        break;
                    }
                    var soldierPositions = GetSoldierSimilarPositions(soldier.Soldier)
                        .OrderDescending()
                        .ToList();
                    var soldierAssigned = false;
                    foreach (var soldierPosition in soldierPositions)
                    {
                        var position = missionPositions.FirstOrDefault(mp => mp.Position == soldierPosition);
                        if (position != null)
                        {
                            assignments.Add(new SoldierMission
                            {
                                Id = 0,
                                SoldierId = soldier.Soldier.Id,
                                MissionInstanceId = missionInstance.Id,
                                MissionPositionId = position.MissionPositionId,
                                Soldier = soldier.Soldier,
                                MissionInstance = missionInstance,
                                MissionPosition = position.MissionPosition
                            });
                            soldierAssigned = true;
                            var simplePosition = missionPositions.FirstOrDefault(p => p.Position == Position.Simple);
                            var classCommanderPosition = missionPositions.FirstOrDefault(p => p.Position == Position.ClassCommander);
                            if (simplePosition != null && simplePosition.Count > 0)
                            {
                                simplePosition.Count--;
                            } else if(classCommanderPosition != null && classCommanderPosition.Count > 0) { 
                                classCommanderPosition.Count--; 
                            }
                            break;
                        }
                    }
                    if (assignments.Count == (missionInstance.Mission.CommandersRequired + missionInstance.Mission.SoldiersRequired))
                    {
                        break;
                    }
                    if (soldierAssigned)
                    {
                        continue;
                    }
                }
            }

            if(assignments.Count > 1)
            {
                var randNum = rand.Next(0, 101);
                if (randNum <= 20)
                {
                    assignments = assignments.Take(assignments.Count - 1).ToList();
                }
            }
            missionInstance.Soldiers = assignments;
            missionInstance.IsInstanceFilled();
        }

        private List<MissionInstance> GetMissionInstances(DateTime from, DateTime to, List<Mission> missions)
        {
            var ret = new List<MissionInstance>();
            foreach (var mission in missions)
            {
                var instances = mission.MissionInstances
                    .Where(mi => mi.FromTime >= from && mi.ToTime <= to)
                    .ToList();

                ret.AddRange(instances);
            }
            return ret;
        }

        private double RankSoldierForPosition(GetAvailableSoldiersModel soldier, MissionInstance missionInstance, CandidateSoldierAssignment candidate)
        {
            var baseScore = 1.0;
            var soldierRestMultiplier = GetSoldierRestMultiplier(soldier);
            candidate.RankBreakdown.Add("Rest Multiplier", soldierRestMultiplier);

            //Logger.LogToMemory($"\tSoldier Rest Multiplier is: {soldierRestMultiplier}");
            if (soldierRestMultiplier == 0.0)
            {
                return 0.0;
            }
            baseScore *= soldierRestMultiplier;
            var dbSoldier = _soldiersCache.GetDbSoldierById(soldier.Soldier.Id);

            var positionScoreMultiplier = GetSoldierPositionMultiplier(dbSoldier, missionInstance);
            candidate.RankBreakdown.Add("Position Multiplier", positionScoreMultiplier);

            //Logger.LogToMemory($"\tSoldier Position Multiplier is: {positionScoreMultiplier}");
            if (positionScoreMultiplier == 0.0)
            {
                return 0.0;
            }
            baseScore *= positionScoreMultiplier;

            var missionAvg = RankSoldierByMissionAvg(soldier, missionInstance);
            candidate.RankBreakdown.Add("Mission Avg Multiplier", missionAvg);

            //Logger.LogToMemory($"\tSoldier Mission Avg Multiplier is: {missionAvg}");
            baseScore *= missionAvg;

            var totalMissionAvg = RankSoldierByTotalMissionsAvg(soldier, missionInstance);
            candidate.RankBreakdown.Add("Total Mission Avg Multiplier", totalMissionAvg);

            //Logger.LogToMemory($"\tSoldier Total Mission Avg Multiplier is: {missionAvg}");
            baseScore *= totalMissionAvg;

            var hoursAvg = RankSoldierByHoursAvg(soldier, missionInstance);
            candidate.RankBreakdown.Add("Mission Hours Avg Multiplier", hoursAvg);

            //Logger.LogToMemory($"\tSoldier Total Mission Avg Multiplier is: {missionAvg}");
            baseScore *= hoursAvg;

            var totalHoursAvg = RankSoldierByTotalHoursAvg(soldier, missionInstance);
            candidate.RankBreakdown.Add("Total Hours Avg Multiplier", totalHoursAvg);

            //Logger.LogToMemory($"\tSoldier Total Mission Avg Multiplier is: {missionAvg}");
            baseScore *= totalHoursAvg;

            //RankSoldierByTotalHoursAvg
            return baseScore;
        }


        private double RankSoldierByMissionAvg(GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            var instances = missionInstanceDic;
            var soldierMissions = selectedMissionAssignments
                .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                .ToList();
            var soldiers = soldierMissions
                .Where(sm => missionInstanceDic[sm.MissionInstanceId].MissionId == missionInstance.MissionId)
                .GroupBy(sm => sm.SoldierId)
                .ToDictionary(k => k.Key, v => v.ToList());

            double total = soldiers.Keys
                .Count;
            if(total == 0)
            {
                return 1.0;
            }
            double assignmentsSum = soldiers.SelectMany(s => s.Value).Count();
            var avg = assignmentsSum / total;
            var soldierMissionAssignments = selectedMissionAssignments
                .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                .Count(sm => sm.SoldierId == soldier.Soldier.Id && missionInstanceDic[sm.MissionInstanceId].MissionId == missionInstance.MissionId);
            if(soldierMissionAssignments <= avg)
            {
                return 1.0;
            }
            var marginError = avg * 1.05;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.85;
            }
            marginError = avg * 1.1;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.7;
            }
            marginError = avg * 1.15;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.5;
            }
            marginError = avg * 1.2;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.2;
            }
            return 0.0;
        }

        private double RankSoldierByTotalMissionsAvg(GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            var soldiers = selectedMissionAssignments
                .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                .GroupBy(sm => sm.SoldierId)
                .ToDictionary(k => k.Key, v => v.ToList());

            double total = soldiers.Keys
                .Count;
            if (total == 0)
            {
                return 1.0;
            }
            double assignmentsSum = soldiers.SelectMany(s => s.Value).Count();
            var avg = assignmentsSum / total;
            var soldierMissionAssignments = selectedMissionAssignments
                .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                .Count(sm => sm.SoldierId == soldier.Soldier.Id);
            if (soldierMissionAssignments <= avg)
            {
                return 1.0;
            }
            var marginError = avg * 1.05;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.85;
            }
            marginError = avg * 1.1;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.7;
            }
            marginError = avg * 1.15;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.5;
            }
            marginError = avg * 1.2;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.2;
            }
            return 0.0;
        }

        private double RankSoldierByHoursAvg(GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            var soldiers = selectedMissionAssignments
                .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                .Where(sm => missionInstanceDic[sm.MissionInstanceId].MissionId == missionInstance.MissionId)
                .GroupBy(sm => sm.SoldierId)
                .ToDictionary(k => k.Key, v => v.ToList());

            double total = soldiers.Keys
                .Count;
            if (total == 0)
            {
                return 1.0;
            }
            double assignmentsSum = soldiers.SelectMany(s => s.Value).Count();
            var avg = assignmentsSum / total;
            var soldierMissionAssignments = selectedMissionAssignments
                .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                .Count(sm => sm.SoldierId == soldier.Soldier.Id && missionInstanceDic[sm.MissionInstanceId].MissionId == missionInstance.MissionId);
            if (soldierMissionAssignments < avg)
            {
                return 1.0;
            }
            var marginError = avg * 1.05;
            if (soldierMissionAssignments < marginError)
            {
                return 0.85;
            }
            marginError = avg * 1.1;
            if (soldierMissionAssignments < marginError)
            {
                return 0.7;
            }
            marginError = avg * 1.15;
            if (soldierMissionAssignments < marginError)
            {
                return 0.5;
            }
            marginError = avg * 1.2;
            if (soldierMissionAssignments < marginError)
            {
                return 0.2;
            }
            return 0.0;
        }

        private double RankSoldierByTotalHoursAvg(GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            var soldiers = selectedMissionAssignments
                .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                .GroupBy(sm => sm.SoldierId)
                .ToDictionary(k => k.Key, v => v.ToList());

            double total = soldiers.Keys
                .Count;
            if (total == 0)
            {
                return 1.0;
            }
            double assignmentsSum = soldiers.SelectMany(s => s.Value).Count();
            var avg = assignmentsSum / total;
            var soldierMissionAssignments = selectedMissionAssignments
                .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                .Count(sm => sm.SoldierId == soldier.Soldier.Id);
            if (soldierMissionAssignments < avg)
            {
                return 1.0;
            }
            var marginError = avg * 1.05;
            if (soldierMissionAssignments < marginError)
            {
                return 0.85;
            }
            marginError = avg * 1.1;
            if (soldierMissionAssignments < marginError)
            {
                return 0.7;
            }
            marginError = avg * 1.15;
            if (soldierMissionAssignments < marginError)
            {
                return 0.5;
            }
            marginError = avg * 1.2;
            if (soldierMissionAssignments < marginError)
            {
                return 0.2;
            }
            return 0.0;
        }


        private double GetSoldierPositionMultiplier(Soldier soldier, MissionInstance missionInstance)
        {
            var missionPositions = missionInstance.Mission.MissionPositions;
            foreach (var soldierPosition in soldier.GetSoldierPositions())
            {
                if (missionPositions.Any(mp => mp.Position == soldierPosition))
                {
                    return 1;
                }
            }
            var soldierPositions = GetSoldierSimilarPositions(soldier);
            var matchingCount = 0;
            foreach (var soldierPosition in soldierPositions)
            {
                if(missionPositions.Any(mp => mp.Position == soldierPosition))
                {
                    matchingCount++;
                }
            }
            return matchingCount switch
            {
                0 => 0,
                1 => 0.7,
                2 => 0.8,
                3 => 0.9,
                _ => 1
            };
        }

        private double GetSoldierRestMultiplier(GetAvailableSoldiersModel soldier)
        {
            soldier.RestTimeBefore = soldier.RestTimeBefore ?? int.MaxValue;
            soldier.RestTimeAfter = soldier.RestTimeAfter ?? int.MaxValue;
            if (soldier.RestTimeBefore == 0 || soldier.RestTimeAfter == 0)
            {
                return 0.0;
            }
            if (soldier.RestTimeBefore < 8 && soldier.RestTimeAfter < 8)
            {
                return 0.1;
            }
            if (soldier.RestTimeBefore < 8 || soldier.RestTimeAfter < 8)
            {
                return 0.3;
            }
            if (soldier.RestTimeBefore == int.MaxValue || soldier.RestTimeAfter == int.MaxValue)
            {
                return 1.0;
            }
            if(soldier.RestTimeBefore >= 12)
            {
                return 0.9;
            }
            if (soldier.RestTimeBefore >= 10)
            {
                return 0.8;
            }
            if (soldier.RestTimeBefore >= 8)
            {
                return 0.7;
            }
            return 0.0;
        }

        private List<Position> GetSoldierPositions(Soldier soldier)
        {
            return soldier.Position.Split(",", StringSplitOptions.RemoveEmptyEntries)
                    .Select(s =>
                    {
                        if (int.TryParse(s, out int numericValue))
                        {
                            return (Position)numericValue;
                        }
                        return Position.Simple;
                    })
                    .ToList();
        }

        private List<Position> GetSoldierSimilarPositions(Soldier soldier)
        {
            if(soldierPositionCache.ContainsKey(soldier.Id))
            {
                return soldierPositionCache[soldier.Id];
            }
            var pos = soldier.GetSoldierPositions()
                    .SelectMany(s => posHelper.GetSimilarPositions(s))
                    .Distinct()
                    .ToList();
            soldierPositionCache[soldier.Id] = pos;
            return pos;
        }
    }
}
