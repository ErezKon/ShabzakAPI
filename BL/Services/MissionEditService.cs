using BL.Cache;
using BL.Extensions;
using BL.Logging;
using BL.Models.Schedule;
using DataLayer;
using DataLayer.Models;
using Microsoft.EntityFrameworkCore;
using Translators.Encryption;

namespace BL.Services
{
    /// <summary>
    /// Non-destructive, ISO-based mission and instance writes for the agent-facing endpoints: create a mission (optionally with
    /// generated instances), edit its details and position slots without touching instances, and generate / add / reschedule /
    /// delete instances. Text fields are AES-encrypted as in every mission write (blank → "N/A" placeholder). Writes run under
    /// <see cref="ScheduleService.WriteSync"/>, save once and reload the caches. Registered as a singleton.
    /// <para>The legacy <c>MissionService.UpdateMission</c> deletes and recreates every instance (and with them all assignments);
    /// the agent must use <see cref="UpdateMissionDetails"/> instead.</para>
    /// </summary>
    public class MissionEditService
    {
        private readonly ScheduleService _scheduleService;
        private readonly AESEncryptor _encryptor = new();

        public MissionEditService(ScheduleService scheduleService)
        {
            _scheduleService = scheduleService;
        }

        /// <summary>
        /// Creates a mission with its position slots. A special mission gets one instance [specialStart, specialStart + duration);
        /// a recurring mission optionally gets instances generated over <see cref="MissionCreateInput.Generate"/>.
        /// </summary>
        /// <exception cref="ArgumentException">Invalid input.</exception>
        public CreateMissionResultDto CreateMission(MissionCreateInput input)
        {
            var name = RequireName(input.Name);
            ValidateNumbers(input.SoldiersRequired, input.CommandersRequired, input.Duration, input.ActualHours, input.RequiredRestAfter);
            var fromTime = NormalizeTimeOfDay(input.FromTime, "fromTime");
            var toTime = NormalizeTimeOfDay(input.ToTime, "toTime");
            var slots = MissionSlotPlanner.Plan([], input.Positions).Added;
            var instances = PlanInitialInstances(input, fromTime, toTime);

            var mission = new Mission
            {
                Name = name,
                Description = input.Description?.Trim() ?? string.Empty,
                SoldiersRequired = input.SoldiersRequired,
                CommandersRequired = input.CommandersRequired,
                Duration = input.Duration,
                FromTime = fromTime,
                ToTime = toTime,
                IsSpecial = input.IsSpecial,
                RequiredInstances = true,
                ActualHours = input.ActualHours,
                RequiredRestAfter = input.RequiredRestAfter,
                MissionPositions = [],
                MissionInstances = []
            }.Encrypt();
            foreach (var slot in slots)
            {
                mission.MissionPositions.Add(new MissionPositions { Position = slot.Position, Count = slot.Count, Mission = mission });
            }
            var required = slots.Sum(s => s.Count);
            foreach (var slot in instances)
            {
                mission.MissionInstances.Add(new MissionInstance { FromTime = slot.From, ToTime = slot.To, IsFilled = required == 0, Mission = mission });
            }

            lock (ScheduleService.WriteSync)
            {
                using var db = DbFactory.Create();
                db.Missions.Add(mission);
                db.SaveChanges();
            }
            Logger.Log($"Mission/CreateMission created mission ID={mission.Id} with {slots.Count} slot(s) and {instances.Count} instance(s)");
            MissionsCache.ReloadAsync();
            return new CreateMissionResultDto { Mission = Summary(mission.Id), GeneratedInstances = instances.Count };
        }

