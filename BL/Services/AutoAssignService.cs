using BL.Cache;
using DataLayer.Models;
using BL.Models;
using BL.Logging;
using BL.Extensions;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
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
        private Dictionary<int, Dictionary<int, List<SoldierMission>>> soldierMissionsByMissionIdBySoldierId;
        private Dictionary<int, int> missionCountBySoldier;

        private Dictionary<int, MissionInstance> missionInstanceDic;

        private readonly Random rand = new Random();

        private static readonly bool _verboseLogging = System.Diagnostics.Debugger.IsAttached;

        // Interactive session store
        private readonly ConcurrentDictionary<string, InteractiveAutoAssignSession> _interactiveSessions = new();
        private readonly Timer _sessionCleanupTimer;
        private const int MaxConcurrentSessions = 16;
        private const int SessionIdleTimeoutMinutes = 60;
        private volatile bool _batchRunning;

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

            _sessionCleanupTimer = new Timer(_ => CleanupExpiredSessions(), null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        private void CleanupExpiredSessions()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-SessionIdleTimeoutMinutes);
            foreach (var kvp in _interactiveSessions)
            {
                if (kvp.Value.LastTouchedAt < cutoff)
                {
                    _interactiveSessions.TryRemove(kvp.Key, out _);
                    Logger.Log($"Interactive session {kvp.Key} expired and removed.");
                }
            }
        }

        public List<Translators.Models.Mission> AcceptAutoAssignCandidate(string candidateId)
        {
            using var db = new ShabzakDB();

            var allCandidates = db.SoldierMissionCandidates.ToList();
            var candidates = allCandidates
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
            db.SoldierMissionCandidates.RemoveRange(allCandidates);
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

        public List<AssignmentValidationModel> AutoAssign(
            DateTime? from,
            DateTime? to,
            List<int>? missions = null,
            List<int>? soldiers = null,
            int? maxSchedules = null,
            AutoAssignScoringOptions? scoringOptions = null)
        {
            if (_interactiveSessions.Count > 0)
            {
                throw new InvalidOperationException("Cannot run batch auto-assign while interactive sessions are active.");
            }
            _batchRunning = true;
            try
            {
                return AutoAssignInternal(from, to, missions, soldiers, maxSchedules, scoringOptions);
            }
            finally
            {
                _batchRunning = false;
            }
        }

        private List<AssignmentValidationModel> AutoAssignInternal(
            DateTime? from,
            DateTime? to,
            List<int>? missions,
            List<int>? soldiers,
            int? maxSchedules,
            AutoAssignScoringOptions? scoringOptions)
        {
            var watch = new Stopwatch();
            watch.Start();

            var ctx = new RunContext();
            ctx.ScoringOptions = scoringOptions ?? new AutoAssignScoringOptions();
            ReloadCache();

            List<Soldier> selectedSoldiers;
            if (soldiers == null || soldiers.Count == 0)
            {
                selectedSoldiers = _soldiersCache.GetDBSoldiers();
                soldiers = selectedSoldiers.Select(s => s.Id).ToList();
            }
            else
            {
                selectedSoldiers = _soldiersCache.GetDBSoldiers()
                    .Where(s => soldiers.Any(sol => sol == s.Id))
                    .ToList();
            }
            ctx.SelectedSoldierIds = soldiers;

            List<Mission> selectedMissionAssignments;
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
            ctx.SelectedMissionAssignments = selectedMissionAssignments;

            var flatInstances = selectedMissionAssignments.SelectMany(m => m.MissionInstances).ToList();
            var absoluteFrom = from ?? (flatInstances.Any() ? flatInstances.Min(mi => mi.FromTime) : DateTime.MinValue);
            var absoluteTo = to ?? (flatInstances.Any() ? flatInstances.Max(mi => mi.FromTime) : DateTime.MaxValue);

            ctx.AllInstances = GetMissionInstances(absoluteFrom, absoluteTo, selectedMissionAssignments);

            ctx.OriginalSoldiersByInstanceId = ctx.AllInstances.ToDictionary(
                mi => mi.Id,
                mi => (mi.Soldiers ?? new List<SoldierMission>()).ToList());

            var schedulesToRun = SelectStartingMissions(selectedMissionAssignments, ctx.AllInstances, maxSchedules);

            var results = new List<AssignmentValidationModel>();
            for (var i = 0; i < schedulesToRun.Count; i++)
            {
                var scheduleWatch = Stopwatch.StartNew();
                var result = RunSchedule(ctx, schedulesToRun[i], i, selectedSoldiers, absoluteFrom, absoluteTo);
                scheduleWatch.Stop();
                Logger.Log($"AutoAssign schedule #{i} (start={result.StartingMissionId}) took {scheduleWatch.Elapsed.TotalSeconds:F2}s");
                results.Add(result);
            }

            if (results.Count > 0)
            {
                var best = results
                    .OrderByDescending(r => r.ValidInstancesCount)
                    .ThenBy(r => r.EvennessScore ?? double.MaxValue)
                    .ThenBy(r => r.FaultyInstances?.Values.Sum(list => list.Count) ?? 0)
                    .ThenBy(r => r.StartingMissionId ?? int.MaxValue)
                    .First();
                best.IsBestCandidate = true;
            }

            watch.Stop();
            Logger.Log($"AutoAssign produced {results.Count} schedule(s) in {watch.Elapsed.TotalSeconds:F2}s total");
            Logger.DumpMemoryLogs();
            return results;
        }

        private List<Mission> SelectStartingMissions(List<Mission> missions, List<MissionInstance> instancesInWindow, int? maxSchedules)
        {
            var cap = (maxSchedules.HasValue && maxSchedules.Value > 0) ? maxSchedules.Value : 4;
            if (missions == null || missions.Count == 0)
            {
                return [];
            }
            if (missions.Count == 1 || cap == 1)
            {
                return [missions.OrderBy(m => m.Id).First()];
            }

            var instanceCountByMission = instancesInWindow
                .GroupBy(mi => mi.MissionId)
                .ToDictionary(g => g.Key, g => g.Count());

            return missions
                .OrderByDescending(m => (m.SoldiersRequired + m.CommandersRequired)
                                        * (instanceCountByMission.TryGetValue(m.Id, out var c) ? c : 0))
                .ThenBy(m => m.Id)
                .Take(Math.Min(cap, missions.Count))
                .ToList();
        }

        private AssignmentValidationModel RunSchedule(
            RunContext ctx,
            Mission startingMission,
            int scheduleIndex,
            List<Soldier> selectedSoldiers,
            DateTime absoluteFrom,
            DateTime absoluteTo)
        {
            RestoreInstanceSoldiers(ctx);
            BuildAvailabilityIndex(ctx, selectedSoldiers);
            PrimeAggregates(ctx);

            ctx.JitterSeed = ctx.ScoringOptions.JitterSeed ?? scheduleIndex;

            var orderedInstances = ctx.AllInstances
                .OrderBy(mi => mi.MissionId == startingMission.Id ? 0 : 1)
                .ThenBy(mi => mi.FromTime)
                .ThenBy(mi => mi.MissionId)
                .ThenBy(mi => mi.Id)
                .ToList();

            ctx.CandidatesPerInstance = new Dictionary<int, List<CandidateSoldierAssignment>>();
            ctx.SkippedInstanceIds = new HashSet<int>();

            foreach (var instance in orderedInstances)
            {
                Logger.LogToMemory($"[Sch#{scheduleIndex} start={startingMission.Id}] Assigning to instance: {instance.Id}", LogLevel.Info);

                var originalSoldiers = ctx.OriginalSoldiersByInstanceId.TryGetValue(instance.Id, out var orig) ? orig : [];
                if (originalSoldiers.Count > 0)
                {
                    ctx.SkippedInstanceIds.Add(instance.Id);
                    ctx.CandidatesPerInstance.Add(instance.Id, []);
                    Logger.LogToMemory($"Instance {instance.Id} already has {originalSoldiers.Count} accepted assignment(s), skipping.", LogLevel.Info);
                    continue;
                }

                if (IsInstanceFilledInMemory(instance))
                {
                    ctx.CandidatesPerInstance.Add(instance.Id, []);
                    Logger.LogToMemory($"Instance already filled, skipping.", LogLevel.Info);
                    continue;
                }

                var candidateSoldiers = ComputeAndRankCandidates(ctx, instance);
                ctx.CandidatesPerInstance.Add(instance.Id, candidateSoldiers);

                LogCandidates(candidateSoldiers, instance, scheduleIndex, startingMission.Id);

                var (assignments, _, isFilled) = TryFillInstance(ctx, candidateSoldiers, instance);
                CommitAssignment(ctx, instance, assignments);
            }

            return FinalizeScheduleResult(ctx, startingMission, absoluteFrom, absoluteTo);
        }

        private List<CandidateSoldierAssignment> ComputeAndRankCandidates(RunContext ctx, MissionInstance instance)
        {
            var availableSoldiers = ComputeAvailableSoldiers(ctx, instance, ctx.SelectedSoldierIds);
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
                candidate.Rank = RankSoldierForPosition(ctx, soldier, instance, candidate);
                candidateSoldiers.Add(candidate);
            }

            candidateSoldiers = candidateSoldiers
                .OrderByDescending(cs => cs.Rank)
                .Where(cs => cs.Rank > 0)
                .ToList();

            return candidateSoldiers;
        }

        private void LogCandidates(List<CandidateSoldierAssignment> candidateSoldiers, MissionInstance instance, int scheduleIndex, int startingMissionId)
        {
            if (_verboseLogging)
            {
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
            }
        }

        private (List<SoldierMission> Assignments, Dictionary<Translators.Models.Position, int> MissingPositions, bool IsFilled) TryFillInstance(
            RunContext ctx, List<CandidateSoldierAssignment> soldiers, MissionInstance missionInstance)
        {
            foreach (var soldier in soldiers)
            {
                ctx.TotalAssignmentsBySoldier.TryGetValue(soldier.Soldier.Id, out var total);
                soldier.MissionsAssignedTo = total;
            }

            if (ctx.ScoringOptions.DeterministicJitter && ctx.ScoringOptions.JitterEpsilon != 0)
            {
                foreach (var soldier in soldiers)
                {
                    var r = DeterministicRandom(ctx.JitterSeed, soldier.Soldier.Id, missionInstance.Id);
                    var jitter = (r - 0.5) * 2.0 * ctx.ScoringOptions.JitterEpsilon;
                    soldier.Rank *= (1 + jitter);
                }
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

            if (!ctx.ScoringOptions.DeterministicJitter && assignments.Count > 1)
            {
                var randNum = rand.Next(0, 101);
                if (randNum <= 20)
                {
                    assignments = assignments.Take(assignments.Count - 1).ToList();
                }
            }

            var required = missionInstance.Mission.MissionPositions?.Sum(mp => mp.Count) ?? 0;
            var isFilled = required > 0 && assignments.Count >= required;

            var missingPositions = new Dictionary<Translators.Models.Position, int>();
            if (!isFilled)
            {
                var mi = missionInstance.ToBL(false, true);
                mi.SoldierMissions = assignments.Select(a =>
                {
                    var sm = new Translators.Models.SoldierMission
                    {
                        Id = a.Id,
                        Soldier = a.Soldier?.ToBL(false, false),
                        MissionPosition = a.MissionPosition != null
                            ? new Translators.Models.MissionPosition
                            {
                                Id = a.MissionPosition.Id,
                                Position = (Translators.Models.Position)(int)a.MissionPosition.Position,
                                Count = a.MissionPosition.Count
                            }
                            : null
                    };
                    return sm;
                }).ToList();
                missingPositions = FillMissingPositions(mi, missionInstance.Mission);
            }

            return (assignments, missingPositions, isFilled);
        }

        private void CommitAssignment(RunContext ctx, MissionInstance missionInstance, List<SoldierMission> assignments)
        {
            missionInstance.Soldiers = assignments;
            missionInstance.IsFilled = IsInstanceFilledInMemory(missionInstance);

            var hours = InstanceHours(missionInstance);
            foreach (var assignment in assignments)
            {
                TrackAssignment(ctx, assignment.SoldierId, missionInstance.MissionId, hours);
                AddIntervalForSoldier(ctx, assignment.SoldierId, missionInstance.FromTime, missionInstance.ToTime, missionInstance.Mission?.RequiredRestAfter);
            }
        }

        private AssignmentValidationModel FinalizeScheduleResult(
            RunContext ctx, Mission startingMission, DateTime absoluteFrom, DateTime absoluteTo)
        {
            var scheduleAssignments = ctx.AllInstances
                .SelectMany(mi => mi.Soldiers ?? new List<SoldierMission>())
                .ToList();

            var ret = ValidateAssignments(ctx, absoluteFrom, absoluteTo, ctx.CandidatesPerInstance);
            ret.StartingMissionId = startingMission.Id;
            ret.StartingMissionName = startingMission.Name;
            ret.EvennessScore = ComputeEvennessScore(ctx);

            using var db = new ShabzakDB();
            var candidateId = Guid.NewGuid().ToString();
            ret.Id = candidateId;
            db.SoldierMissionCandidates.AddRange(scheduleAssignments
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
            if (!Directory.Exists(jsonDirectory))
            {
                Directory.CreateDirectory(jsonDirectory);
            }
            var jsonPath = Path.Combine(jsonDirectory, $"{ret.Id}.json");
            File.WriteAllText(jsonPath, assignmentsJson);

            return ret;
        }

        private void RestoreInstanceSoldiers(RunContext ctx)
        {
            foreach (var instance in ctx.AllInstances)
            {
                if (ctx.OriginalSoldiersByInstanceId.TryGetValue(instance.Id, out var original))
                {
                    instance.Soldiers = original.ToList();
                }
                else
                {
                    instance.Soldiers = [];
                }
                instance.IsFilled = IsInstanceFilledInMemory(instance);
            }
        }

        private static bool IsInstanceFilledInMemory(MissionInstance instance)
        {
            var required = instance.Mission?.MissionPositions?.Sum(mp => mp.Count) ?? 0;
            var current = instance.Soldiers?.Count ?? 0;
            return required > 0 && current >= required;
        }

        private static double InstanceHours(MissionInstance instance)
        {
            if (instance == null) return 1.0;
            if (instance.Mission?.ActualHours.HasValue == true && instance.Mission.ActualHours.Value > 0)
            {
                return instance.Mission.ActualHours.Value;
            }
            var h = (instance.ToTime - instance.FromTime).TotalHours;
            return h > 0 ? h : 1.0;
        }

        private void BuildAvailabilityIndex(RunContext ctx, List<Soldier> selectedSoldiers)
        {
            ctx.SoldierIntervalsById = new Dictionary<int, List<(DateTime From, DateTime To, int? RequiredRestAfter)>>();

            foreach (var soldier in selectedSoldiers)
            {
                var list = new List<(DateTime From, DateTime To, int? RequiredRestAfter)>();
                if (soldier.Vacations != null)
                {
                    foreach (var vacation in soldier.Vacations)
                    {
                        if (vacation.Approved != VacationRequestStatus.Denied)
                        {
                            list.Add((vacation.From, vacation.To, null));
                        }
                    }
                }
                ctx.SoldierIntervalsById[soldier.Id] = list;
            }

            foreach (var mi in ctx.AllInstances)
            {
                if (mi.Soldiers == null) continue;
                var missionRest = mi.Mission?.RequiredRestAfter;
                foreach (var sm in mi.Soldiers)
                {
                    if (!ctx.SoldierIntervalsById.TryGetValue(sm.SoldierId, out var list))
                    {
                        list = new List<(DateTime From, DateTime To, int? RequiredRestAfter)>();
                        ctx.SoldierIntervalsById[sm.SoldierId] = list;
                    }
                    list.Add((mi.FromTime, mi.ToTime, missionRest));
                }
            }

            var allInstanceIds = ctx.AllInstances.Select(mi => mi.Id).ToHashSet();
            foreach (var soldier in selectedSoldiers)
            {
                if (soldier.Missions == null) continue;
                foreach (var sm in soldier.Missions)
                {
                    if (sm.MissionInstance == null) continue;
                    if (allInstanceIds.Contains(sm.MissionInstanceId)) continue;
                    var extRest = sm.MissionInstance.Mission?.RequiredRestAfter;
                    if (!ctx.SoldierIntervalsById.TryGetValue(soldier.Id, out var list))
                    {
                        list = new List<(DateTime From, DateTime To, int? RequiredRestAfter)>();
                        ctx.SoldierIntervalsById[soldier.Id] = list;
                    }
                    list.Add((sm.MissionInstance.FromTime, sm.MissionInstance.ToTime, extRest));
                }
            }

            foreach (var key in ctx.SoldierIntervalsById.Keys.ToList())
            {
                ctx.SoldierIntervalsById[key] = ctx.SoldierIntervalsById[key]
                    .OrderBy(t => t.From)
                    .ToList();
            }
        }

        private void AddIntervalForSoldier(RunContext ctx, int soldierId, DateTime fromTime, DateTime toTime, int? requiredRestAfter = null)
        {
            if (!ctx.SoldierIntervalsById.TryGetValue(soldierId, out var list))
            {
                list = new List<(DateTime From, DateTime To, int? RequiredRestAfter)>();
                ctx.SoldierIntervalsById[soldierId] = list;
            }
            var idx = 0;
            while (idx < list.Count && list[idx].From <= fromTime)
            {
                idx++;
            }
            list.Insert(idx, (fromTime, toTime, requiredRestAfter));
        }

        private void PrimeAggregates(RunContext ctx)
        {
            ctx.TotalAssignmentsBySoldier = new Dictionary<int, int>();
            ctx.TotalHoursBySoldier = new Dictionary<int, double>();
            ctx.SoldiersAssignedByMissionId = new Dictionary<int, HashSet<int>>();
            ctx.PerMissionAssignmentsBySoldier = new Dictionary<(int MissionId, int SoldierId), int>();
            ctx.PerMissionHoursBySoldier = new Dictionary<(int MissionId, int SoldierId), double>();
            ctx.PerMissionAssignmentCount = new Dictionary<int, int>();
            ctx.PerMissionHoursSum = new Dictionary<int, double>();
            ctx.TotalAssignmentsCount = 0;
            ctx.TotalHoursSum = 0;

            foreach (var instance in ctx.AllInstances)
            {
                if (instance.Soldiers == null) continue;
                var hours = InstanceHours(instance);
                foreach (var sm in instance.Soldiers)
                {
                    TrackAssignment(ctx, sm.SoldierId, instance.MissionId, hours);
                }
            }
        }

        private void TrackAssignment(RunContext ctx, int soldierId, int missionId, double hours)
        {
            ctx.TotalAssignmentsBySoldier.TryGetValue(soldierId, out var totalCount);
            ctx.TotalAssignmentsBySoldier[soldierId] = totalCount + 1;

            ctx.TotalHoursBySoldier.TryGetValue(soldierId, out var totalHours);
            ctx.TotalHoursBySoldier[soldierId] = totalHours + hours;

            ctx.TotalAssignmentsCount++;
            ctx.TotalHoursSum += hours;

            if (!ctx.SoldiersAssignedByMissionId.TryGetValue(missionId, out var set))
            {
                set = new HashSet<int>();
                ctx.SoldiersAssignedByMissionId[missionId] = set;
            }
            set.Add(soldierId);

            var key = (missionId, soldierId);
            ctx.PerMissionAssignmentsBySoldier.TryGetValue(key, out var perCount);
            ctx.PerMissionAssignmentsBySoldier[key] = perCount + 1;

            ctx.PerMissionHoursBySoldier.TryGetValue(key, out var perHours);
            ctx.PerMissionHoursBySoldier[key] = perHours + hours;

            ctx.PerMissionAssignmentCount.TryGetValue(missionId, out var missionCount);
            ctx.PerMissionAssignmentCount[missionId] = missionCount + 1;

            ctx.PerMissionHoursSum.TryGetValue(missionId, out var missionHours);
            ctx.PerMissionHoursSum[missionId] = missionHours + hours;
        }

        private double ComputeEvennessScore(RunContext ctx)
        {
            if (ctx.SelectedSoldierIds == null || ctx.SelectedSoldierIds.Count == 0)
            {
                return 0.0;
            }
            var counts = ctx.SelectedSoldierIds
                .Select(id => (double)(ctx.TotalAssignmentsBySoldier.TryGetValue(id, out var c) ? c : 0))
                .ToList();
            var mean = counts.Average();
            if (mean <= 0) return 0.0;
            var variance = counts.Select(c => (c - mean) * (c - mean)).Sum() / counts.Count;
            var stddev = Math.Sqrt(variance);
            return stddev / Math.Max(mean, 1.0);
        }

        private List<GetAvailableSoldiersModel> ComputeAvailableSoldiers(RunContext ctx, MissionInstance instance, List<int> soldierIds)
        {
            var startTime = instance.FromTime;
            var endTime = instance.ToTime;
            var ret = new List<GetAvailableSoldiersModel>();

            foreach (var soldierId in soldierIds)
            {
                var dbSoldier = _soldiersCache.GetDbSoldierById(soldierId);
                if (dbSoldier == null) continue;

                if (instance.Soldiers != null && instance.Soldiers.Any(sm => sm.SoldierId == soldierId))
                {
                    continue;
                }

                ctx.SoldierIntervalsById.TryGetValue(soldierId, out var intervals);

                int? restBefore = null;
                int? restAfter = null;
                int? restBeforeThreshold = null;
                bool overlap = false;

                if (intervals != null)
                {
                    foreach (var (iFrom, iTo, iRequiredRestAfter) in intervals)
                    {
                        if (OverlappingTimes(startTime, endTime, iFrom, iTo))
                        {
                            overlap = true;
                            break;
                        }
                        if (iTo <= startTime)
                        {
                            var diff = (int)(startTime - iTo).TotalHours;
                            if (restBefore == null || diff < restBefore)
                            {
                                restBefore = diff;
                                restBeforeThreshold = iRequiredRestAfter;
                            }
                        }
                        else if (iFrom >= endTime)
                        {
                            var diff = (int)(iFrom - endTime).TotalHours;
                            if (restAfter == null || diff < restAfter) restAfter = diff;
                        }
                    }
                }

                var currentMissionRest = instance.Mission?.RequiredRestAfter;
                var blSoldier = dbSoldier.ToBL(false, false);
                var model = new GetAvailableSoldiersModel
                {
                    Soldier = blSoldier,
                    RestTimeBefore = overlap ? 0 : restBefore,
                    RestTimeAfter = overlap ? 0 : restAfter,
                    RequiredRestBeforeThreshold = restBeforeThreshold,
                    RequiredRestAfterThreshold = currentMissionRest
                };
                ret.Add(model);
            }

            foreach (var r in ret)
            {
                r.Soldier.Positions = r.Soldier.Positions
                    .OrderByDescending(p => p)
                    .ToList();
            }

            return ret;
        }

        private static bool OverlappingTimes(DateTime aFrom, DateTime aTo, DateTime bFrom, DateTime bTo)
        {
            return aFrom < bTo && bFrom < aTo;
        }



        public AssignmentValidationModel ValidateAssignments(RunContext ctx, DateTime? from, DateTime to, Dictionary<int, List<CandidateSoldierAssignment>> candidates)
        {
            DateTime absoluteFrom;
            var instances = ctx.SelectedMissionAssignments
                    .SelectMany(ma => ma.MissionInstances.SelectMany(mi => mi.Soldiers))
                    .ToList();
            if (!from.HasValue)
            {
                absoluteFrom = ctx.SelectedMissionAssignments
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
                ValidInstances = [],
                SkippedInstances = [],
                SkippedInstancesCount = 0
            };
            if (!instances.Any())
            {
                return ret;
            }

            var missions = ctx.SelectedMissionAssignments
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

                    if (ctx.SkippedInstanceIds.Contains(instance.Id))
                    {
                        ret.SkippedInstancesCount++;
                        var skippedMi = instance.ToBL(false, true);
                        if (!ret.SkippedInstances.ContainsKey(instance.Mission.Name))
                        {
                            ret.SkippedInstances[instance.Mission.Name] = [];
                        }
                        ret.SkippedInstances[instance.Mission.Name].Add(new CandidateMissionInstance
                        {
                            Id = skippedMi.Id,
                            FromTime = skippedMi.FromTime,
                            ToTime = skippedMi.ToTime,
                            SoldierMissions = skippedMi.SoldierMissions
                        });
                        continue;
                    }

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
                            Candidates = candidates.ContainsKey(instance.Id) ? candidates[instance.Id]
                                    .Select(c => new CandidateSoldierAssignmentVM
                                        {
                                            Soldier = c.Soldier.ToBL(false, false),
                                            MissionsAssignedTo = c.MissionsAssignedTo,
                                            Rank = c.Rank,
                                            RankBreakdown = c.RankBreakdown,
                                        })
                                    .ToList() : []
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
                            MissingPositions = FillMissingPositions(mi, mission),
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

        private static Dictionary<Translators.Models.Position, int> FillMissingPositions(Translators.Models.MissionInstance mi, Mission mission)
        {
            // Build the required-positions map from the in-memory mission (already loaded
            // in ReloadCache via .Include(m => m.MissionPositions)). This replaces the
            // previous approach of deriving MissionId from mi.SoldierMissions.First(),
            // which threw NullReferenceException when an instance had zero assignments.
            var positions = new Dictionary<Translators.Models.Position, int>();
            foreach (var mp in mission.MissionPositions ?? [])
            {
                var key = (Translators.Models.Position)(int)mp.Position;
                if (!positions.ContainsKey(key))
                {
                    positions[key] = mp.Count;
                }
            }

            foreach (var position in (mi.SoldierMissions ?? []).Select(sm => sm.MissionPosition).Where(mp => mp != null))
            {
                if (positions.ContainsKey(position.Position))
                {
                    positions[position.Position]--;
                }
            }

            return positions
                .Where(p => p.Value > 0)
                .ToDictionary(p => p.Key, p => p.Value);
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

        public void RemoveSoldierFromMissionInstance(int soldierId, int missionInstanceId)
        {
            using var db = new ShabzakDB();
            var instance = db.MissionInstances
                .Include(mi => mi.Mission)
                .First(mi => mi.Id == missionInstanceId);
            var model = db.SoldierMission
                .FirstOrDefault(mi => mi.SoldierId == soldierId && mi.MissionInstanceId == missionInstanceId)
                ?? throw new ArgumentException("Soldier assignment not found");
            var totalPositions = instance.Mission.MissionPositions.Sum(mp => mp.Count);
            var assignedPositions = db.SoldierMission.Count(sm => sm.MissionInstanceId == missionInstanceId);
            if((assignedPositions - 1) < totalPositions)
            {
                instance.IsFilled = false;
            }
            db.SoldierMission.Remove(model);
            db.SaveChanges();
        }

        private static double DeterministicRandom(int seed, int soldierId, int instanceId)
        {
            unchecked
            {
                uint h = (uint)(seed * 2654435761u);
                h ^= (uint)(soldierId * 2246822519u);
                h ^= (uint)(instanceId * 3266489917u);
                h ^= h << 13;
                h ^= h >> 17;
                h ^= h << 5;
                return (h & 0x7FFFFFFFu) / (double)0x80000000u;
            }
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

        private double RankSoldierForPosition(RunContext ctx, GetAvailableSoldiersModel soldier, MissionInstance missionInstance, CandidateSoldierAssignment candidate)
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

            var positionScoreMultiplier = GetSoldierPositionMultiplier(ctx, dbSoldier, missionInstance);
            candidate.RankBreakdown.Add("Position Multiplier", positionScoreMultiplier);

            //Logger.LogToMemory($"\tSoldier Position Multiplier is: {positionScoreMultiplier}");
            if (positionScoreMultiplier == 0.0)
            {
                return 0.0;
            }
            baseScore *= positionScoreMultiplier;

            var missionAvg = RankSoldierByMissionAvg(ctx, soldier, missionInstance);
            candidate.RankBreakdown.Add("Mission Avg Multiplier", missionAvg);

            //Logger.LogToMemory($"\tSoldier Mission Avg Multiplier is: {missionAvg}");
            baseScore *= missionAvg;

            var totalMissionAvg = RankSoldierByTotalMissionsAvg(ctx, soldier, missionInstance);
            candidate.RankBreakdown.Add("Total Mission Avg Multiplier", totalMissionAvg);

            //Logger.LogToMemory($"\tSoldier Total Mission Avg Multiplier is: {missionAvg}");
            baseScore *= totalMissionAvg;

            var hoursAvg = RankSoldierByHoursAvg(ctx, soldier, missionInstance);
            candidate.RankBreakdown.Add("Mission Hours Avg Multiplier", hoursAvg);

            //Logger.LogToMemory($"\tSoldier Total Mission Avg Multiplier is: {missionAvg}");
            baseScore *= hoursAvg;

            var totalHoursAvg = RankSoldierByTotalHoursAvg(ctx, soldier, missionInstance);
            candidate.RankBreakdown.Add("Total Hours Avg Multiplier", totalHoursAvg);

            //Logger.LogToMemory($"\tSoldier Total Mission Avg Multiplier is: {missionAvg}");
            baseScore *= totalHoursAvg;

            //RankSoldierByTotalHoursAvg
            return baseScore;
        }


        private double RankSoldierByMissionAvg(RunContext ctx, GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            ctx.SoldiersAssignedByMissionId.TryGetValue(missionInstance.MissionId, out var assigned);
            double total = assigned?.Count ?? 0;
            if (total == 0)
            {
                return 1.0;
            }
            ctx.PerMissionAssignmentCount.TryGetValue(missionInstance.MissionId, out var missionTotal);
            var avg = missionTotal / total;
            ctx.PerMissionAssignmentsBySoldier.TryGetValue((missionInstance.MissionId, soldier.Soldier.Id), out var soldierValue);
            return StepFairnessScore(soldierValue, avg, strictBelow: false);
        }

        private double RankSoldierByTotalMissionsAvg(RunContext ctx, GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            double total = ctx.TotalAssignmentsBySoldier.Count;
            if (total == 0)
            {
                return 1.0;
            }
            var avg = ctx.TotalAssignmentsCount / total;
            ctx.TotalAssignmentsBySoldier.TryGetValue(soldier.Soldier.Id, out var soldierValue);
            return StepFairnessScore(soldierValue, avg, strictBelow: false);
        }

        private double RankSoldierByHoursAvg(RunContext ctx, GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            ctx.SoldiersAssignedByMissionId.TryGetValue(missionInstance.MissionId, out var assigned);
            double total = assigned?.Count ?? 0;
            if (total == 0)
            {
                return 1.0;
            }

            if (ctx.ScoringOptions.HoursAsHours)
            {
                ctx.PerMissionHoursSum.TryGetValue(missionInstance.MissionId, out var missionHoursTotal);
                var avg = missionHoursTotal / total;
                ctx.PerMissionHoursBySoldier.TryGetValue((missionInstance.MissionId, soldier.Soldier.Id), out var soldierHours);
                return StepFairnessScore(soldierHours, avg, strictBelow: true);
            }

            ctx.PerMissionAssignmentCount.TryGetValue(missionInstance.MissionId, out var missionTotal);
            var avgCount = missionTotal / total;
            ctx.PerMissionAssignmentsBySoldier.TryGetValue((missionInstance.MissionId, soldier.Soldier.Id), out var soldierCount);
            return StepFairnessScore(soldierCount, avgCount, strictBelow: true);
        }

        private double RankSoldierByTotalHoursAvg(RunContext ctx, GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            double total = ctx.TotalAssignmentsBySoldier.Count;
            if (total == 0)
            {
                return 1.0;
            }

            if (ctx.ScoringOptions.HoursAsHours)
            {
                var avg = ctx.TotalHoursSum / total;
                ctx.TotalHoursBySoldier.TryGetValue(soldier.Soldier.Id, out var soldierHours);
                return StepFairnessScore(soldierHours, avg, strictBelow: true);
            }

            var avgCount = ctx.TotalAssignmentsCount / total;
            ctx.TotalAssignmentsBySoldier.TryGetValue(soldier.Soldier.Id, out var soldierCount);
            return StepFairnessScore(soldierCount, avgCount, strictBelow: true);
        }

        // Preserves the legacy staircase thresholds (avg*1.05, *1.1, *1.15, *1.2 -> 0.85/0.7/0.5/0.2/0.0).
        // strictBelow=false matches RankSoldierByMissionAvg / RankSoldierByTotalMissionsAvg;
        // strictBelow=true matches RankSoldierByHoursAvg / RankSoldierByTotalHoursAvg.
        private static double StepFairnessScore(double value, double avg, bool strictBelow)
        {
            bool atOrBelow(double v, double threshold) => strictBelow ? v < threshold : v <= threshold;
            if (atOrBelow(value, avg)) return 1.0;
            if (atOrBelow(value, avg * 1.05)) return 0.85;
            if (atOrBelow(value, avg * 1.1)) return 0.7;
            if (atOrBelow(value, avg * 1.15)) return 0.5;
            if (atOrBelow(value, avg * 1.2)) return 0.2;
            return 0.0;
        }


        private double GetSoldierPositionMultiplier(RunContext ctx, Soldier soldier, MissionInstance missionInstance)
        {
            var missionId = missionInstance.Mission.Id;
            var key = (soldier.Id, missionId);
            if (ctx.PositionMultiplierCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var missionPositions = missionInstance.Mission.MissionPositions;
            var exactMatch = false;
            foreach (var soldierPosition in soldier.GetSoldierPositions())
            {
                if (missionPositions.Any(mp => mp.Position == soldierPosition))
                {
                    exactMatch = true;
                    break;
                }
            }

            double result;
            if (exactMatch)
            {
                result = 1.0;
            }
            else
            {
                var soldierPositions = GetSoldierSimilarPositions(soldier);
                var matchingCount = 0;
                foreach (var soldierPosition in soldierPositions)
                {
                    if (missionPositions.Any(mp => mp.Position == soldierPosition))
                    {
                        matchingCount++;
                    }
                }
                result = matchingCount switch
                {
                    0 => 0,
                    1 => 0.7,
                    2 => 0.8,
                    3 => 0.9,
                    _ => 1
                };
            }

            // Over-qualification damping: soldiers covering a slot only via similar
            // (non-exact) positions are rank-downed so exact-match soldiers are preferred.
            if (!exactMatch && result > 0 && ctx.ScoringOptions != null && ctx.ScoringOptions.OverQualificationDamping)
            {
                result *= ctx.ScoringOptions.OverQualificationFactor;
            }

            ctx.PositionMultiplierCache[key] = result;
            return result;
        }

        private double GetSoldierRestMultiplier(GetAvailableSoldiersModel soldier)
        {
            var beforeScore = ComputeRestSideScore(soldier.RestTimeBefore, soldier.RequiredRestBeforeThreshold);
            var afterScore = ComputeRestSideScore(soldier.RestTimeAfter, soldier.RequiredRestAfterThreshold);

            if (beforeScore == 0.0 || afterScore == 0.0) return 0.0;
            return Math.Min(beforeScore, afterScore);
        }

        private static double ComputeRestSideScore(int? restTime, int? requiredRestThreshold)
        {
            var t = requiredRestThreshold ?? 8;
            if (t == 0) return 1.0;

            var rest = restTime ?? int.MaxValue;
            if (rest == 0) return 0.0;
            if (rest == int.MaxValue) return 1.0;
            if (rest >= (int)(t * 1.5)) return 0.9;
            if (rest >= (int)(t * 1.25)) return 0.8;
            if (rest >= t) return 0.7;
            return 0.1;
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

        #region Interactive Auto-Assign

        public InteractiveAutoAssignStep StartInteractive(
            DateTime? from,
            DateTime? to,
            List<int>? missions,
            List<int>? soldiers,
            AutoAssignScoringOptions? scoringOptions,
            InteractivePauseOn pauseOn,
            bool showAllSoldiersOnPause)
        {
            if (_batchRunning)
            {
                throw new InvalidOperationException("Cannot start interactive session while batch auto-assign is running.");
            }
            if (_interactiveSessions.Count >= MaxConcurrentSessions)
            {
                throw new InvalidOperationException($"Maximum concurrent interactive sessions ({MaxConcurrentSessions}) reached.");
            }

            var ctx = new RunContext();
            ctx.ScoringOptions = scoringOptions ?? new AutoAssignScoringOptions();
            ReloadCache();

            List<Soldier> selectedSoldiers;
            if (soldiers == null || soldiers.Count == 0)
            {
                selectedSoldiers = _soldiersCache.GetDBSoldiers();
                soldiers = selectedSoldiers.Select(s => s.Id).ToList();
            }
            else
            {
                selectedSoldiers = _soldiersCache.GetDBSoldiers()
                    .Where(s => soldiers.Any(sol => sol == s.Id))
                    .ToList();
            }
            ctx.SelectedSoldierIds = soldiers;

            List<Mission> selectedMissionAssignments;
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
            ctx.SelectedMissionAssignments = selectedMissionAssignments;

            var flatInstances = selectedMissionAssignments.SelectMany(m => m.MissionInstances).ToList();
            var absoluteFrom = from ?? (flatInstances.Any() ? flatInstances.Min(mi => mi.FromTime) : DateTime.MinValue);
            var absoluteTo = to ?? (flatInstances.Any() ? flatInstances.Max(mi => mi.FromTime) : DateTime.MaxValue);

            ctx.AllInstances = GetMissionInstances(absoluteFrom, absoluteTo, selectedMissionAssignments);

            ctx.OriginalSoldiersByInstanceId = ctx.AllInstances.ToDictionary(
                mi => mi.Id,
                mi => (mi.Soldiers ?? new List<SoldierMission>()).ToList());

            // Force single schedule for interactive mode
            var startingMissions = SelectStartingMissions(selectedMissionAssignments, ctx.AllInstances, 1);
            if (startingMissions.Count == 0)
            {
                return new InteractiveAutoAssignStep
                {
                    SessionId = "",
                    Status = InteractiveAutoAssignStatus.Completed,
                    CurrentIndex = 0,
                    TotalInstancesCount = 0,
                    Result = new AssignmentValidationModel
                    {
                        From = absoluteFrom,
                        To = absoluteTo,
                        ValidInstancesCount = 0,
                        TotalInstancesCount = 0,
                        FaultyInstances = new(),
                        ValidInstances = new(),
                        SkippedInstances = new(),
                        SkippedInstancesCount = 0
                    }
                };
            }
            var startingMission = startingMissions[0];

            RestoreInstanceSoldiers(ctx);
            BuildAvailabilityIndex(ctx, selectedSoldiers);
            PrimeAggregates(ctx);
            ctx.JitterSeed = ctx.ScoringOptions.JitterSeed ?? 0;

            var orderedInstances = ctx.AllInstances
                .OrderBy(mi => mi.MissionId == startingMission.Id ? 0 : 1)
                .ThenBy(mi => mi.FromTime)
                .ThenBy(mi => mi.MissionId)
                .ThenBy(mi => mi.Id)
                .ToList();

            ctx.CandidatesPerInstance = new Dictionary<int, List<CandidateSoldierAssignment>>();

            var sessionId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            var session = new InteractiveAutoAssignSession
            {
                SessionId = sessionId,
                CreatedAt = now,
                LastTouchedAt = now,
                Context = ctx,
                OrderedInstanceIds = orderedInstances.Select(mi => mi.Id).ToList(),
                CurrentIndex = 0,
                StartingMission = startingMission,
                SelectedSoldiers = selectedSoldiers,
                AbsoluteFrom = absoluteFrom,
                AbsoluteTo = absoluteTo,
                PauseOn = pauseOn,
                ShowAllSoldiersOnPause = showAllSoldiersOnPause,
                CacheSnapshotTime = now
            };

            _interactiveSessions[sessionId] = session;
            Logger.Log($"Interactive session {sessionId} started with {orderedInstances.Count} instances.");

            return AdvanceLoop(session);
        }

        public InteractiveAutoAssignStep ContinueInteractive(string sessionId, List<CandidatePickDto> picks, bool skipInstance)
        {
            if (!_interactiveSessions.TryGetValue(sessionId, out var session))
            {
                throw new KeyNotFoundException($"Interactive session '{sessionId}' not found or expired.");
            }

            session.Lock.Wait();
            try
            {
                session.LastTouchedAt = DateTime.UtcNow;
                var ctx = session.Context;
                var instanceId = session.OrderedInstanceIds[session.CurrentIndex];
                var instance = ctx.AllInstances.First(mi => mi.Id == instanceId);

                if (skipInstance)
                {
                    LogInteractiveAction(sessionId, instanceId, "Skipped", null);
                    ctx.CandidatesPerInstance[instanceId] = ctx.CandidatesPerInstance.ContainsKey(instanceId)
                        ? ctx.CandidatesPerInstance[instanceId] : new List<CandidateSoldierAssignment>();
                    session.CurrentIndex++;
                    return AdvanceLoop(session);
                }

                // Build assignments from user picks with auto-demotion
                var assignments = new List<SoldierMission>();
                foreach (var pick in picks)
                {
                    var soldier = _soldiersCache.GetDbSoldierById(pick.SoldierId);
                    if (soldier == null) continue;

                    var missionPosition = instance.Mission.MissionPositions
                        .FirstOrDefault(mp => mp.Id == pick.MissionPositionId);
                    if (missionPosition == null)
                    {
                        // Auto-demotion: find any valid position for this soldier
                        var soldierPositions = GetSoldierSimilarPositions(soldier);
                        missionPosition = instance.Mission.MissionPositions
                            .Where(mp => soldierPositions.Contains(mp.Position))
                            .OrderByDescending(mp => mp.Position)
                            .FirstOrDefault();
                    }
                    if (missionPosition == null) continue;

                    assignments.Add(new SoldierMission
                    {
                        Id = 0,
                        SoldierId = pick.SoldierId,
                        MissionInstanceId = instanceId,
                        MissionPositionId = missionPosition.Id,
                        MissionInstance = instance,
                        MissionPosition = missionPosition,
                        Soldier = soldier
                    });
                }

                CommitAssignment(ctx, instance, assignments);

                if (!ctx.CandidatesPerInstance.ContainsKey(instanceId))
                {
                    ctx.CandidatesPerInstance[instanceId] = new List<CandidateSoldierAssignment>();
                }

                LogInteractiveAction(sessionId, instanceId, "UserPicked",
                    JsonConvert.SerializeObject(picks));

                session.CurrentIndex++;
                return AdvanceLoop(session);
            }
            finally
            {
                session.Lock.Release();
            }
        }

        public void CancelInteractive(string sessionId)
        {
            if (_interactiveSessions.TryRemove(sessionId, out _))
            {
                Logger.Log($"Interactive session {sessionId} cancelled.");
            }
        }

        private InteractiveAutoAssignStep AdvanceLoop(InteractiveAutoAssignSession session)
        {
            var ctx = session.Context;
            var instanceDic = ctx.AllInstances.ToDictionary(mi => mi.Id);

            while (session.CurrentIndex < session.OrderedInstanceIds.Count)
            {
                var instanceId = session.OrderedInstanceIds[session.CurrentIndex];
                if (!instanceDic.TryGetValue(instanceId, out var instance))
                {
                    session.CurrentIndex++;
                    continue;
                }

                var originalSoldiers = ctx.OriginalSoldiersByInstanceId.TryGetValue(instanceId, out var orig) ? orig : [];
                if (originalSoldiers.Count > 0)
                {
                    ctx.SkippedInstanceIds.Add(instanceId);
                    if (!ctx.CandidatesPerInstance.ContainsKey(instanceId))
                    {
                        ctx.CandidatesPerInstance[instanceId] = new List<CandidateSoldierAssignment>();
                    }
                    Logger.LogToMemory($"Interactive: Instance {instanceId} already has {originalSoldiers.Count} accepted assignment(s), skipping.", LogLevel.Info);
                    session.CurrentIndex++;
                    continue;
                }

                if (IsInstanceFilledInMemory(instance))
                {
                    if (!ctx.CandidatesPerInstance.ContainsKey(instanceId))
                    {
                        ctx.CandidatesPerInstance[instanceId] = new List<CandidateSoldierAssignment>();
                    }
                    session.CurrentIndex++;
                    continue;
                }

                var candidateSoldiers = ComputeAndRankCandidates(ctx, instance);

                var allCandidatesForPause = session.ShowAllSoldiersOnPause
                    ? ComputeAllCandidatesIncludingZeroRank(ctx, instance)
                    : candidateSoldiers;

                var (assignments, missingPositions, isFilled) = TryFillInstance(ctx, candidateSoldiers, instance);

                bool shouldPause = session.PauseOn == InteractivePauseOn.EveryInstance || !isFilled;

                if (shouldPause)
                {
                    ctx.CandidatesPerInstance[instanceId] = candidateSoldiers;

                    var mi = instance.ToBL(false, true);
                    var pendingView = new PendingInstanceView
                    {
                        Instance = new CandidateMissionInstance
                        {
                            Id = mi.Id,
                            FromTime = mi.FromTime,
                            ToTime = mi.ToTime,
                            SoldierMissions = mi.SoldierMissions,
                            MissingPositions = missingPositions.Any() ? missingPositions : null,
                            Candidates = allCandidatesForPause
                                .Select(c => new CandidateSoldierAssignmentVM
                                {
                                    Soldier = c.Soldier.ToBL(false, false),
                                    MissionsAssignedTo = c.MissionsAssignedTo,
                                    Rank = c.Rank,
                                    RankBreakdown = c.RankBreakdown
                                })
                                .ToList()
                        },
                        MaxSelections = instance.Mission.CommandersRequired + instance.Mission.SoldiersRequired,
                        CommandersRequired = instance.Mission.CommandersRequired,
                        SoldiersRequired = instance.Mission.SoldiersRequired
                    };

                    return new InteractiveAutoAssignStep
                    {
                        SessionId = session.SessionId,
                        Status = InteractiveAutoAssignStatus.Paused,
                        CurrentIndex = session.CurrentIndex,
                        TotalInstancesCount = session.OrderedInstanceIds.Count,
                        Pending = pendingView
                    };
                }

                // Auto-filled successfully
                CommitAssignment(ctx, instance, assignments);
                ctx.CandidatesPerInstance[instanceId] = candidateSoldiers;
                LogInteractiveAction(session.SessionId, instanceId, "AutoFilled", null);
                session.CurrentIndex++;
            }

            // All instances processed — finalize
            var result = FinalizeScheduleResult(ctx, session.StartingMission, session.AbsoluteFrom, session.AbsoluteTo);
            result.IsBestCandidate = true;

            _interactiveSessions.TryRemove(session.SessionId, out _);
            Logger.Log($"Interactive session {session.SessionId} completed.");

            return new InteractiveAutoAssignStep
            {
                SessionId = session.SessionId,
                Status = InteractiveAutoAssignStatus.Completed,
                CurrentIndex = session.OrderedInstanceIds.Count,
                TotalInstancesCount = session.OrderedInstanceIds.Count,
                Result = result
            };
        }

        private List<CandidateSoldierAssignment> ComputeAllCandidatesIncludingZeroRank(RunContext ctx, MissionInstance instance)
        {
            var availableSoldiers = ComputeAvailableSoldiers(ctx, instance, ctx.SelectedSoldierIds);
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
                candidate.Rank = RankSoldierForPosition(ctx, soldier, instance, candidate);
                candidateSoldiers.Add(candidate);
            }

            return candidateSoldiers
                .OrderByDescending(cs => cs.Rank)
                .ToList();
        }

        private void LogInteractiveAction(string sessionId, int instanceId, string action, string? picksJson)
        {
            try
            {
                using var db = new ShabzakDB();
                db.InteractiveAutoAssignLogs.Add(new InteractiveAutoAssignLog
                {
                    SessionId = sessionId,
                    MissionInstanceId = instanceId,
                    Action = action,
                    PicksJson = picksJson,
                    Timestamp = DateTime.UtcNow
                });
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to log interactive action: {ex.Message}");
            }
        }

        #endregion
    }
}
