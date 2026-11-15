using BL.Cache;
using BL.Extensions;
using BL.Models.Schedule;
using DataLayer;
using DataLayer.Models;
using Microsoft.EntityFrameworkCore;
using Translators.Encryption;

namespace BL.Services
{
    /// <summary>
    /// Compact, agent-friendly schedule operations: queries (instances with assignments in a date range, mission summaries),
    /// assignment validation (via <see cref="AssignmentConstraintEvaluator"/>) and assignment writes (assign, unassign,
    /// replace / swap, move) planned by <see cref="ScheduleChangePlanner"/>. Registered as a singleton.
    /// Datetimes in results are ISO local strings (<see cref="ScheduleDateFormat"/>).
    /// <para>Every write runs under <see cref="WriteSync"/>, validates first (violations block unless overridden), saves with a
    /// single SaveChanges, recomputes IsFilled of the affected instances and reloads the mission and soldier caches.
    /// The controllers log each write with IDs only.</para>
    /// </summary>
    public class ScheduleService
    {
        /// <summary>Maximum span of a GetAssignments range.</summary>
        public const int MaxRangeDays = 62;

        /// <summary>Maximum number of soldiers per ValidateAssignments call.</summary>
        public const int MaxValidateSoldiers = 1000;

        /// <summary>Maximum number of instance IDs per GetInstances call.</summary>
        public const int MaxInstanceIds = 200;

        /// <summary>
        /// Serializes the schedule and instance writes of this process so that validation and save are atomic with respect to
        /// each other (e.g. two admins filling the last seat). Shared with <see cref="MissionEditService"/>.
        /// </summary>
        internal static readonly object WriteSync = new();

        private readonly SoldiersCache _soldiersCache;
        private readonly AESEncryptor _encryptor = new();

        public ScheduleService(SoldiersCache soldiersCache)
        {
            _soldiersCache = soldiersCache;
        }

        /// <summary>
        /// Returns instances overlapping [<paramref name="from"/>, <paramref name="to"/>) sorted by start, using one SQL-filtered query.
        /// </summary>
        /// <param name="missionIds">Optional mission filter.</param>
        /// <param name="soldierIds">Optional: only instances where any of these soldiers is assigned (each with all its assignments).</param>
        /// <param name="onlyUnfilled">Only instances whose live assigned count is below the required count.</param>
        /// <exception cref="ArgumentException">Invalid range (to ≤ from, or longer than <see cref="MaxRangeDays"/> days).</exception>
        public List<ScheduleInstanceDto> GetAssignments(DateTime from, DateTime to, IReadOnlyCollection<int>? missionIds = null, IReadOnlyCollection<int>? soldierIds = null, bool onlyUnfilled = false)
        {
            if (to <= from)
            {
                throw new ArgumentException("'to' must be later than 'from'.");
            }
            if ((to - from).TotalDays > MaxRangeDays)
            {
                throw new ArgumentException($"The range must be at most {MaxRangeDays} days.");
            }

            using var db = DbFactory.Create();
            var query = db.MissionInstances
                .AsNoTracking()
                .Where(mi => mi.FromTime < to && mi.ToTime > from);
            if (missionIds is { Count: > 0 })
            {
                var ids = missionIds.Distinct().ToList();
                query = query.Where(mi => ids.Contains(mi.MissionId));
            }
            if (soldierIds is { Count: > 0 })
            {
                var ids = soldierIds.Distinct().ToList();
                query = query.Where(mi => mi.Soldiers.Any(sm => ids.Contains(sm.SoldierId)));
            }

            var instances = query
                .Include(mi => mi.Mission)
                    .ThenInclude(m => m.MissionPositions)
                .Include(mi => mi.Soldiers)
                    .ThenInclude(sm => sm.MissionPosition)
                .OrderBy(mi => mi.FromTime)
                .ThenBy(mi => mi.Id)
                .ToList();

            return instances
                .Select(ToDto)
                .Where(dto => !onlyUnfilled || !dto.IsFilled)
                .ToList();
        }

