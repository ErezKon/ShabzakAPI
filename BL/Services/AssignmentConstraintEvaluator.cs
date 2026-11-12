using BL.Models.Schedule;
using DataLayer.Models;

namespace BL.Services
{
    /// <summary>A required slot of the instance's mission: <see cref="Position"/> × <see cref="Count"/>.</summary>
    public sealed record ConstraintSlot(int MissionPositionId, Position Position, int Count);

    /// <summary>A soldier currently assigned to a slot of the validated instance.</summary>
    public sealed record ConstraintSlotAssignment(int SoldierId, int MissionPositionId);

    /// <summary>The mission instance being validated.</summary>
    public sealed record ConstraintInstance(
        int InstanceId,
        int MissionId,
        string MissionName,
        DateTime From,
        DateTime To,
        int? RequiredRestAfter,
        IReadOnlyList<ConstraintSlot> Slots,
        IReadOnlyList<ConstraintSlotAssignment> Assignments);

    /// <summary>The soldier being validated.</summary>
    public sealed record ConstraintSoldier(int SoldierId, string Name, bool Active, IReadOnlyList<Position> Positions);

    /// <summary>One of the soldier's existing assignments (any instance), with its mission's RequiredRestAfter.</summary>
    public sealed record ConstraintAssignment(int InstanceId, string MissionName, DateTime From, DateTime To, int? RequiredRestAfter);

    /// <summary>One of the soldier's vacations.</summary>
    public sealed record ConstraintVacation(int Id, DateTime From, DateTime To, VacationRequestStatus Status);

    /// <summary>
    /// Validation options: <see cref="MissionPositionId"/> restricts the check to one slot;
    /// <see cref="IgnoreSoldierId"/> frees that soldier's slot in the instance (replacement);
    /// <see cref="ExcludeInstanceIds"/> are ignored for overlap and rest checks (move / swap).
    /// </summary>
    public sealed record ConstraintOptions(
        int? MissionPositionId = null,
        int? IgnoreSoldierId = null,
        IReadOnlyCollection<int>? ExcludeInstanceIds = null);

    /// <summary>
    /// Pure (no DB access) evaluator of the assignment rules used by the schedule endpoints and the AI agent.
    /// <para>Violations: INACTIVE, ALREADY_ASSIGNED, OVERLAP, ON_VACATION (Pending/Approved), INSTANCE_FULL,
    /// POSITION_MISMATCH, REST_BEFORE, REST_AFTER. Warning: SIMILAR_POSITION.</para>
    /// <list type="bullet">
    /// <item>Overlap uses half-open intervals (back-to-back is not an overlap).</item>
    /// <item>Rest before: gap from the end of the previous assignment to this start must be at least the previous mission's
    /// RequiredRestAfter (default 8h). Rest after: gap from this end to the next assignment's start must be at least this
    /// mission's RequiredRestAfter (default 8h). Only the nearest assignment on each side is considered.</item>
    /// <item>Position fit uses <see cref="PositionHelper"/> (slot position → soldier positions allowed to fill it): an exact match
    /// among free slots wins over a similar one (ranking shared with <see cref="AssignmentSlotSelector"/>).</item>
    /// <item>INSTANCE_FULL: no free capacity in the requested slot (when given) or in the whole instance.</item>
    /// </list>
    /// </summary>
    public static class AssignmentConstraintEvaluator
    {
        /// <summary>Required rest (hours) when a mission's RequiredRestAfter is null (matches MissionService.ComputeRestScore).</summary>
        public const int DefaultRequiredRestHours = 8;

