using BL.Models.Schedule;
using DataLayer.Models;

namespace BL.Services
{
    /// <summary>
    /// Result of <see cref="MissionSlotPlanner.Plan"/>: existing slots to update in place, new slots to add
    /// (<see cref="ConstraintSlot.MissionPositionId"/> 0) and existing slot IDs to remove.
    /// </summary>
    public sealed record SlotPlan(IReadOnlyList<ConstraintSlot> Updated, IReadOnlyList<ConstraintSlot> Added, IReadOnlyList<int> RemovedIds)
    {
        /// <summary>Soldiers required per instance after the change.</summary>
        public int RequiredCount => Updated.Sum(s => s.Count) + Added.Sum(s => s.Count);
    }

    /// <summary>
    /// Pure planning of a mission's position slots. The requested list is the complete desired list: entries with an Id update
    /// that slot, entries without one add a slot, and existing slots missing from the list are removed (the caller must reject
    /// removing a slot that assignments reference). Validates enum values, counts (≥ 1), unknown or repeated IDs and repeated
    /// positions.
    /// </summary>
    public static class MissionSlotPlanner
    {
        /// <param name="existing">The mission's current slots (empty when creating a mission).</param>
        /// <exception cref="ArgumentException">Empty list or an invalid entry.</exception>
        public static SlotPlan Plan(IReadOnlyCollection<ConstraintSlot> existing, IReadOnlyCollection<MissionSlotInput>? requested)
        {
            if (requested == null || requested.Count == 0)
            {
                throw new ArgumentException("At least one position slot is required.");
            }
            foreach (var slot in requested)
            {
                if (!Enum.IsDefined(typeof(Position), slot.Position))
                {
                    throw new ArgumentException($"Unknown position {slot.Position}.");
                }
                if (slot.Count < 1)
                {
                    throw new ArgumentException($"Position {slot.Position}: count must be at least 1.");
                }
                if (slot.Id.HasValue && !existing.Any(e => e.MissionPositionId == slot.Id.Value))
                {
                    throw new ArgumentException($"Position slot {slot.Id.Value} is not a slot of this mission.");
                }
            }
            var repeatedId = requested.Where(s => s.Id.HasValue).GroupBy(s => s.Id!.Value).FirstOrDefault(g => g.Count() > 1);
            if (repeatedId != null)
            {
                throw new ArgumentException($"Position slot {repeatedId.Key} appears more than once.");
            }
            var repeatedPosition = requested.GroupBy(s => s.Position).FirstOrDefault(g => g.Count() > 1);
            if (repeatedPosition != null)
            {
                throw new ArgumentException($"Position {repeatedPosition.Key} appears more than once; use one slot with a higher count.");
            }

            var updated = requested
                .Where(s => s.Id.HasValue)
                .Select(s => new ConstraintSlot(s.Id!.Value, (Position)s.Position, s.Count))
                .ToList();
            var added = requested
                .Where(s => !s.Id.HasValue)
                .Select(s => new ConstraintSlot(0, (Position)s.Position, s.Count))
                .ToList();
            var removed = existing
                .Where(e => updated.All(u => u.MissionPositionId != e.MissionPositionId))
                .Select(e => e.MissionPositionId)
                .ToList();
            return new SlotPlan(updated, added, removed);
        }
    }
}