        /// <summary>
        /// Looks instances up by ID (same DTO as <see cref="GetAssignments"/>), in the requested order. Unknown IDs are omitted.
        /// </summary>
        /// <exception cref="ArgumentException">No IDs, or more than <see cref="MaxInstanceIds"/> distinct IDs.</exception>
        public List<ScheduleInstanceDto> GetInstances(IReadOnlyCollection<int>? instanceIds)
        {
            var ids = instanceIds?.Distinct().ToList() ?? [];
            if (ids.Count == 0)
            {
                throw new ArgumentException("At least one instance ID is required.");
            }
            if (ids.Count > MaxInstanceIds)
            {
                throw new ArgumentException($"At most {MaxInstanceIds} instances can be looked up per call.");
            }
            using var db = DbFactory.Create();
            return LoadInstanceDtos(db, ids);
        }

        /// <summary>
        /// Validates each soldier for one mission instance (overlap, vacation, rest, position fit, capacity, active).
        /// Unknown soldier IDs yield a NOT_FOUND result.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Unknown mission instance.</exception>
        /// <exception cref="ArgumentException">Too many soldiers, or a mission position that is not a slot of the instance.</exception>
        public List<AssignmentValidationDto> ValidateAssignments(
            int missionInstanceId,
            IReadOnlyCollection<int> soldierIds,
            int? missionPositionId = null,
            int? ignoreSoldierId = null,
            IReadOnlyCollection<int>? excludeInstanceIds = null)
        {
            var ids = soldierIds?.Distinct().ToList() ?? [];
            if (ids.Count == 0)
            {
                return [];
            }
            if (ids.Count > MaxValidateSoldiers)
            {
                throw new ArgumentException($"At most {MaxValidateSoldiers} soldiers can be validated per call.");
            }

            using var db = DbFactory.Create();
            var instance = FindInstance(db, missionInstanceId, track: false)
                ?? throw new KeyNotFoundException($"Mission instance {missionInstanceId} not found.");
            var target = ToConstraintInstance(instance);
            EnsureSlot(target, missionPositionId);

            var history = LoadSoldierHistory(db, target, ids);
            var options = new ConstraintOptions(missionPositionId, ignoreSoldierId, excludeInstanceIds);
            return ids
                .Select(id => FindSoldier(id) is { } soldier
                    ? history.Evaluate(target, soldier, options)
                    : AssignmentConstraintEvaluator.NotFound(id))
                .ToList();
        }

        /// <summary>
        /// Assigns a soldier to an instance: the requested slot, otherwise the best free fit
        /// (<see cref="AssignmentSlotSelector.SelectTargetSlot"/>). Blocked by any violation unless
        /// <paramref name="allowViolations"/>; ALREADY_ASSIGNED always blocks.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Unknown instance or soldier.</exception>
        /// <exception cref="ArgumentException">The requested slot is not a slot of the instance, or the mission has no slots.</exception>
        public ScheduleWriteResult<AssignSoldierResultDto> AssignSoldier(int missionInstanceId, int soldierId, int? missionPositionId = null, bool allowViolations = false)
        {
            lock (WriteSync)
            {
                using var db = DbFactory.Create();
                var instance = RequireInstance(db, missionInstanceId);
                var soldier = RequireSoldier(soldierId);
                var target = ToConstraintInstance(instance);
                EnsureSlot(target, missionPositionId);

                var placement = ScheduleChangePlanner.Assign(target, soldier, missionPositionId);
                var validation = Evaluate(db, [placement])[0];
                var blocked = ScheduleChangePlanner.BlockReason([validation], allowViolations, BlockingPolicy.Placement);
                if (blocked != null)
                {
                    return ScheduleWriteResult<AssignSoldierResultDto>.Blocked(new AssignSoldierResultDto { Validation = validation }, blocked);
                }

                AddAssignment(db, placement);
                RefreshIsFilled(db, instance);
                db.SaveChanges();
                ReloadCaches();
                return ScheduleWriteResult<AssignSoldierResultDto>.Ok(new AssignSoldierResultDto
                {
                    Instance = LoadInstanceDtos(db, [instance.Id]).Single(),
                    Validation = validation
                });
            }
        }