        /// <summary>
        /// Evaluates whether <paramref name="soldier"/> can be assigned to <paramref name="instance"/>.
        /// </summary>
        /// <param name="soldierAssignments">The soldier's assignments around the instance (may include the instance itself).</param>
        /// <param name="soldierVacations">The soldier's vacations (Denied ones are ignored).</param>
        /// <exception cref="ArgumentException">When <see cref="ConstraintOptions.MissionPositionId"/> is not a slot of the instance.</exception>
        public static AssignmentValidationDto Evaluate(
            ConstraintInstance instance,
            ConstraintSoldier soldier,
            IEnumerable<ConstraintAssignment> soldierAssignments,
            IEnumerable<ConstraintVacation> soldierVacations,
            ConstraintOptions? options = null)
        {
            options ??= new ConstraintOptions();
            var excluded = options.ExcludeInstanceIds?.ToHashSet() ?? [];
            var result = new AssignmentValidationDto { SoldierId = soldier.SoldierId, SoldierName = soldier.Name };

            if (!soldier.Active)
            {
                result.Violations.Add(AssignmentConstraintCodes.Inactive);
            }

            var effectiveAssignments = instance.Assignments
                .Where(a => a.SoldierId != options.IgnoreSoldierId)
                .ToList();
            if (effectiveAssignments.Any(a => a.SoldierId == soldier.SoldierId))
            {
                result.Violations.Add(AssignmentConstraintCodes.AlreadyAssigned);
            }

            var others = soldierAssignments
                .Where(a => a.InstanceId != instance.InstanceId && !excluded.Contains(a.InstanceId))
                .ToList();

            var overlap = others
                .Where(a => a.From < instance.To && instance.From < a.To)
                .OrderBy(a => a.From)
                .FirstOrDefault();
            if (overlap != null)
            {
                result.Violations.Add(AssignmentConstraintCodes.Overlap);
                result.Overlap = new OverlapInfoDto
                {
                    InstanceId = overlap.InstanceId,
                    MissionName = overlap.MissionName,
                    From = ScheduleDateFormat.ToIso(overlap.From),
                    To = ScheduleDateFormat.ToIso(overlap.To)
                };
            }

            var vacation = soldierVacations
                .Where(v => v.Status != VacationRequestStatus.Denied && v.From < instance.To && instance.From < v.To)
                .OrderBy(v => v.From)
                .FirstOrDefault();
            if (vacation != null)
            {
                result.Violations.Add(AssignmentConstraintCodes.OnVacation);
                result.Vacation = new VacationInfoDto
                {
                    Id = vacation.Id,
                    From = ScheduleDateFormat.ToIso(vacation.From),
                    To = ScheduleDateFormat.ToIso(vacation.To),
                    Status = vacation.Status.ToString()
                };
            }

            EvaluatePosition(instance, soldier, options, result);
            EvaluateRest(instance, others, result);

            result.IsValid = result.Violations.Count == 0;
            return result;
        }

        /// <summary>Result for a soldier ID that does not exist.</summary>
        public static AssignmentValidationDto NotFound(int soldierId) => new()
        {
            SoldierId = soldierId,
            IsValid = false,
            Violations = [AssignmentConstraintCodes.NotFound],
            PositionFit = PositionFitKind.None
        };

        private static void EvaluatePosition(
            ConstraintInstance instance,
            ConstraintSoldier soldier,
            ConstraintOptions options,
            AssignmentValidationDto result)
        {
            var slots = AssignmentSlotSelector.Capacities(instance, options.IgnoreSoldierId);

            List<SlotCapacity> candidates;
            if (options.MissionPositionId.HasValue)
            {
                var requested = slots.FirstOrDefault(s => s.Slot.MissionPositionId == options.MissionPositionId.Value)
                    ?? throw new ArgumentException($"Mission position {options.MissionPositionId.Value} is not a slot of mission instance {instance.InstanceId}.");
                candidates = [requested];
                if (requested.Missing <= 0)
                {
                    result.Violations.Add(AssignmentConstraintCodes.InstanceFull);
                }
            }
            else
            {
                var free = slots.Where(s => s.Missing > 0).ToList();
                if (free.Count == 0)
                {
                    result.Violations.Add(AssignmentConstraintCodes.InstanceFull);
                }
                // When nothing is free, still report how the soldier would fit (without suggesting a slot).
                candidates = free.Count > 0 ? free : slots;
            }

            var (fit, chosen) = AssignmentSlotSelector.PickBest(candidates, soldier.Positions);
            result.PositionFit = fit;
            if (fit == PositionFitKind.None)
            {
                result.Violations.Add(AssignmentConstraintCodes.PositionMismatch);
            }
            else if (fit == PositionFitKind.Similar)
            {
                result.Warnings.Add(AssignmentConstraintCodes.SimilarPosition);
            }
            if (chosen != null && chosen.Missing > 0)
            {
                result.SuggestedMissionPositionId = chosen.Slot.MissionPositionId;
                result.SuggestedPosition = (int)chosen.Slot.Position;
            }
        }

        private static void EvaluateRest(ConstraintInstance instance, List<ConstraintAssignment> others, AssignmentValidationDto result)
        {
            var previous = others
                .Where(a => a.To <= instance.From)
                .OrderByDescending(a => a.To)
                .FirstOrDefault();
            if (previous != null)
            {
                var gap = (instance.From - previous.To).TotalHours;
                var required = previous.RequiredRestAfter ?? DefaultRequiredRestHours;
                result.RestBeforeHours = Math.Round(gap, 2);
                result.RequiredRestBeforeHours = required;
                if (gap < required)
                {
                    result.Violations.Add(AssignmentConstraintCodes.RestBefore);
                }
            }

            var next = others
                .Where(a => a.From >= instance.To)
                .OrderBy(a => a.From)
                .FirstOrDefault();
            if (next != null)
            {
                var gap = (next.From - instance.To).TotalHours;
                var required = instance.RequiredRestAfter ?? DefaultRequiredRestHours;
                result.RestAfterHours = Math.Round(gap, 2);
                result.RequiredRestAfterHours = required;
                if (gap < required)
                {
                    result.Violations.Add(AssignmentConstraintCodes.RestAfter);
                }
            }
        }
    }
}
