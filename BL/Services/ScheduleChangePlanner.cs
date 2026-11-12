using BL.Models.Schedule;

namespace BL.Services
{
    /// <summary>
    /// A soldier placement that a schedule write validates and then applies: the target instance, the slot the soldier
    /// would take, and the evaluator options (requested slot, freed seat, instances the soldier leaves).
    /// </summary>
    public sealed record PlannedPlacement(ConstraintInstance Instance, ConstraintSoldier Soldier, int? SlotId, ConstraintOptions Options);

    /// <summary>
    /// Which violation codes stop a write. <see cref="Blocking"/> null means every violation blocks; <see cref="Hard"/>
    /// codes block even when the caller allows violations.
    /// </summary>
    public sealed record BlockingPolicy(IReadOnlySet<string>? Blocking, IReadOnlySet<string> Hard)
    {
        /// <summary>Assign / replace / swap / move: every violation blocks; ALREADY_ASSIGNED and NOT_FOUND can't be overridden.</summary>
        public static BlockingPolicy Placement { get; } = new(
            null,
            new HashSet<string> { AssignmentConstraintCodes.AlreadyAssigned, AssignmentConstraintCodes.NotFound });

        /// <summary>Changing an instance's times: only OVERLAP of an assigned soldier blocks; other violations are reported.</summary>
        public static BlockingPolicy Reschedule { get; } = new(
            new HashSet<string> { AssignmentConstraintCodes.Overlap },
            new HashSet<string>());
    }

    /// <summary>
    /// Pure (no DB access) planning of the schedule writes: chooses the target slot (<see cref="AssignmentSlotSelector"/>) and
    /// the <see cref="ConstraintOptions"/> each placement is validated with, and decides whether validations block the write.
    /// <list type="bullet">
    /// <item>Replace: the old soldier's seat is freed (<c>IgnoreSoldierId</c>) and preferred for the new soldier.</item>
    /// <item>Swap: two replacements; each soldier also leaves their own instance (<c>ExcludeInstanceIds</c>).</item>
    /// <item>Move: the soldier leaves the source instance (excluded); the same slot is preferred when both instances share it.</item>
    /// <item>Reschedule: every assigned soldier is re-validated in their own slot with their own seat freed.</item>
    /// </list>
    /// </summary>
    public static class ScheduleChangePlanner
    {
        /// <summary>Assign a soldier: the requested slot, otherwise the best free fit.</summary>
        public static PlannedPlacement Assign(ConstraintInstance instance, ConstraintSoldier soldier, int? requestedSlotId = null)
        {
            var slot = AssignmentSlotSelector.SelectTargetSlot(instance, soldier.Positions, requestedSlotId);
            return new PlannedPlacement(instance, soldier, slot, new ConstraintOptions(MissionPositionId: slot));
        }

        /// <summary>
        /// <paramref name="newSoldier"/> takes <paramref name="oldSoldierId"/>'s place in <paramref name="instance"/>.
        /// </summary>
        /// <param name="leavingInstanceIds">Instances the new soldier leaves in the same change (swap).</param>
        public static PlannedPlacement Replace(
            ConstraintInstance instance,
            int oldSoldierId,
            ConstraintSoldier newSoldier,
            IReadOnlyCollection<int>? leavingInstanceIds = null)
        {
            var vacatedSlot = instance.Assignments.FirstOrDefault(a => a.SoldierId == oldSoldierId)?.MissionPositionId;
            var slot = AssignmentSlotSelector.SelectTargetSlot(instance, newSoldier.Positions, preferredSlotId: vacatedSlot, ignoreSoldierId: oldSoldierId);
            return new PlannedPlacement(instance, newSoldier, slot, new ConstraintOptions(slot, oldSoldierId, leavingInstanceIds));
        }

        /// <summary>
        /// Swap: <paramref name="newSoldier"/> (assigned to <paramref name="swapInstance"/>) takes <paramref name="oldSoldier"/>'s
        /// place in <paramref name="original"/>, and <paramref name="oldSoldier"/> takes theirs in <paramref name="swapInstance"/>.
        /// </summary>
        public static (PlannedPlacement NewIntoOriginal, PlannedPlacement OldIntoSwap) Swap(
            ConstraintInstance original,
            ConstraintSoldier oldSoldier,
            ConstraintInstance swapInstance,
            ConstraintSoldier newSoldier) =>
            (Replace(original, oldSoldier.SoldierId, newSoldier, [swapInstance.InstanceId]),
             Replace(swapInstance, newSoldier.SoldierId, oldSoldier, [original.InstanceId]));

        /// <summary>Move <paramref name="soldier"/> from <paramref name="fromInstanceId"/> (seat <paramref name="currentSlotId"/>) into <paramref name="target"/>.</summary>
        public static PlannedPlacement Move(
            ConstraintInstance target,
            ConstraintSoldier soldier,
            int fromInstanceId,
            int? currentSlotId,
            int? requestedSlotId = null)
        {
            var sameSlot = currentSlotId.HasValue && target.Slots.Any(s => s.MissionPositionId == currentSlotId.Value) ? currentSlotId : null;
            var slot = AssignmentSlotSelector.SelectTargetSlot(target, soldier.Positions, requestedSlotId, sameSlot);
            return new PlannedPlacement(target, soldier, slot, new ConstraintOptions(slot, ExcludeInstanceIds: [fromInstanceId]));
        }

        /// <summary>
        /// Re-validates the soldiers assigned to <paramref name="instanceAtNewTimes"/> (an instance whose times change) in their own
        /// slots. Soldiers missing from <paramref name="soldiers"/> are skipped (the caller reports them as NOT_FOUND).
        /// </summary>
        public static List<PlannedPlacement> Reschedule(ConstraintInstance instanceAtNewTimes, IReadOnlyDictionary<int, ConstraintSoldier> soldiers) =>
            instanceAtNewTimes.Assignments
                .GroupBy(a => a.SoldierId)
                .Where(g => soldiers.ContainsKey(g.Key))
                .Select(g => new PlannedPlacement(
                    instanceAtNewTimes,
                    soldiers[g.Key],
                    g.First().MissionPositionId,
                    new ConstraintOptions(g.First().MissionPositionId, g.Key)))
                .ToList();

        /// <summary>
        /// Why the write must not be applied, or null when it may proceed. Hard codes always block; the other blocking codes
        /// block unless <paramref name="allowViolations"/> is set. The message contains soldier IDs and codes only.
        /// </summary>
        public static string? BlockReason(IReadOnlyCollection<AssignmentValidationDto> validations, bool allowViolations, BlockingPolicy policy)
        {
            var hard = Describe(validations, policy.Hard);
            if (hard != null)
            {
                return $"Cannot apply the change: {hard}. These violations cannot be overridden.";
            }
            if (allowViolations)
            {
                return null;
            }
            var blocking = Describe(validations, policy.Blocking);
            return blocking == null ? null : $"Blocked by constraint violations: {blocking}. Set allowViolations=true to override.";
        }

        private static string? Describe(IEnumerable<AssignmentValidationDto> validations, IReadOnlySet<string>? codes)
        {
            var parts = validations
                .Select(v => (v.SoldierId, Codes: v.Violations.Where(c => codes == null || codes.Contains(c)).ToList()))
                .Where(v => v.Codes.Count > 0)
                .Select(v => $"soldier {v.SoldierId}: {string.Join(", ", v.Codes)}")
                .ToList();
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
    }
}