        /// <summary>Removes a soldier from an instance and returns the instance after the change.</summary>
        /// <exception cref="KeyNotFoundException">Unknown instance, or the soldier is not assigned to it.</exception>
        public ScheduleInstanceDto UnassignSoldier(int missionInstanceId, int soldierId)
        {
            lock (WriteSync)
            {
                using var db = DbFactory.Create();
                var instance = RequireInstance(db, missionInstanceId);
                db.SoldierMission.RemoveRange(RequireAssignments(instance, soldierId));
                RefreshIsFilled(db, instance);
                db.SaveChanges();
                ReloadCaches();
                return LoadInstanceDtos(db, [instance.Id]).Single();
            }
        }

        /// <summary>
        /// Replaces <paramref name="oldSoldierId"/> with <paramref name="newSoldierId"/> in an instance (the new soldier prefers the
        /// vacated slot). With <paramref name="swap"/>, the new soldier must be assigned to <paramref name="swapMissionInstanceId"/>
        /// and the old soldier takes their place there; both placements are validated with the instance each soldier leaves
        /// excluded. Blocked by any violation unless <paramref name="allowViolations"/>; ALREADY_ASSIGNED always blocks.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Unknown instance or soldier, or a soldier is not assigned where required.</exception>
        /// <exception cref="ArgumentException">Same soldier twice, swap without (or with the same) swap instance.</exception>
        public ScheduleWriteResult<ScheduleChangeResultDto> ReplaceSoldier(
            int missionInstanceId,
            int oldSoldierId,
            int newSoldierId,
            bool swap = false,
            int? swapMissionInstanceId = null,
            bool allowViolations = false)
        {
            if (oldSoldierId == newSoldierId)
            {
                throw new ArgumentException("oldSoldierId and newSoldierId must be different soldiers.");
            }
            if (swap && !swapMissionInstanceId.HasValue)
            {
                throw new ArgumentException("swapMissionInstanceId is required when swap is true.");
            }
            if (swap && swapMissionInstanceId == missionInstanceId)
            {
                throw new ArgumentException("swapMissionInstanceId must be a different instance than missionInstanceId.");
            }

            lock (WriteSync)
            {
                using var db = DbFactory.Create();
                var instance = RequireInstance(db, missionInstanceId);
                var oldRows = RequireAssignments(instance, oldSoldierId);
                var newSoldier = RequireSoldier(newSoldierId);
                var original = ToConstraintInstance(instance);

                var affected = new List<MissionInstance> { instance };
                var removed = new List<SoldierMission>(oldRows);
                List<PlannedPlacement> placements;
                if (swap)
                {
                    var swapInstance = RequireInstance(db, swapMissionInstanceId!.Value);
                    removed.AddRange(RequireAssignments(swapInstance, newSoldierId));
                    var oldSoldier = RequireSoldier(oldSoldierId);
                    var (newIntoOriginal, oldIntoSwap) = ScheduleChangePlanner.Swap(original, oldSoldier, ToConstraintInstance(swapInstance), newSoldier);
                    placements = [newIntoOriginal, oldIntoSwap];
                    affected.Add(swapInstance);
                }
                else
                {
                    placements = [ScheduleChangePlanner.Replace(original, oldSoldierId, newSoldier)];
                }

                return ApplyPlacements(db, placements, removed, affected, allowViolations);
            }
        }

