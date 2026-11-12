using BL.Models.Schedule;
using DataLayer.Models;

namespace BL.Services
{
    /// <summary>A slot of an instance with its free capacity (<see cref="Missing"/> ≤ 0 means full).</summary>
    public sealed record SlotCapacity(ConstraintSlot Slot, int Missing);

    /// <summary>
    /// Pure slot ranking shared by <see cref="AssignmentConstraintEvaluator"/> (suggested slot) and the schedule writes
    /// (target slot). Fit uses <see cref="PositionHelper"/>: a slot position maps to the soldier positions allowed to fill it.
    /// Ranking: exact fit over similar fit, then most missing, then lowest slot ID.
    /// </summary>
    public static class AssignmentSlotSelector
    {
        private static readonly PositionHelper positionHelper = new();

        /// <summary>Free capacity of every slot of <paramref name="instance"/>, after freeing <paramref name="ignoreSoldierId"/>'s seat.</summary>
        public static List<SlotCapacity> Capacities(ConstraintInstance instance, int? ignoreSoldierId = null)
        {
            var assignments = instance.Assignments.Where(a => a.SoldierId != ignoreSoldierId).ToList();
            return instance.Slots
                .Select(s => new SlotCapacity(s, s.Count - assignments.Count(a => a.MissionPositionId == s.MissionPositionId)))
                .ToList();
        }

        /// <summary>How a soldier with <paramref name="soldierPositions"/> fits a slot of <paramref name="slotPosition"/>.</summary>
        public static string Fit(Position slotPosition, IReadOnlyCollection<Position> soldierPositions)
        {
            if (soldierPositions.Contains(slotPosition))
            {
                return PositionFitKind.Exact;
            }
            return positionHelper.GetSimilarPositions(slotPosition).Any(soldierPositions.Contains)
                ? PositionFitKind.Similar
                : PositionFitKind.None;
        }

        /// <summary>Best fitting candidate: exact over similar, then most missing, then lowest slot ID.</summary>
        public static (string Fit, SlotCapacity? Slot) PickBest(IEnumerable<SlotCapacity> candidates, IReadOnlyCollection<Position> soldierPositions)
        {
            var ranked = candidates
                .Select(c => (Capacity: c, Fit: Fit(c.Slot.Position, soldierPositions)))
                .Where(c => c.Fit != PositionFitKind.None)
                .OrderBy(c => c.Fit == PositionFitKind.Exact ? 0 : 1)
                .ThenByDescending(c => c.Capacity.Missing)
                .ThenBy(c => c.Capacity.Slot.MissionPositionId)
                .FirstOrDefault();
            return ranked.Capacity != null ? (ranked.Fit, ranked.Capacity) : (PositionFitKind.None, null);
        }

        /// <summary>
        /// Slot a write puts the soldier into. In order: the requested slot; the preferred slot (e.g. the seat vacated by a
        /// replaced soldier, or the same slot when moving between instances of one mission) when it is free and the soldier fits;
        /// the best fitting free slot; the preferred slot even without a fit; the free slot with most missing; the best fitting
        /// full slot; the lowest slot. Null only when the mission has no slots. The evaluator then reports the violations of
        /// the chosen slot (e.g. POSITION_MISMATCH or INSTANCE_FULL), which block the write unless overridden.
        /// </summary>
        public static int? SelectTargetSlot(
            ConstraintInstance instance,
            IReadOnlyCollection<Position> soldierPositions,
            int? requestedSlotId = null,
            int? preferredSlotId = null,
            int? ignoreSoldierId = null)
        {
            if (requestedSlotId.HasValue)
            {
                return requestedSlotId;
            }

            var slots = Capacities(instance, ignoreSoldierId);
            var free = slots.Where(s => s.Missing > 0).ToList();
            var preferred = preferredSlotId.HasValue ? free.FirstOrDefault(s => s.Slot.MissionPositionId == preferredSlotId.Value) : null;
            if (preferred != null && Fit(preferred.Slot.Position, soldierPositions) != PositionFitKind.None)
            {
                return preferred.Slot.MissionPositionId;
            }

            var chosen = PickBest(free, soldierPositions).Slot
                ?? preferred
                ?? free.OrderByDescending(s => s.Missing).ThenBy(s => s.Slot.MissionPositionId).FirstOrDefault()
                ?? PickBest(slots, soldierPositions).Slot
                ?? slots.OrderBy(s => s.Slot.MissionPositionId).FirstOrDefault();
            return chosen?.Slot.MissionPositionId;
        }
    }
}