        /// <summary>
        /// Updates the given fields of a mission. Instances are never touched. <see cref="MissionUpdateInput.Positions"/> (when given)
        /// is upserted: removing a slot that assignments or pending auto-assign candidates reference is a conflict, and IsFilled
        /// of every instance is recomputed for the new required count.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Unknown mission.</exception>
        /// <exception cref="ScheduleConflictException">A removed slot is still referenced.</exception>
        /// <exception cref="ArgumentException">Nothing to update, or invalid input.</exception>
        public MissionSummaryDto UpdateMissionDetails(MissionUpdateInput input)
        {
            if (input is { Name: null, Description: null, SoldiersRequired: null, CommandersRequired: null, Duration: null, FromTime: null, ToTime: null, ActualHours: null, RequiredRestAfter: null, Positions: null })
            {
                throw new ArgumentException("Nothing to update.");
            }
            ValidateNumbers(input.SoldiersRequired, input.CommandersRequired, input.Duration, input.ActualHours, input.RequiredRestAfter);
            var name = input.Name == null ? null : RequireName(input.Name);
            var fromTime = NormalizeTimeOfDay(input.FromTime, "fromTime");
            var toTime = NormalizeTimeOfDay(input.ToTime, "toTime");

            lock (ScheduleService.WriteSync)
            {
                using var db = DbFactory.Create();
                var mission = db.Missions
                    .Include(m => m.MissionPositions)
                    .FirstOrDefault(m => m.Id == input.Id)
                    ?? throw new KeyNotFoundException($"Mission {input.Id} not found.");

                if (name != null)
                {
                    mission.Name = _encryptor.Encrypt(name);
                }
                if (input.Description != null)
                {
                    mission.Description = EncryptOptional(input.Description.Trim());
                }
                if (input.FromTime != null)
                {
                    mission.FromTime = EncryptOptional(fromTime);
                }
                if (input.ToTime != null)
                {
                    mission.ToTime = EncryptOptional(toTime);
                }
                mission.SoldiersRequired = input.SoldiersRequired ?? mission.SoldiersRequired;
                mission.CommandersRequired = input.CommandersRequired ?? mission.CommandersRequired;
                mission.Duration = input.Duration ?? mission.Duration;
                mission.ActualHours = input.ActualHours ?? mission.ActualHours;
                mission.RequiredRestAfter = input.RequiredRestAfter ?? mission.RequiredRestAfter;
                if (input.Positions != null)
                {
                    ApplySlotPlan(db, mission, input.Positions);
                }
                db.SaveChanges();
            }
            ScheduleService.ReloadCaches();
            return Summary(input.Id);
        }

        /// <summary>
        /// Generates back-to-back instances of <c>duration</c> hours over [from, to) (see <see cref="MissionInstanceGenerator"/>),
        /// skipping slots that overlap existing instances. With <paramref name="dryRun"/> nothing is saved.
        /// </summary>
        /// <param name="from">ISO local datetime, or a date (then the mission's FromTime applies).</param>
        /// <param name="to">ISO local datetime, or a date (then the mission's ToTime applies).</param>
        /// <exception cref="KeyNotFoundException">Unknown mission.</exception>
        /// <exception cref="ArgumentException">Special mission, invalid range, duration below 1h, or too many slots.</exception>
        public GenerateInstancesResultDto GenerateMissionInstances(int missionId, string? from, string? to, bool dryRun)
        {
            lock (ScheduleService.WriteSync)
            {
                using var db = DbFactory.Create();
                var mission = db.Missions
                    .AsNoTracking()
                    .Include(m => m.MissionPositions)
                    .FirstOrDefault(m => m.Id == missionId)
                    ?? throw new KeyNotFoundException($"Mission {missionId} not found.");
                if (mission.IsSpecial)
                {
                    throw new ArgumentException("A special mission has a single instance; use AddMissionInstances or UpdateMissionInstance.");
                }

                var range = MissionInstanceGenerator.ResolveRange(from, to, DecryptOptional(mission.FromTime), DecryptOptional(mission.ToTime));
                MissionInstanceGenerator.ValidateRange(range);
                var existing = db.MissionInstances
                    .AsNoTracking()
                    .Where(mi => mi.MissionId == missionId && mi.FromTime < range.To && mi.ToTime > range.From)
                    .Select(mi => new TimeSlot(mi.FromTime, mi.ToTime))
                    .ToList();
                var result = MissionInstanceGenerator.Generate(range, mission.Duration, existing);

                if (!dryRun && result.Slots.Count > 0)
                {
                    var required = mission.MissionPositions.Sum(p => p.Count);
                    db.MissionInstances.AddRange(result.Slots.Select(s => new MissionInstance
                    {
                        MissionId = missionId,
                        FromTime = s.From,
                        ToTime = s.To,
                        IsFilled = required == 0
                    }));
                    db.SaveChanges();
                    MissionsCache.ReloadAsync();
                }
                return new GenerateInstancesResultDto
                {
                    Count = result.Slots.Count,
                    SkippedExisting = result.SkippedExisting,
                    From = ScheduleDateFormat.ToIso(range.From),
                    To = ScheduleDateFormat.ToIso(range.To),
                    Preview = result.Slots.Take(MissionInstanceGenerator.PreviewSize).Select(ToSlotDto).ToList(),
                    LastSlot = result.Slots.Count > 0 ? ToSlotDto(result.Slots[^1]) : null
                };
            }
        }