        /// <summary>
        /// Moves a soldier from one instance to another (requested slot, else the same slot when both instances share it, else the
        /// best free fit). The source instance is excluded from the overlap and rest checks. Blocked by any violation unless
        /// <paramref name="allowViolations"/>; ALREADY_ASSIGNED always blocks.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Unknown instance or soldier, or the soldier is not assigned to the source instance.</exception>
        /// <exception cref="ArgumentException">Same source and target, or a requested slot that is not a slot of the target.</exception>
        public ScheduleWriteResult<ScheduleChangeResultDto> MoveSoldier(
            int soldierId,
            int fromMissionInstanceId,
            int toMissionInstanceId,
            int? toMissionPositionId = null,
            bool allowViolations = false)
        {
            if (fromMissionInstanceId == toMissionInstanceId)
            {
                throw new ArgumentException("toMissionInstanceId must be a different instance than fromMissionInstanceId.");
            }

            lock (WriteSync)
            {
                using var db = DbFactory.Create();
                var fromInstance = RequireInstance(db, fromMissionInstanceId);
                var rows = RequireAssignments(fromInstance, soldierId);
                var toInstance = RequireInstance(db, toMissionInstanceId);
                var soldier = RequireSoldier(soldierId);
                var target = ToConstraintInstance(toInstance);
                EnsureSlot(target, toMissionPositionId);

                var placement = ScheduleChangePlanner.Move(target, soldier, fromInstance.Id, rows[0].MissionPositionId, toMissionPositionId);
                return ApplyPlacements(db, [placement], rows, [fromInstance, toInstance], allowViolations);
            }
        }

        /// <summary>
        /// Mission definitions with positions and aggregated instance/assignment counts, computed with grouped SQL
        /// queries (no instance graph is loaded).
        /// </summary>
        /// <param name="missionIds">Optional filter; all missions when null/empty.</param>
        public List<MissionSummaryDto> GetMissionsSummary(IReadOnlyCollection<int>? missionIds = null)
        {
            using var db = DbFactory.Create();
            var now = DateTime.Now;
            var ids = missionIds is { Count: > 0 } ? missionIds.Distinct().ToList() : null;

            var missionsQuery = db.Missions.AsNoTracking().Include(m => m.MissionPositions).AsQueryable();
            var instancesQuery = db.MissionInstances.AsNoTracking();
            var assignmentsQuery = db.SoldierMission.AsNoTracking();
            if (ids != null)
            {
                missionsQuery = missionsQuery.Where(m => ids.Contains(m.Id));
                instancesQuery = instancesQuery.Where(mi => ids.Contains(mi.MissionId));
                assignmentsQuery = assignmentsQuery.Where(sm => ids.Contains(sm.MissionInstance.MissionId));
            }

            var missions = missionsQuery.ToList();
            var instanceStats = instancesQuery
                .GroupBy(mi => mi.MissionId)
                .Select(g => new
                {
                    MissionId = g.Key,
                    Count = g.Count(),
                    Future = g.Sum(mi => mi.FromTime >= now ? 1 : 0),
                    First = g.Min(mi => mi.FromTime),
                    Last = g.Max(mi => mi.ToTime)
                })
                .ToDictionary(s => s.MissionId);
            var assignmentCounts = assignmentsQuery
                .GroupBy(sm => sm.MissionInstance.MissionId)
                .Select(g => new { MissionId = g.Key, Count = g.Count() })
                .ToDictionary(s => s.MissionId, s => s.Count);

            return missions
                .OrderBy(m => m.Id)
                .Select(m =>
                {
                    var stats = instanceStats.GetValueOrDefault(m.Id);
                    return new MissionSummaryDto
                    {
                        Id = m.Id,
                        Name = DecryptRequired(m.Name),
                        Description = DecryptOptional(m.Description),
                        IsSpecial = m.IsSpecial,
                        RequiredInstances = m.RequiredInstances,
                        Duration = m.Duration,
                        FromTime = DecryptOptional(m.FromTime),
                        ToTime = DecryptOptional(m.ToTime),
                        SoldiersRequired = m.SoldiersRequired,
                        CommandersRequired = m.CommandersRequired,
                        ActualHours = m.ActualHours,
                        RequiredRestAfter = m.RequiredRestAfter,
                        Positions = (m.MissionPositions ?? [])
                            .OrderBy(p => p.Id)
                            .Select(p => new MissionPositionDto { Id = p.Id, Position = (int)p.Position, Count = p.Count })
                            .ToList(),
                        InstanceCount = stats?.Count ?? 0,
                        FutureInstanceCount = stats?.Future ?? 0,
                        FirstInstanceFrom = stats != null ? ScheduleDateFormat.ToIso(stats.First) : null,
                        LastInstanceTo = stats != null ? ScheduleDateFormat.ToIso(stats.Last) : null,
                        AssignmentCount = assignmentCounts.GetValueOrDefault(m.Id)
                    };
                })
                .ToList();
        }

