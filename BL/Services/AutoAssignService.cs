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
    /// <summary>
    /// Core scheduling engine for automatic soldier-to-mission assignment.
    /// Supports batch mode (multiple candidate schedules) and interactive mode (step-by-step with user overrides).
    /// Uses a multi-factor ranking algorithm: rest time, position match, per-mission fairness, and total fairness.
    /// </summary>
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

        /// <summary>
        /// Initializes the auto-assign service with required caches and services.
        /// Starts a background timer to clean up idle interactive sessions every 5 minutes.
        /// </summary>
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
            // Goal: Promote a candidate schedule from temporary storage (SoldierMissionCandidates)
            // into actual assignments (SoldierMission), then clean up all candidates.

            using var db = new ShabzakDB();

            // Step 1: Load all candidate records and filter to the accepted schedule by GUID.
            var allCandidates = db.SoldierMissionCandidates.ToList();
            var candidates = allCandidates
                .Where(sm => sm.CandidateId == candidateId)
                .ToList();

            // Step 2: Convert each candidate into a real SoldierMission assignment and persist.
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

            // Step 3: Recalculate IsFilled flag for each affected instance
            // (checks if assigned count meets required positions).
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

            // Step 4: Remove ALL candidate records (not just the accepted one)
            // since only one schedule can be accepted, and reload the missions cache.
            var reload = MissionsCache.ReloadAsync();
            db.SoldierMissionCandidates.RemoveRange(allCandidates);
            db.SaveChanges();
            reload.Wait();
            return _missionsCache.GetMissions();
        }

        private void ReloadCache()
        {
            // Goal: Refresh all in-memory lookup structures from the database so the
            // auto-assign algorithm works with the latest assignment state.

            using var db = new ShabzakDB();

            // Build a two-level lookup: MissionId → SoldierId → [SoldierMission]
            // Used to quickly find how many times a soldier has been assigned to a specific mission.
            soldierMissionsByMissionIdBySoldierId = db.SoldierMission
                .GroupBy(sm => sm.MissionInstance.MissionId)
                .ToDictionary(k => k.Key, v => v.GroupBy(s => s.SoldierId)
                                                .ToDictionary(k => k.Key, v => v.ToList()));

            // Build a dictionary of all mission instances with their soldier assignments and positions,
            // keyed by instance ID for O(1) lookups.
            missionInstanceDic = db.MissionInstances
                .Include(mi => mi.Soldiers)
                .ThenInclude(s => s.MissionPosition)
                .ToList()
                .GroupBy(mi => mi.Id)
                .ToDictionary(k => k.Key, v => v.First());

            // Load all missions with their instances and required positions, decrypting PII fields.
            missionAssignments = db.Missions
                .Include(m => m.MissionInstances)
                .Include(m => m.MissionPositions)
                .ToList()
                .Select(m => m.Decrypt())
                .ToList();
            
            // Count total assignments per soldier across all missions — used for global fairness scoring.
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
            // Goal: Public entry point for batch auto-assign. Ensures mutual exclusion with
            // interactive sessions (only one mode can run at a time), then delegates to AutoAssignInternal.
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
            // Goal: Generate multiple candidate schedules (each starting from a different mission)
            // and return them all so the user can pick the best one.
            //
            // Algorithm overview:
            //   1. Reload caches and resolve which soldiers/missions/instances participate.
            //   2. Select up to N "starting missions" (prioritized by demand × instance count).
            //   3. For each starting mission, run a full schedule that assigns soldiers to every instance.
            //   4. Score each schedule by: most valid instances, lowest evenness score (fairness), fewest faults.
            //   5. Mark the best schedule and return all candidates.

            var watch = new Stopwatch();
            watch.Start();

            // --- Phase 1: Initialize context and resolve filters ---
            var ctx = new RunContext();
            ctx.ScoringOptions = scoringOptions ?? new AutoAssignScoringOptions();
            ReloadCache();

            // Resolve soldier pool: use all soldiers if no filter provided.
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

            // Resolve mission pool: use all missions if no filter provided.
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

            // Determine the scheduling time window — fall back to the full range of existing instances.
            var flatInstances = selectedMissionAssignments.SelectMany(m => m.MissionInstances).ToList();
            var absoluteFrom = from ?? (flatInstances.Any() ? flatInstances.Min(mi => mi.FromTime) : DateTime.MinValue);
            var absoluteTo = to ?? (flatInstances.Any() ? flatInstances.Max(mi => mi.FromTime) : DateTime.MaxValue);

            // Collect all mission instances within the scheduling window.
            ctx.AllInstances = GetMissionInstances(absoluteFrom, absoluteTo, selectedMissionAssignments);

            // Snapshot the original (already-accepted) soldier assignments per instance
            // so we can restore state between schedule runs.
            ctx.OriginalSoldiersByInstanceId = ctx.AllInstances.ToDictionary(
                mi => mi.Id,
                mi => (mi.Soldiers ?? new List<SoldierMission>()).ToList());

            // --- Phase 2: Select starting missions for schedule diversity ---
            var schedulesToRun = SelectStartingMissions(selectedMissionAssignments, ctx.AllInstances, maxSchedules);

            // --- Phase 3: Run each schedule independently ---
            var results = new List<AssignmentValidationModel>();
            for (var i = 0; i < schedulesToRun.Count; i++)
            {
                var scheduleWatch = Stopwatch.StartNew();
                var result = RunSchedule(ctx, schedulesToRun[i], i, selectedSoldiers, absoluteFrom, absoluteTo);
                scheduleWatch.Stop();
                Logger.Log($"AutoAssign schedule #{i} (start={result.StartingMissionId}) took {scheduleWatch.Elapsed.TotalSeconds:F2}s");
                results.Add(result);
            }

            // --- Phase 4: Rank schedules and mark the best one ---
            // Best = most fully-filled instances, then fairest distribution, then fewest faults.
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

        /// <summary>
        /// Selects which missions to use as starting points for schedule generation.
        /// Prioritizes missions with the most required soldiers × instance count.
        /// Returns up to <paramref name="maxSchedules"/> missions (default 4).
        /// </summary>
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

        /// <summary>
        /// Executes a single schedule attempt starting from a specific mission.
        /// Orders instances (starting mission first, then chronologically), computes candidates,
        /// fills each instance, then validates and scores the result.
        /// </summary>
        private AssignmentValidationModel RunSchedule(
            RunContext ctx,
            Mission startingMission,
            int scheduleIndex,
            List<Soldier> selectedSoldiers,
            DateTime absoluteFrom,
            DateTime absoluteTo)
        {
            // Goal: Execute one complete scheduling attempt. The starting mission's instances
            // are processed first (giving it priority), then remaining instances chronologically.
            //
            // Steps:
            //   1. Restore instance soldiers to the original state (undo prior schedule's in-memory changes).
            //   2. Rebuild the availability index (time intervals per soldier) and prime fairness aggregates.
            //   3. Order instances: starting mission first, then by time.
            //   4. For each instance: skip if already assigned, compute candidates, rank, fill, commit.
            //   5. Finalize: validate results, compute evenness, persist candidates to DB.

            // Step 1: Reset in-memory state so this schedule starts fresh.
            RestoreInstanceSoldiers(ctx);
            // Step 2: Build soldier availability (vacations + existing assignments) and fairness counters.
            BuildAvailabilityIndex(ctx, selectedSoldiers);
            PrimeAggregates(ctx);

            ctx.JitterSeed = ctx.ScoringOptions.JitterSeed ?? scheduleIndex;

            // Step 3: Order instances — starting mission gets priority (sorted first),
            // which means the most-demanding mission gets the best soldier picks.
            var orderedInstances = ctx.AllInstances
                .OrderBy(mi => mi.MissionId == startingMission.Id ? 0 : 1)
                .ThenBy(mi => mi.FromTime)
                .ThenBy(mi => mi.MissionId)
                .ThenBy(mi => mi.Id)
                .ToList();

            ctx.CandidatesPerInstance = new Dictionary<int, List<CandidateSoldierAssignment>>();
            ctx.SkippedInstanceIds = new HashSet<int>();

            // Step 4: Iterate through each instance and assign soldiers.
            foreach (var instance in orderedInstances)
            {
                Logger.LogToMemory($"[Sch#{scheduleIndex} start={startingMission.Id}] Assigning to instance: {instance.Id}", LogLevel.Info);

                // Skip instances that already have accepted (real) assignments from the DB.
                var originalSoldiers = ctx.OriginalSoldiersByInstanceId.TryGetValue(instance.Id, out var orig) ? orig : [];
                if (originalSoldiers.Count > 0)
                {
                    ctx.SkippedInstanceIds.Add(instance.Id);
                    ctx.CandidatesPerInstance.Add(instance.Id, []);
                    Logger.LogToMemory($"Instance {instance.Id} already has {originalSoldiers.Count} accepted assignment(s), skipping.", LogLevel.Info);
                    continue;
                }

                // Skip instances that are already fully filled (all positions occupied).
                if (IsInstanceFilledInMemory(instance))
                {
                    ctx.CandidatesPerInstance.Add(instance.Id, []);
                    Logger.LogToMemory($"Instance already filled, skipping.", LogLevel.Info);
                    continue;
                }

                // Compute ranked candidates, fill positions, and commit assignments in memory.
                var candidateSoldiers = ComputeAndRankCandidates(ctx, instance);
                ctx.CandidatesPerInstance.Add(instance.Id, candidateSoldiers);

                LogCandidates(candidateSoldiers, instance, scheduleIndex, startingMission.Id);

                var (assignments, _, isFilled) = TryFillInstance(ctx, candidateSoldiers, instance);
                CommitAssignment(ctx, instance, assignments);
            }

            // Step 5: Validate all assignments, compute evenness score, persist to DB.
            return FinalizeScheduleResult(ctx, startingMission, absoluteFrom, absoluteTo);
        }

        /// <summary>
        /// Computes and ranks all available soldiers for a given mission instance.
        /// Filters out soldiers with rank 0 (unavailable due to overlap or no position match).
        /// Returns candidates sorted by descending rank.
        /// </summary>
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

        /// <summary>
        /// Attempts to fill a mission instance with the best-ranked soldiers.
        /// Two-pass approach: first tries exact position matches, then falls back to similar positions.
        /// Applies optional deterministic jitter for schedule diversity.
        /// Returns the assignments, any unfilled positions, and whether the instance is fully filled.
        /// </summary>
        private (List<SoldierMission> Assignments, Dictionary<Translators.Models.Position, int> MissingPositions, bool IsFilled) TryFillInstance(
            RunContext ctx, List<CandidateSoldierAssignment> soldiers, MissionInstance missionInstance)
        {
            // Goal: Assign the best-ranked soldiers to a mission instance's required positions.
            //
            // Algorithm (two-pass greedy matching):
            //   Pass 1 (Strict): For each soldier (best rank first), try to match them to a
            //          position slot that still has capacity. Only assigns if the soldier's
            //          similar positions directly match a remaining slot. Decrements slot count.
            //   Pass 2 (Relaxed): If still under-filled, iterate again over unassigned soldiers.
            //          This time, assign to ANY matching position even if that position's count
            //          is already 0 — instead, deduct from the generic Simple or ClassCommander pool.
            //          This allows over-qualified soldiers to fill generic slots.
            //   Random dropout: Without deterministic jitter, there's a 20% chance of dropping
            //          the last assignment to introduce schedule diversity across runs.

            // Annotate each soldier with their total assignment count for tiebreaking.
            foreach (var soldier in soldiers)
            {
                ctx.TotalAssignmentsBySoldier.TryGetValue(soldier.Soldier.Id, out var total);
                soldier.MissionsAssignedTo = total;
            }

            // Apply deterministic jitter: a small pseudo-random perturbation to each soldier's rank
            // to produce different (but reproducible) schedules across schedule variants.
            if (ctx.ScoringOptions.DeterministicJitter && ctx.ScoringOptions.JitterEpsilon != 0)
            {
                foreach (var soldier in soldiers)
                {
                    var r = DeterministicRandom(ctx.JitterSeed, soldier.Soldier.Id, missionInstance.Id);
                    var jitter = (r - 0.5) * 2.0 * ctx.ScoringOptions.JitterEpsilon;
                    soldier.Rank *= (1 + jitter);
                }
            }

            // Re-sort after jitter: highest rank first, tiebreak by fewest total assignments (fairness).
            soldiers = soldiers
                .OrderByDescending(cs => cs.Rank)
                .ThenBy(cs => cs.MissionsAssignedTo)
                .ToList();

            // Build a mutable counter for each required position in this mission.
            // Positions are ordered descending (commanding positions first) to prioritize filling
            // high-rank positions before generic ones.
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

            // --- Pass 1 (Strict): Exact/similar position match with available slot capacity ---
            var assignments = new List<SoldierMission>();
            foreach (var soldier in soldiers)
            {
                // Stop if all positions are filled.
                if (!missionPositions.Any(mp => mp.Count > 0))
                {
                    break;
                }
                // Get this soldier's similar positions (expanded via PositionHelper),
                // ordered descending so commanding positions are tried first.
                var soldierPositions = GetSoldierSimilarPositions(soldier.Soldier)
                    .OrderDescending()
                    .ToList();
                var soldierAssigned = false;
                foreach (var soldierPosition in soldierPositions)
                {
                    // Find a position slot that matches and still has capacity.
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

            // --- Pass 2 (Relaxed): Fill remaining slots even if position count is exhausted ---
            // This handles cases where not enough exact-match soldiers exist.
            // Soldiers are assigned to their best matching position, but the capacity is deducted
            // from the generic Simple (or ClassCommander) pool instead.
            if (assignments.Count < (missionInstance.Mission.CommandersRequired + missionInstance.Mission.SoldiersRequired))
            {
                foreach (var soldier in soldiers)
                {
                    // Skip soldiers already assigned in Pass 1.
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
                        // In Pass 2, accept any matching position (even if its own count is 0).
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
                            // Deduct capacity from the generic pool (Simple first, then ClassCommander)
                            // since this soldier is filling a slot via cross-position match.
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

            // Random dropout: without deterministic jitter, 20% chance to drop the last assignment.
            // This introduces diversity across schedule variants by occasionally under-filling,
            // which forces subsequent instances to use different soldiers.
            if (!ctx.ScoringOptions.DeterministicJitter && assignments.Count > 1)
            {
                var randNum = rand.Next(0, 101);
                if (randNum <= 20)
                {
                    assignments = assignments.Take(assignments.Count - 1).ToList();
                }
            }

            // Check if the instance is fully filled (all required positions covered).
            var required = missionInstance.Mission.MissionPositions?.Sum(mp => mp.Count) ?? 0;
            var isFilled = required > 0 && assignments.Count >= required;

            // If not fully filled, compute which positions are still missing for reporting.
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

        /// <summary>
        /// Commits soldier assignments to a mission instance in memory.
        /// Updates tracking aggregates (total assignments, hours, intervals) for fairness scoring.
        /// </summary>
        private void CommitAssignment(RunContext ctx, MissionInstance missionInstance, List<SoldierMission> assignments)
        {
            // Goal: Persist assignments in memory and update all tracking structures so that
            // subsequent instance assignments reflect the updated state (availability, fairness counts).

            missionInstance.Soldiers = assignments;
            missionInstance.IsFilled = IsInstanceFilledInMemory(missionInstance);

            var hours = InstanceHours(missionInstance);
            foreach (var assignment in assignments)
            {
                // Update fairness counters (per-mission and total assignment/hours counts).
                TrackAssignment(ctx, assignment.SoldierId, missionInstance.MissionId, hours);
                // Insert this assignment's time interval into the soldier's schedule
                // so future availability checks detect the new commitment.
                AddIntervalForSoldier(ctx, assignment.SoldierId, missionInstance.FromTime, missionInstance.ToTime, missionInstance.Mission?.RequiredRestAfter);
            }
        }

        /// <summary>
        /// Finalizes a schedule run: validates all instance assignments, computes evenness score,
        /// persists candidate assignments to DB (SoldierMissionCandidate), and writes results to JSON file.
        /// </summary>
        private AssignmentValidationModel FinalizeScheduleResult(
            RunContext ctx, Mission startingMission, DateTime absoluteFrom, DateTime absoluteTo)
        {
            // Goal: After all instances have been processed, validate the full schedule,
            // persist it as a candidate in the DB, and save a JSON snapshot to disk.

            // Step 1: Collect all assignments across all instances in this schedule.
            var scheduleAssignments = ctx.AllInstances
                .SelectMany(mi => mi.Soldiers ?? new List<SoldierMission>())
                .ToList();

            // Step 2: Validate — categorize each instance as valid, faulty, or skipped.
            var ret = ValidateAssignments(ctx, absoluteFrom, absoluteTo, ctx.CandidatesPerInstance);
            ret.StartingMissionId = startingMission.Id;
            ret.StartingMissionName = startingMission.Name;
            // Step 3: Compute evenness score (coefficient of variation) across soldiers.
            ret.EvennessScore = ComputeEvennessScore(ctx);

            // Step 4: Persist all assignments as SoldierMissionCandidate records under a single GUID.
            // These are temporary until the user accepts one schedule.
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

            // Step 5: Write a JSON file for offline inspection / debugging.
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
            // Goal: Reset all instance assignments back to their original DB state.
            // This is called before each schedule run so that multiple schedule variants
            // all start from the same baseline (not accumulating assignments from prior runs).
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

        /// <summary>
        /// Builds a per-soldier time-interval index for availability checking.
        /// Includes vacation periods, existing mission assignments (both in-window and external),
        /// sorted chronologically for efficient overlap detection.
        /// </summary>
        private void BuildAvailabilityIndex(RunContext ctx, List<Soldier> selectedSoldiers)
        {
            // Goal: Build a per-soldier timeline of busy intervals (vacations + existing assignments).
            // This index is used by ComputeAvailableSoldiers to quickly check for time overlaps
            // and calculate rest-time gaps when ranking soldiers for a given instance.
            //
            // Three sources of intervals are merged:
            //   1. Vacations (non-denied) — soldier is completely unavailable.
            //   2. In-window assignments — missions already assigned within the scheduling window.
            //   3. External assignments — missions outside the window that still affect rest times.
            // Finally, intervals are sorted chronologically for efficient overlap detection.

            ctx.SoldierIntervalsById = new Dictionary<int, List<(DateTime From, DateTime To, int? RequiredRestAfter)>>();

            // Source 1: Add vacation intervals (non-denied vacations block the soldier entirely).
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

            // Source 2: Add intervals from existing assignments within the scheduling window.
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

            // Source 3: Add external assignments (outside the window) that may still
            // affect rest-time calculations for instances near the window boundaries.
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

            // Sort all intervals chronologically for efficient binary-search-style overlap checking.
            foreach (var key in ctx.SoldierIntervalsById.Keys.ToList())
            {
                ctx.SoldierIntervalsById[key] = ctx.SoldierIntervalsById[key]
                    .OrderBy(t => t.From)
                    .ToList();
            }
        }

        private void AddIntervalForSoldier(RunContext ctx, int soldierId, DateTime fromTime, DateTime toTime, int? requiredRestAfter = null)
        {
            // Goal: Insert a new busy interval into the soldier's sorted interval list.
            // Uses linear scan to find the correct insertion point (maintaining chronological order)
            // so that future overlap checks remain efficient.
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

        /// <summary>
        /// Initializes per-soldier and per-mission assignment counters from existing assignments.
        /// These aggregates are used by the fairness scoring functions during ranking.
        /// </summary>
        private void PrimeAggregates(RunContext ctx)
        {
            // Goal: Initialize all fairness-tracking counters from existing (pre-assigned) data.
            // These counters track how many assignments and hours each soldier has accumulated,
            // both per-mission and globally. They are used by the fairness scoring functions
            // (RankSoldierByMissionAvg, RankSoldierByTotalMissionsAvg, etc.) to ensure
            // equitable distribution of duty across soldiers.

            ctx.TotalAssignmentsBySoldier = new Dictionary<int, int>();
            ctx.TotalHoursBySoldier = new Dictionary<int, double>();
            ctx.SoldiersAssignedByMissionId = new Dictionary<int, HashSet<int>>();
            ctx.PerMissionAssignmentsBySoldier = new Dictionary<(int MissionId, int SoldierId), int>();
            ctx.PerMissionHoursBySoldier = new Dictionary<(int MissionId, int SoldierId), double>();
            ctx.PerMissionAssignmentCount = new Dictionary<int, int>();
            ctx.PerMissionHoursSum = new Dictionary<int, double>();
            ctx.TotalAssignmentsCount = 0;
            ctx.TotalHoursSum = 0;

            // Seed counters from all existing assignments in the scheduling window.
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
            // Goal: Increment all fairness tracking counters when a soldier is assigned to an instance.
            // This updates both global and per-mission counters so the ranking algorithm
            // can compute accurate averages for fairness scoring.

            // Global counters: total assignments and hours per soldier.
            ctx.TotalAssignmentsBySoldier.TryGetValue(soldierId, out var totalCount);
            ctx.TotalAssignmentsBySoldier[soldierId] = totalCount + 1;

            ctx.TotalHoursBySoldier.TryGetValue(soldierId, out var totalHours);
            ctx.TotalHoursBySoldier[soldierId] = totalHours + hours;

            ctx.TotalAssignmentsCount++;
            ctx.TotalHoursSum += hours;

            // Track which soldiers have been assigned to each mission (for per-mission average calculation).
            if (!ctx.SoldiersAssignedByMissionId.TryGetValue(missionId, out var set))
            {
                set = new HashSet<int>();
                ctx.SoldiersAssignedByMissionId[missionId] = set;
            }
            set.Add(soldierId);

            // Per-mission counters: assignments and hours for this specific (mission, soldier) pair.
            var key = (missionId, soldierId);
            ctx.PerMissionAssignmentsBySoldier.TryGetValue(key, out var perCount);
            ctx.PerMissionAssignmentsBySoldier[key] = perCount + 1;

            ctx.PerMissionHoursBySoldier.TryGetValue(key, out var perHours);
            ctx.PerMissionHoursBySoldier[key] = perHours + hours;

            // Per-mission totals (across all soldiers) — used as the denominator for averages.
            ctx.PerMissionAssignmentCount.TryGetValue(missionId, out var missionCount);
            ctx.PerMissionAssignmentCount[missionId] = missionCount + 1;

            ctx.PerMissionHoursSum.TryGetValue(missionId, out var missionHours);
            ctx.PerMissionHoursSum[missionId] = missionHours + hours;
        }

        /// <summary>
        /// Computes the coefficient of variation (stddev/mean) of assignment counts across all selected soldiers.
        /// Lower scores indicate more even distribution. Used to rank candidate schedules.
        /// </summary>
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

        /// <summary>
        /// Determines which soldiers are available for a specific mission instance.
        /// Checks for time overlaps with existing assignments/vacations and computes rest-time metrics.
        /// Returns all soldiers (including those with overlap) with their availability data.
        /// </summary>
        private List<GetAvailableSoldiersModel> ComputeAvailableSoldiers(RunContext ctx, MissionInstance instance, List<int> soldierIds)
        {
            // Goal: For a given mission instance, determine each soldier's availability status.
            // For each soldier, scan their interval list to find:
            //   - Whether any existing commitment overlaps the instance (makes them unavailable).
            //   - The minimum rest gap before the instance (hours between last commitment end and instance start).
            //   - The minimum rest gap after the instance (hours between instance end and next commitment start).
            // These metrics feed into the rest-time multiplier during ranking.

            var startTime = instance.FromTime;
            var endTime = instance.ToTime;
            var ret = new List<GetAvailableSoldiersModel>();

            foreach (var soldierId in soldierIds)
            {
                var dbSoldier = _soldiersCache.GetDbSoldierById(soldierId);
                if (dbSoldier == null) continue;

                // Skip soldiers already assigned to this instance.
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
                    // Scan all intervals to find overlaps and nearest rest gaps.
                    foreach (var (iFrom, iTo, iRequiredRestAfter) in intervals)
                    {
                        // Direct time overlap → soldier is busy during this instance.
                        if (OverlappingTimes(startTime, endTime, iFrom, iTo))
                        {
                            overlap = true;
                            break;
                        }
                        // Interval ends before instance starts → compute rest-before gap.
                        // Keep the smallest gap (closest prior commitment).
                        if (iTo <= startTime)
                        {
                            var diff = (int)(startTime - iTo).TotalHours;
                            if (restBefore == null || diff < restBefore)
                            {
                                restBefore = diff;
                                restBeforeThreshold = iRequiredRestAfter;
                            }
                        }
                        // Interval starts after instance ends → compute rest-after gap.
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

            // Sort each soldier's positions descending (commanding positions first)
            // for consistent ordering in downstream position-matching logic.
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
            // Standard interval overlap check: two intervals [aFrom,aTo) and [bFrom,bTo) overlap
            // if and only if each one starts before the other ends.
            return aFrom < bTo && bFrom < aTo;
        }



        public AssignmentValidationModel ValidateAssignments(RunContext ctx, DateTime? from, DateTime to, Dictionary<int, List<CandidateSoldierAssignment>> candidates)
        {
            // Goal: Classify every instance in the schedule into three buckets:
            //   - Valid: all required positions are filled.
            //   - Faulty: some positions are missing (includes which positions are missing).
            //   - Skipped: instance was already assigned (accepted) before this auto-assign run.
            // Also attaches the ranked candidate list to each instance for transparency.

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
            // Goal: Compute which positions are still unfilled for a partially-assigned instance.
            // Algorithm:
            //   1. Build a map of required positions → count from the mission definition.
            //   2. Subtract one for each assigned soldier's position.
            //   3. Return only positions with count > 0 (still needed).

            // Step 1: Build required-positions map from the mission definition.
            var positions = new Dictionary<Translators.Models.Position, int>();
            foreach (var mp in mission.MissionPositions ?? [])
            {
                var key = (Translators.Models.Position)(int)mp.Position;
                if (!positions.ContainsKey(key))
                {
                    positions[key] = mp.Count;
                }
            }

            // Step 2: Subtract assigned soldiers from the required counts.
            foreach (var position in (mi.SoldierMissions ?? []).Select(sm => sm.MissionPosition).Where(mp => mp != null))
            {
                if (positions.ContainsKey(position.Position))
                {
                    positions[position.Position]--;
                }
            }

            // Step 3: Return only positions that still need soldiers.
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

        /// <summary>
        /// Returns all distinct candidate schedule GUIDs from the SoldierMissionCandidates table.
        /// </summary>
        public List<string> GetAllCandidates()
        {
            using var db = new ShabzakDB();
            var candidates = db.SoldierMissionCandidates
                .Select(sm => sm.CandidateId)
                .Distinct()
                .ToList();
            return candidates ?? [];
        }

        /// <summary>
        /// Loads a candidate schedule's validation results from its JSON file on disk.
        /// </summary>
        /// <param name="guid">The candidate GUID (used as filename).</param>
        /// <returns>The deserialized assignment validation model.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the candidate JSON file doesn't exist.</exception>
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

        /// <summary>
        /// Removes a soldier's assignment from a mission instance in the database.
        /// Updates the instance's IsFilled flag if removing drops below required positions.
        /// </summary>
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
            // Goal: Generate a reproducible pseudo-random number in [0, 1) for a given
            // (seed, soldierId, instanceId) triple. Uses a hash-based approach with
            // Knuth multiplicative constants and xorshift mixing to ensure good distribution.
            // Being deterministic means the same schedule variant always produces the same jitter,
            // making results reproducible for debugging and comparison.
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
            // Goal: Collect all mission instances that fall entirely within the scheduling window [from, to].
            // Only includes instances where both start and end are within the range.
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

        /// <summary>
        /// Computes a composite rank score for a soldier-instance pair.
        /// Multiplies: RestMultiplier × PositionMultiplier × MissionAvg × TotalMissionAvg × HoursAvg × TotalHoursAvg.
        /// Any factor returning 0 short-circuits the entire score to 0 (soldier is ineligible).
        /// </summary>
        private double RankSoldierForPosition(RunContext ctx, GetAvailableSoldiersModel soldier, MissionInstance missionInstance, CandidateSoldierAssignment candidate)
        {
            // Goal: Compute a composite suitability score for assigning this soldier to this instance.
            // The score is the product of six independent multipliers, each in [0, 1]:
            //   1. Rest Multiplier     — Does the soldier have enough rest before/after this instance?
            //   2. Position Multiplier  — Does the soldier's position match the mission's requirements?
            //   3. Mission Avg          — Has this soldier been assigned to THIS mission less than average?
            //   4. Total Mission Avg    — Has this soldier been assigned to ANY mission less than average?
            //   5. Mission Hours Avg    — Has this soldier worked fewer hours on THIS mission than average?
            //   6. Total Hours Avg      — Has this soldier worked fewer total hours than average?
            //
            // If any multiplier is 0, the soldier is ineligible and we short-circuit to 0.
            // Higher score = better candidate. Each multiplier is also recorded in RankBreakdown
            // for transparency/debugging.

            var baseScore = 1.0;

            // Factor 1: Rest time — penalizes soldiers with insufficient rest gaps.
            var soldierRestMultiplier = GetSoldierRestMultiplier(soldier);
            candidate.RankBreakdown.Add("Rest Multiplier", soldierRestMultiplier);

            //Logger.LogToMemory($"\tSoldier Rest Multiplier is: {soldierRestMultiplier}");
            if (soldierRestMultiplier == 0.0)
            {
                return 0.0;
            }
            baseScore *= soldierRestMultiplier;
            var dbSoldier = _soldiersCache.GetDbSoldierById(soldier.Soldier.Id);

            // Factor 2: Position match — exact match = 1.0, similar = 0.7–1.0, none = 0.0.
            var positionScoreMultiplier = GetSoldierPositionMultiplier(ctx, dbSoldier, missionInstance);
            candidate.RankBreakdown.Add("Position Multiplier", positionScoreMultiplier);

            //Logger.LogToMemory($"\tSoldier Position Multiplier is: {positionScoreMultiplier}");
            if (positionScoreMultiplier == 0.0)
            {
                return 0.0;
            }
            baseScore *= positionScoreMultiplier;

            // Factor 3: Per-mission assignment fairness — penalizes soldiers assigned to this mission more than average.
            var missionAvg = RankSoldierByMissionAvg(ctx, soldier, missionInstance);
            candidate.RankBreakdown.Add("Mission Avg Multiplier", missionAvg);

            //Logger.LogToMemory($"\tSoldier Mission Avg Multiplier is: {missionAvg}");
            baseScore *= missionAvg;

            // Factor 4: Total assignment fairness — penalizes soldiers assigned to any mission more than average.
            var totalMissionAvg = RankSoldierByTotalMissionsAvg(ctx, soldier, missionInstance);
            candidate.RankBreakdown.Add("Total Mission Avg Multiplier", totalMissionAvg);

            //Logger.LogToMemory($"\tSoldier Total Mission Avg Multiplier is: {missionAvg}");
            baseScore *= totalMissionAvg;

            // Factor 5: Per-mission hours fairness — penalizes soldiers who worked more hours on this mission.
            var hoursAvg = RankSoldierByHoursAvg(ctx, soldier, missionInstance);
            candidate.RankBreakdown.Add("Mission Hours Avg Multiplier", hoursAvg);

            //Logger.LogToMemory($"\tSoldier Total Mission Avg Multiplier is: {missionAvg}");
            baseScore *= hoursAvg;

            // Factor 6: Total hours fairness — penalizes soldiers who worked more total hours across all missions.
            var totalHoursAvg = RankSoldierByTotalHoursAvg(ctx, soldier, missionInstance);
            candidate.RankBreakdown.Add("Total Hours Avg Multiplier", totalHoursAvg);

            //Logger.LogToMemory($"\tSoldier Total Mission Avg Multiplier is: {missionAvg}");
            baseScore *= totalHoursAvg;

            //RankSoldierByTotalHoursAvg
            return baseScore;
        }


        private double RankSoldierByMissionAvg(RunContext ctx, GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            // Goal: Score how fairly this soldier has been assigned to THIS specific mission.
            // avg = total assignments for this mission / number of distinct soldiers assigned to it.
            // If the soldier's count is at or below average, score = 1.0 (no penalty).
            // Above average → progressively penalized via StepFairnessScore staircase.

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
            // Goal: Score how fairly this soldier has been assigned across ALL missions.
            // avg = total assignments across all missions / number of soldiers with any assignment.
            // Penalizes soldiers who have been assigned more than their fair share overall.

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
            // Goal: Score how fairly this soldier's hours (or assignment count) on THIS mission
            // compare to the average. When HoursAsHours is enabled, uses actual hours;
            // otherwise falls back to assignment count as a proxy.
            // Uses strict comparison (strictBelow: true) — soldiers exactly at average get 1.0.

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
            // Goal: Score how fairly this soldier's total hours (or assignment count) across ALL
            // missions compare to the global average. Same logic as RankSoldierByHoursAvg but
            // across all missions instead of just one specific mission.

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

        /// <summary>
        /// Staircase fairness scoring: penalizes soldiers who have more assignments/hours than the average.
        /// Thresholds: ≤avg → 1.0, ≤1.05×avg → 0.85, ≤1.1×avg → 0.7, ≤1.15×avg → 0.5, ≤1.2×avg → 0.2, above → 0.0.
        /// <paramref name="strictBelow"/>: false uses ≤ comparison (for count-based), true uses &lt; (for hours-based).
        /// </summary>
        private static double StepFairnessScore(double value, double avg, bool strictBelow)
        {
            // Goal: Map a soldier's value (assignment count or hours) relative to the average
            // onto a discrete fairness score using a staircase function.
            //   ≤ avg        → 1.0  (at or below average: full score)
            //   ≤ 1.05× avg  → 0.85 (slightly above average)
            //   ≤ 1.10× avg  → 0.7  (moderately above)
            //   ≤ 1.15× avg  → 0.5  (significantly above)
            //   ≤ 1.20× avg  → 0.2  (heavily above)
            //   > 1.20× avg  → 0.0  (far above average: ineligible)
            //
            // strictBelow: when true, uses strict '<' (for hours); when false, uses '≤' (for counts).

            bool atOrBelow(double v, double threshold) => strictBelow ? v < threshold : v <= threshold;
            if (atOrBelow(value, avg)) return 1.0;
            if (atOrBelow(value, avg * 1.05)) return 0.85;
            if (atOrBelow(value, avg * 1.1)) return 0.7;
            if (atOrBelow(value, avg * 1.15)) return 0.5;
            if (atOrBelow(value, avg * 1.2)) return 0.2;
            return 0.0;
        }


        /// <summary>
        /// Computes position match multiplier for a soldier-mission pair.
        /// Exact match → 1.0. Similar positions: 1 match → 0.7, 2 → 0.8, 3 → 0.9, 4+ → 1.0. No match → 0.0.
        /// Over-qualification damping optionally penalizes non-exact matches.
        /// Results are cached per (soldierId, missionId) pair.
        /// </summary>
        private double GetSoldierPositionMultiplier(RunContext ctx, Soldier soldier, MissionInstance missionInstance)
        {
            // Goal: Determine how well a soldier's positions match the mission's required positions.
            // Algorithm:
            //   1. Check for an exact match between the soldier's positions and any mission position.
            //      If found → multiplier = 1.0.
            //   2. Otherwise, expand the soldier's positions via GetSoldierSimilarPositions and count
            //      how many mission positions have a similar-position match.
            //      0 matches → 0.0, 1 → 0.7, 2 → 0.8, 3 → 0.9, 4+ → 1.0.
            //   3. Apply over-qualification damping if enabled (penalizes non-exact matches).
            //   4. Cache the result per (soldierId, missionId) to avoid recomputation.

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

        /// <summary>
        /// Computes rest-time multiplier based on the minimum of before/after rest scores.
        /// If either side has 0 rest (direct overlap), returns 0.0 (soldier unavailable).
        /// Rest scoring: ≥1.5×threshold → 0.9, ≥1.25× → 0.8, ≥threshold → 0.7, below → 0.1.
        /// </summary>
        private double GetSoldierRestMultiplier(GetAvailableSoldiersModel soldier)
        {
            // Goal: Compute a rest-time multiplier that penalizes soldiers with insufficient rest.
            // Evaluates both the before-gap and after-gap independently, returns the minimum.
            // If either side has 0 rest (time overlap), the soldier is completely unavailable (return 0).
            var beforeScore = ComputeRestSideScore(soldier.RestTimeBefore, soldier.RequiredRestBeforeThreshold);
            var afterScore = ComputeRestSideScore(soldier.RestTimeAfter, soldier.RequiredRestAfterThreshold);

            if (beforeScore == 0.0 || afterScore == 0.0) return 0.0;
            return Math.Min(beforeScore, afterScore);
        }

        private static double ComputeRestSideScore(int? restTime, int? requiredRestThreshold)
        {
            // Goal: Score one side (before or after) of the rest gap.
            // Uses the mission's required rest threshold (default 8 hours) as the baseline.
            //   null rest (no nearby commitment) → 1.0 (fully rested).
            //   0 hours (overlap)               → 0.0 (unavailable).
            //   ≥ 1.5× threshold                → 0.9 (well-rested).
            //   ≥ 1.25× threshold               → 0.8.
            //   ≥ threshold                     → 0.7 (minimally rested).
            //   < threshold                     → 0.1 (under-rested, but not overlapping).
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
            // Goal: Parse the soldier's position CSV string (e.g., "0,3,13") into a list of
            // Position enum values. Falls back to Position.Simple for any unparseable value.
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
            // Goal: Expand a soldier's positions into the full set of similar/compatible positions
            // using the PositionHelper similarity map (e.g., Marksman → [Marksman, Sniper]).
            // Results are cached per soldier ID to avoid redundant computation.
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

        /// <summary>
        /// Starts an interactive auto-assign session. Initializes context, builds availability index,
        /// and begins advancing through instances. Pauses at the first instance requiring user input
        /// (based on PauseOn strategy: EveryInstance or FaultyOnly).
        /// Throws if batch auto-assign is running or max concurrent sessions reached.
        /// </summary>
        public InteractiveAutoAssignStep StartInteractive(
            DateTime? from,
            DateTime? to,
            List<int>? missions,
            List<int>? soldiers,
            AutoAssignScoringOptions? scoringOptions,
            InteractivePauseOn pauseOn,
            bool showAllSoldiersOnPause)
        {
            // Goal: Start a new interactive auto-assign session.
            // Unlike batch mode (which generates multiple complete schedules), interactive mode
            // processes instances one at a time, pausing for user input based on the PauseOn strategy:
            //   - EveryInstance: pause at every instance for the user to review/modify picks.
            //   - FaultyOnly: auto-fill well-covered instances, pause only when under-filled.
            //
            // Steps:
            //   1. Validate no conflicting batch run and session limit not exceeded.
            //   2. Initialize RunContext (same as batch: reload cache, resolve filters, build index).
            //   3. Create a session object with ordered instances and persist in _interactiveSessions.
            //   4. Start advancing through instances via AdvanceLoop (will pause at first applicable instance).

            if (_batchRunning)
            {
                throw new InvalidOperationException("Cannot start interactive session while batch auto-assign is running.");
            }
            if (_interactiveSessions.Count >= MaxConcurrentSessions)
            {
                throw new InvalidOperationException($"Maximum concurrent interactive sessions ({MaxConcurrentSessions}) reached.");
            }

            // Step 2: Initialize context — same setup as batch mode.
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

            // Force single schedule for interactive mode — only one starting mission needed
            // since interactive mode builds exactly one schedule with user guidance.
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

            // Step 3–4: Store session and begin the advance loop (processes until first pause point).
            _interactiveSessions[sessionId] = session;
            Logger.Log($"Interactive session {sessionId} started with {orderedInstances.Count} instances.");

            return AdvanceLoop(session);
        }

        /// <summary>
        /// Continues an interactive session. Applies user-provided soldier picks (or skips the instance),
        /// commits assignments, and advances to the next instance. Returns Paused or Completed status.
        /// Supports auto-demotion: if a picked soldier doesn't match the specified position, finds a valid one.
        /// </summary>
        public InteractiveAutoAssignStep ContinueInteractive(string sessionId, List<CandidatePickDto> picks, bool skipInstance)
        {
            // Goal: Resume an interactive session after the user has reviewed a paused instance.
            // The user either provides soldier picks or skips the instance.
            //
            // Steps:
            //   1. Look up the session and acquire its semaphore lock.
            //   2. If skipping, log and advance to the next instance.
            //   3. Otherwise, convert user picks into SoldierMission assignments.
            //      - Auto-demotion: if the specified position ID doesn't exist, find the best
            //        matching position via the soldier's similar-positions set.
            //   4. Commit assignments, log, and advance via AdvanceLoop.

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

                // Step 2: Skip path — log and move on without assigning anyone.
                if (skipInstance)
                {
                    LogInteractiveAction(sessionId, instanceId, "Skipped", null);
                    ctx.CandidatesPerInstance[instanceId] = ctx.CandidatesPerInstance.ContainsKey(instanceId)
                        ? ctx.CandidatesPerInstance[instanceId] : new List<CandidateSoldierAssignment>();
                    session.CurrentIndex++;
                    return AdvanceLoop(session);
                }

                // Step 3: Build assignments from user picks with auto-demotion.
                // Auto-demotion finds a valid position if the one the user chose doesn't exist.
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

        /// <summary>
        /// Cancels and removes an interactive auto-assign session by its ID.
        /// </summary>
        public void CancelInteractive(string sessionId)
        {
            if (_interactiveSessions.TryRemove(sessionId, out _))
            {
                Logger.Log($"Interactive session {sessionId} cancelled.");
            }
        }

        private InteractiveAutoAssignStep AdvanceLoop(InteractiveAutoAssignSession session)
        {
            // Goal: Process instances sequentially until a pause point is reached or all are done.
            //
            // For each instance:
            //   1. Skip if already assigned or fully filled.
            //   2. Compute and rank candidates.
            //   3. Try to fill the instance.
            //   4. Decide whether to pause (based on PauseOn strategy and fill status):
            //      - EveryInstance: always pause for user review.
            //      - FaultyOnly: pause only if not fully filled.
            //   5. If not pausing, commit the auto-filled assignments and move to the next instance.
            //   6. When all instances are processed, finalize and return Completed status.

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

                // Skip instances that already have accepted assignments from DB.
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

                // Rank all eligible soldiers for this instance.
                var candidateSoldiers = ComputeAndRankCandidates(ctx, instance);

                // If ShowAllSoldiersOnPause is enabled, include zero-rank soldiers in the
                // pause view so the user can manually pick soldiers that the algorithm excluded.
                var allCandidatesForPause = session.ShowAllSoldiersOnPause
                    ? ComputeAllCandidatesIncludingZeroRank(ctx, instance)
                    : candidateSoldiers;

                var (assignments, missingPositions, isFilled) = TryFillInstance(ctx, candidateSoldiers, instance);

                // Determine if we should pause for user input.
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

                // No pause needed — auto-fill this instance and continue to the next one.
                CommitAssignment(ctx, instance, assignments);
                ctx.CandidatesPerInstance[instanceId] = candidateSoldiers;
                LogInteractiveAction(session.SessionId, instanceId, "AutoFilled", null);
                session.CurrentIndex++;
            }

            // All instances processed — finalize the schedule, persist, and clean up the session.
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
            // Goal: Compute candidates for ALL soldiers (including those with rank 0 / unavailable).
            // Unlike ComputeAndRankCandidates which filters out rank-0 soldiers, this method
            // keeps everyone so the interactive UI can show the user all possible picks,
            // even soldiers the algorithm would normally exclude.

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
            // Goal: Persist every interactive action (AutoFilled, Skipped, UserPicked) to the
            // InteractiveAutoAssignLogs table for audit trail and debugging purposes.
            // Failures are swallowed to avoid crashing the session over a logging issue.
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