        /// <summary>
        /// Adds explicit instances to a mission. Each must be non-empty and at most 62 days long; they may not overlap each other
        /// (400) or existing instances of the mission (409).
        /// </summary>
        /// <exception cref="KeyNotFoundException">Unknown mission.</exception>
        /// <exception cref="ScheduleConflictException">An instance overlaps an existing instance.</exception>
        /// <exception cref="ArgumentException">Invalid instances.</exception>
        public List<ScheduleInstanceDto> AddMissionInstances(int missionId, IReadOnlyList<TimeSlot>? instances)
        {
            if (instances == null || instances.Count == 0)
            {
                throw new ArgumentException("At least one instance is required.");
            }
            if (instances.Count > MissionInstanceGenerator.MaxSlots)
            {
                throw new ArgumentException($"At most {MissionInstanceGenerator.MaxSlots} instances can be added per call.");
            }
            foreach (var slot in instances)
            {
                MissionInstanceGenerator.ValidateRange(slot);
            }
            var sorted = instances.OrderBy(s => s.From).ToList();
            for (var i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].Overlaps(sorted[i - 1]))
                {
                    throw new ArgumentException($"The requested instances {Describe(sorted[i - 1])} and {Describe(sorted[i])} overlap each other.");
                }
            }

            lock (ScheduleService.WriteSync)
            {
                using var db = DbFactory.Create();
                var mission = db.Missions
                    .AsNoTracking()
                    .Include(m => m.MissionPositions)
                    .FirstOrDefault(m => m.Id == missionId)
                    ?? throw new KeyNotFoundException($"Mission {missionId} not found.");
                var from = sorted[0].From;
                var to = sorted.Max(s => s.To);
                var existing = db.MissionInstances
                    .AsNoTracking()
                    .Where(mi => mi.MissionId == missionId && mi.FromTime < to && mi.ToTime > from)
                    .Select(mi => new { mi.Id, Slot = new TimeSlot(mi.FromTime, mi.ToTime) })
                    .ToList();
                foreach (var slot in sorted)
                {
                    var clash = existing.FirstOrDefault(e => e.Slot.Overlaps(slot));
                    if (clash != null)
                    {
                        throw new ScheduleConflictException($"The instance {Describe(slot)} overlaps instance {clash.Id} ({Describe(clash.Slot)}) of the same mission.");
                    }
                }

                var required = mission.MissionPositions.Sum(p => p.Count);
                var entities = sorted
                    .Select(s => new MissionInstance { MissionId = missionId, FromTime = s.From, ToTime = s.To, IsFilled = required == 0 })
                    .ToList();
                db.MissionInstances.AddRange(entities);
                db.SaveChanges();
                MissionsCache.ReloadAsync();
                return _scheduleService.LoadInstanceDtos(db, entities.Select(e => e.Id).ToList());
            }
        }

        /// <summary>
        /// Changes an instance's times. The new times may not overlap another instance of the mission (409). Every assigned soldier
        /// is re-validated at the new times in their own slot (<see cref="ScheduleChangePlanner.Reschedule"/>); an OVERLAP blocks
        /// unless <paramref name="allowViolations"/>, other violations are only reported. With <paramref name="dryRun"/> the result
        /// is computed but not saved.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Unknown instance.</exception>
        /// <exception cref="ScheduleConflictException">The new times overlap another instance of the mission.</exception>
        /// <exception cref="ArgumentException">Empty, reversed or over-long time range.</exception>
        public ScheduleWriteResult<UpdateInstanceResultDto> UpdateMissionInstance(int missionInstanceId, DateTime from, DateTime to, bool allowViolations = false, bool dryRun = false)
        {
            var times = new TimeSlot(from, to);
            MissionInstanceGenerator.ValidateRange(times);

            lock (ScheduleService.WriteSync)
            {
                using var db = DbFactory.Create();
                var instance = ScheduleService.RequireInstance(db, missionInstanceId);
                var clash = db.MissionInstances
                    .AsNoTracking()
                    .Where(mi => mi.MissionId == instance.MissionId && mi.Id != instance.Id && mi.FromTime < to && mi.ToTime > from)
                    .OrderBy(mi => mi.FromTime)
                    .Select(mi => new { mi.Id, Slot = new TimeSlot(mi.FromTime, mi.ToTime) })
                    .FirstOrDefault();
                if (clash != null)
                {
                    throw new ScheduleConflictException($"{Describe(times)} overlaps instance {clash.Id} ({Describe(clash.Slot)}) of the same mission.");
                }

                var target = _scheduleService.ToConstraintInstance(instance, from, to);
                var soldierIds = target.Assignments.Select(a => a.SoldierId).Distinct().ToList();
                var soldiers = soldierIds
                    .Select(_scheduleService.FindSoldier)
                    .OfType<ConstraintSoldier>()
                    .ToDictionary(s => s.SoldierId);
                var validations = _scheduleService.Evaluate(db, ScheduleChangePlanner.Reschedule(target, soldiers))
                    .Concat(soldierIds.Where(id => !soldiers.ContainsKey(id)).Select(AssignmentConstraintEvaluator.NotFound))
                    .ToList();
                var blocked = ScheduleChangePlanner.BlockReason(validations, allowViolations, BlockingPolicy.Reschedule);
                if (blocked != null)
                {
                    return ScheduleWriteResult<UpdateInstanceResultDto>.Blocked(new UpdateInstanceResultDto { Validations = validations }, blocked);
                }

                instance.FromTime = from;
                instance.ToTime = to;
                if (dryRun)
                {
                    return ScheduleWriteResult<UpdateInstanceResultDto>.Ok(new UpdateInstanceResultDto { Instance = _scheduleService.ToDto(instance), Validations = validations });
                }
                db.SaveChanges();
                ScheduleService.ReloadCaches();
                return ScheduleWriteResult<UpdateInstanceResultDto>.Ok(new UpdateInstanceResultDto
                {
                    Instance = _scheduleService.LoadInstanceDtos(db, [instance.Id]).Single(),
                    Validations = validations
                });
            }
        }

        /// <summary>
        /// Deletes an instance together with its assignments (cascade). Pending auto-assign candidate rows of the instance are
        /// deleted as well: they have no foreign key, and a dangling row would make AcceptAssignCandidate fail.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Unknown instance.</exception>
        public DeleteInstanceResultDto DeleteMissionInstance(int missionInstanceId)
        {
            lock (ScheduleService.WriteSync)
            {
                using var db = DbFactory.Create();
                var instance = db.MissionInstances
                    .Include(mi => mi.Soldiers)
                    .FirstOrDefault(mi => mi.Id == missionInstanceId)
                    ?? throw new KeyNotFoundException($"Mission instance {missionInstanceId} not found.");
                var deletedAssignments = instance.Soldiers?.Count ?? 0;
                var candidates = db.SoldierMissionCandidates.Where(c => c.MissionInstanceId == missionInstanceId).ToList();
                db.SoldierMissionCandidates.RemoveRange(candidates);
                db.MissionInstances.Remove(instance);
                db.SaveChanges();
                if (candidates.Count > 0)
                {
                    Logger.Log($"Mission/DeleteMissionInstance instance={missionInstanceId} also removed {candidates.Count} pending auto-assign candidate row(s)");
                }
                ScheduleService.ReloadCaches();
                return new DeleteInstanceResultDto { DeletedAssignments = deletedAssignments };
            }
        }

        private static List<TimeSlot> PlanInitialInstances(MissionCreateInput input, string? fromTime, string? toTime)
        {
            if (input.IsSpecial)
            {
                if (input.Generate != null)
                {
                    throw new ArgumentException("A special mission gets a single instance at specialStart; 'generate' is not allowed.");
                }
                var start = input.SpecialStart ?? throw new ArgumentException("specialStart is required for a special mission.");
                var single = new TimeSlot(start, start.AddHours(input.Duration));
                MissionInstanceGenerator.ValidateRange(single);
                return [single];
            }
            if (input.SpecialStart.HasValue)
            {
                throw new ArgumentException("specialStart is only allowed for a special mission.");
            }
            if (input.Generate == null)
            {
                return [];
            }
            var range = MissionInstanceGenerator.ResolveRange(input.Generate.From, input.Generate.To, fromTime, toTime);
            return MissionInstanceGenerator.Generate(range, input.Duration).Slots.ToList();
        }

        private static void ApplySlotPlan(ShabzakDB db, Mission mission, IReadOnlyList<MissionSlotInput> requested)
        {
            var existing = mission.MissionPositions.ToList();
            var plan = MissionSlotPlanner.Plan(existing.Select(p => new ConstraintSlot(p.Id, p.Position, p.Count)).ToList(), requested);
            if (plan.RemovedIds.Count > 0)
            {
                var removedIds = plan.RemovedIds.ToList();
                var assignments = db.SoldierMission.Count(sm => removedIds.Contains(sm.MissionPositionId));
                var candidates = db.SoldierMissionCandidates.Count(c => removedIds.Contains(c.MissionPositionId));
                if (assignments + candidates > 0)
                {
                    throw new ScheduleConflictException(
                        $"Position slot(s) {string.Join(", ", removedIds)} cannot be removed: referenced by {assignments} assignment(s) and " +
                        $"{candidates} pending auto-assign candidate assignment(s). Unassign them first, or keep the slot and change its count.");
                }
                db.MissionPositions.RemoveRange(existing.Where(p => removedIds.Contains(p.Id)));
            }
            foreach (var slot in plan.Updated)
            {
                var entity = existing.First(p => p.Id == slot.MissionPositionId);
                entity.Position = slot.Position;
                entity.Count = slot.Count;
            }
            foreach (var slot in plan.Added)
            {
                mission.MissionPositions.Add(new MissionPositions { MissionId = mission.Id, Position = slot.Position, Count = slot.Count, Mission = mission });
            }

            var assigned = db.SoldierMission
                .Where(sm => sm.MissionInstance.MissionId == mission.Id)
                .GroupBy(sm => sm.MissionInstanceId)
                .Select(g => new { InstanceId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.InstanceId, x => x.Count);
            foreach (var instance in db.MissionInstances.Where(mi => mi.MissionId == mission.Id).ToList())
            {
                instance.IsFilled = assigned.GetValueOrDefault(instance.Id) >= plan.RequiredCount;
            }
        }

        private static void ValidateNumbers(int? soldiersRequired, int? commandersRequired, int? duration, double? actualHours, int? requiredRestAfter)
        {
            if (soldiersRequired < 0 || commandersRequired < 0)
            {
                throw new ArgumentException("soldiersRequired and commandersRequired must be 0 or more.");
            }
            if (duration < 1)
            {
                throw new ArgumentException("duration must be at least 1 hour.");
            }
            if (actualHours < 0 || requiredRestAfter < 0)
            {
                throw new ArgumentException("actualHours and requiredRestAfter must be 0 or more.");
            }
        }

        private static string RequireName(string? name) =>
            string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("name is required.") : name.Trim();

        /// <summary>Normalizes a daily time to "HH:mm"; null or blank yields null (unset).</summary>
        private static string? NormalizeTimeOfDay(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            return ScheduleDateFormat.TryParseTimeOfDay(value, out var time)
                ? ScheduleDateFormat.FormatTimeOfDay(time)
                : throw new ArgumentException($"'{field}' must be a daily time HH:mm.");
        }

        private MissionSummaryDto Summary(int missionId) => _scheduleService.GetMissionsSummary([missionId]).Single();

        private string EncryptOptional(string? plain) => _encryptor.Encrypt(string.IsNullOrWhiteSpace(plain) ? "N/A" : plain);

        private string? DecryptOptional(string? cipherText) => string.IsNullOrWhiteSpace(cipherText) ? null : _encryptor.Decrypt(cipherText);

        private static TimeSlotDto ToSlotDto(TimeSlot slot) => new() { From = ScheduleDateFormat.ToIso(slot.From), To = ScheduleDateFormat.ToIso(slot.To) };

        private static string Describe(TimeSlot slot) => $"{ScheduleDateFormat.ToIso(slot.From)}–{ScheduleDateFormat.ToIso(slot.To)}";
    }
}