        /// <summary>Loads an instance with Mission.MissionPositions and Soldiers (tracked for writes).</summary>
        internal static MissionInstance? FindInstance(ShabzakDB db, int missionInstanceId, bool track)
        {
            IQueryable<MissionInstance> query = db.MissionInstances
                .Include(mi => mi.Mission)
                    .ThenInclude(m => m.MissionPositions)
                .Include(mi => mi.Soldiers);
            if (!track)
            {
                query = query.AsNoTracking();
            }
            return query.FirstOrDefault(mi => mi.Id == missionInstanceId);
        }

        /// <summary>Tracked <see cref="FindInstance"/>; throws <see cref="KeyNotFoundException"/> when unknown.</summary>
        internal static MissionInstance RequireInstance(ShabzakDB db, int missionInstanceId) =>
            FindInstance(db, missionInstanceId, track: true)
            ?? throw new KeyNotFoundException($"Mission instance {missionInstanceId} not found.");

        /// <summary>Evaluator input for a loaded instance; <paramref name="from"/>/<paramref name="to"/> override its times (reschedule).</summary>
        internal ConstraintInstance ToConstraintInstance(MissionInstance instance, DateTime? from = null, DateTime? to = null) => new(
            instance.Id,
            instance.MissionId,
            DecryptRequired(instance.Mission.Name),
            from ?? instance.FromTime,
            to ?? instance.ToTime,
            instance.Mission.RequiredRestAfter,
            (instance.Mission.MissionPositions ?? []).Select(p => new ConstraintSlot(p.Id, p.Position, p.Count)).ToList(),
            (instance.Soldiers ?? []).Select(sm => new ConstraintSlotAssignment(sm.SoldierId, sm.MissionPositionId)).ToList());

        /// <summary>Evaluator input for a cached soldier (decrypted name, positions, active flag); null when unknown.</summary>
        internal ConstraintSoldier? FindSoldier(int soldierId)
        {
            var soldier = _soldiersCache.GetDbSoldierById(soldierId);
            return soldier == null ? null : new ConstraintSoldier(soldierId, soldier.Name, soldier.Active, soldier.GetSoldierPositions());
        }

        /// <summary>Validates placements (possibly on different instances) against the soldiers' current assignments and vacations.</summary>
        internal List<AssignmentValidationDto> Evaluate(ShabzakDB db, IReadOnlyList<PlannedPlacement> placements)
        {
            var histories = placements
                .GroupBy(p => p.Instance)
                .ToDictionary(g => g.Key, g => LoadSoldierHistory(db, g.Key, g.Select(p => p.Soldier.SoldierId).ToList()));
            return placements.Select(p => histories[p.Instance].Evaluate(p.Instance, p.Soldier, p.Options)).ToList();
        }

        /// <summary>Maps a loaded instance (Mission.MissionPositions and Soldiers loaded) to its DTO.</summary>
        internal ScheduleInstanceDto ToDto(MissionInstance instance) =>
            ScheduleInstanceMapper.ToDto(instance, DecryptRequired(instance.Mission.Name), SoldierName);

        /// <summary>Reads the given instances from the DB (no tracking) as DTOs, in the given order.</summary>
        internal List<ScheduleInstanceDto> LoadInstanceDtos(ShabzakDB db, IReadOnlyList<int> instanceIds)
        {
            var ids = instanceIds.Distinct().ToList();
            var dtos = db.MissionInstances
                .AsNoTracking()
                .Where(mi => ids.Contains(mi.Id))
                .Include(mi => mi.Mission)
                    .ThenInclude(m => m.MissionPositions)
                .Include(mi => mi.Soldiers)
                    .ThenInclude(sm => sm.MissionPosition)
                .ToList()
                .ToDictionary(mi => mi.Id, ToDto);
            return ids.Where(dtos.ContainsKey).Select(id => dtos[id]).ToList();
        }

        /// <summary>
        /// Sets IsFilled (assigned ≥ required) from the tracked, not-deleted assignments of a tracked instance whose Soldiers and
        /// Mission.MissionPositions are loaded, so the flag is correct after the pending SaveChanges.
        /// </summary>
        internal static void RefreshIsFilled(ShabzakDB db, MissionInstance instance)
        {
            var required = (instance.Mission.MissionPositions ?? []).Sum(p => p.Count);
            var assigned = db.ChangeTracker.Entries<SoldierMission>()
                .Count(e => e.Entity.MissionInstanceId == instance.Id && e.State != EntityState.Deleted && e.State != EntityState.Detached);
            instance.IsFilled = assigned >= required;
        }

        /// <summary>Reloads the mission and soldier caches in the background after a write.</summary>
        internal static void ReloadCaches()
        {
            MissionsCache.ReloadAsync();
            SoldiersCache.ReloadAsync();
        }

        private ScheduleWriteResult<ScheduleChangeResultDto> ApplyPlacements(
            ShabzakDB db,
            List<PlannedPlacement> placements,
            List<SoldierMission> removedAssignments,
            List<MissionInstance> affectedInstances,
            bool allowViolations)
        {
            var validations = Evaluate(db, placements);
            var blocked = ScheduleChangePlanner.BlockReason(validations, allowViolations, BlockingPolicy.Placement);
            if (blocked != null)
            {
                return ScheduleWriteResult<ScheduleChangeResultDto>.Blocked(new ScheduleChangeResultDto { Validations = validations }, blocked);
            }

            db.SoldierMission.RemoveRange(removedAssignments);
            placements.ForEach(p => AddAssignment(db, p));
            affectedInstances.ForEach(i => RefreshIsFilled(db, i));
            db.SaveChanges();
            ReloadCaches();
            return ScheduleWriteResult<ScheduleChangeResultDto>.Ok(new ScheduleChangeResultDto
            {
                Instances = LoadInstanceDtos(db, affectedInstances.Select(i => i.Id).ToList()),
                Validations = validations
            });
        }

        private static void AddAssignment(ShabzakDB db, PlannedPlacement placement)
        {
            var slotId = placement.SlotId
                ?? throw new ArgumentException($"Mission instance {placement.Instance.InstanceId} has no position slots.");
            db.SoldierMission.Add(new SoldierMission
            {
                SoldierId = placement.Soldier.SoldierId,
                MissionInstanceId = placement.Instance.InstanceId,
                MissionPositionId = slotId
            });
        }

        private static List<SoldierMission> RequireAssignments(MissionInstance instance, int soldierId)
        {
            var rows = (instance.Soldiers ?? []).Where(sm => sm.SoldierId == soldierId).ToList();
            return rows.Count > 0
                ? rows
                : throw new KeyNotFoundException($"Soldier {soldierId} is not assigned to mission instance {instance.Id}.");
        }

        private ConstraintSoldier RequireSoldier(int soldierId) =>
            FindSoldier(soldierId) ?? throw new KeyNotFoundException($"Soldier {soldierId} not found.");

        private static void EnsureSlot(ConstraintInstance instance, int? missionPositionId)
        {
            if (missionPositionId.HasValue && !instance.Slots.Any(s => s.MissionPositionId == missionPositionId.Value))
            {
                throw new ArgumentException($"Mission position {missionPositionId.Value} is not a slot of mission instance {instance.InstanceId}.");
            }
        }

        /// <summary>Assignments (window around the instance) and overlapping non-denied vacations of the given soldiers.</summary>
        private SoldierHistory LoadSoldierHistory(ShabzakDB db, ConstraintInstance instance, IReadOnlyCollection<int> soldierIds)
        {
            var ids = soldierIds.Distinct().ToList();

            // Only the nearest assignment on each side matters for rest; a window wider than any RequiredRestAfter is enough.
            var maxRequiredRest = db.Missions.Max(m => m.RequiredRestAfter) ?? 0;
            var windowHours = Math.Max(7 * 24, maxRequiredRest + 24);
            var windowStart = instance.From.AddHours(-windowHours);
            var windowEnd = instance.To.AddHours(windowHours);

            var assignmentRows = db.SoldierMission
                .AsNoTracking()
                .Where(sm => ids.Contains(sm.SoldierId)
                             && sm.MissionInstance.FromTime < windowEnd
                             && sm.MissionInstance.ToTime > windowStart)
                .Select(sm => new
                {
                    sm.SoldierId,
                    sm.MissionInstanceId,
                    sm.MissionInstance.FromTime,
                    sm.MissionInstance.ToTime,
                    sm.MissionInstance.Mission.RequiredRestAfter,
                    MissionName = sm.MissionInstance.Mission.Name
                })
                .ToList();
            var vacationRows = db.Vacations
                .AsNoTracking()
                .Where(v => ids.Contains(v.SoldierId)
                            && v.Approved != VacationRequestStatus.Denied
                            && v.From < instance.To
                            && v.To > instance.From)
                .Select(v => new { v.Id, v.SoldierId, v.From, v.To, v.Approved })
                .ToList();

            return new SoldierHistory(
                assignmentRows
                    .GroupBy(r => r.SoldierId)
                    .ToDictionary(g => g.Key, g => g
                        .Select(r => new ConstraintAssignment(r.MissionInstanceId, DecryptRequired(r.MissionName), r.FromTime, r.ToTime, r.RequiredRestAfter))
                        .ToList()),
                vacationRows
                    .GroupBy(r => r.SoldierId)
                    .ToDictionary(g => g.Key, g => g
                        .Select(r => new ConstraintVacation(r.Id, r.From, r.To, r.Approved))
                        .ToList()));
        }

        /// <summary>Per-soldier assignments and vacations loaded for one validated instance.</summary>
        private sealed class SoldierHistory(
            Dictionary<int, List<ConstraintAssignment>> assignments,
            Dictionary<int, List<ConstraintVacation>> vacations)
        {
            public AssignmentValidationDto Evaluate(ConstraintInstance instance, ConstraintSoldier soldier, ConstraintOptions options) =>
                AssignmentConstraintEvaluator.Evaluate(
                    instance,
                    soldier,
                    assignments.GetValueOrDefault(soldier.SoldierId) ?? [],
                    vacations.GetValueOrDefault(soldier.SoldierId) ?? [],
                    options);
        }

        private string SoldierName(int soldierId) => _soldiersCache.GetSoldierById(soldierId)?.Name ?? $"#{soldierId}";

        private string DecryptRequired(string cipherText) => _encryptor.Decrypt(cipherText);

        /// <summary>Decrypts an optional encrypted field; blank and the "N/A" placeholder become null.</summary>
        private string? DecryptOptional(string? cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText))
            {
                return null;
            }
            var plain = _encryptor.Decrypt(cipherText);
            return string.IsNullOrWhiteSpace(plain) || plain == "N/A" ? null : plain;
        }
    }
}
