using BL.Models.Schedule;

namespace BL.Services
{
    /// <summary>A half-open time interval [<see cref="From"/>, <see cref="To"/>).</summary>
    public sealed record TimeSlot(DateTime From, DateTime To)
    {
        /// <summary>Half-open overlap: touching intervals do not overlap.</summary>
        public bool Overlaps(TimeSlot other) => From < other.To && other.From < To;
    }

    /// <summary>Slots to create, plus how many generated slots were skipped because they overlap existing instances.</summary>
    public sealed record InstanceGenerationResult(IReadOnlyList<TimeSlot> Slots, int SkippedExisting);

    /// <summary>
    /// Pure instance generation, mirroring the mission dialog of the UI (<c>autoCreateInstances</c>): continuous back-to-back
    /// slots of <c>duration</c> hours from the range start, the last one truncated at the range end. Crossing midnight and
    /// durations over 24h need no special handling. Slots overlapping existing instances are skipped (not trimmed).
    /// </summary>
    public static class MissionInstanceGenerator
    {
        /// <summary>Maximum span of a generation range (and of a single instance).</summary>
        public const int MaxRangeDays = 62;

        /// <summary>Maximum number of slots per call (generated, including skipped ones).</summary>
        public const int MaxSlots = 2000;

        /// <summary>Number of slots returned as a preview.</summary>
        public const int PreviewSize = 50;

        /// <summary>
        /// Resolves a generation range. A date-only boundary takes the mission's daily time: <paramref name="from"/> its FromTime,
        /// <paramref name="to"/> its ToTime (as the UI does with its start/end dates); 00:00 when the mission has none.
        /// </summary>
        /// <exception cref="ArgumentException">Missing or invalid boundary.</exception>
        public static TimeSlot ResolveRange(string? from, string? to, string? missionFromTime, string? missionToTime)
        {
            ScheduleDateFormat.TryParseTimeOfDay(missionFromTime, out var startTime);
            ScheduleDateFormat.TryParseTimeOfDay(missionToTime, out var endTime);
            return new TimeSlot(
                ScheduleDateFormat.ParseDateOrDateTime(from, startTime, "from"),
                ScheduleDateFormat.ParseDateOrDateTime(to, endTime, "to"));
        }

        /// <summary>
        /// Generates the slots of [<paramref name="range"/>.From, <paramref name="range"/>.To).
        /// </summary>
        /// <param name="existing">Instances of the mission that generated slots must not overlap.</param>
        /// <exception cref="ArgumentException">Duration below 1h, empty or reversed range, range over <see cref="MaxRangeDays"/> days,
        /// or more than <paramref name="maxSlots"/> slots.</exception>
        public static InstanceGenerationResult Generate(TimeSlot range, int durationHours, IReadOnlyCollection<TimeSlot>? existing = null, int maxSlots = MaxSlots)
        {
            if (durationHours < 1)
            {
                throw new ArgumentException("The mission duration must be at least 1 hour.");
            }
            ValidateRange(range);
            var total = (int)Math.Ceiling((range.To - range.From).TotalHours / durationHours);
            if (total > maxSlots)
            {
                throw new ArgumentException($"The range would generate {total} instances; at most {maxSlots} are allowed per call.");
            }

            existing ??= [];
            var slots = new List<TimeSlot>();
            var skipped = 0;
            for (var start = range.From; start < range.To;)
            {
                var end = start.AddHours(durationHours);
                var slot = new TimeSlot(start, end < range.To ? end : range.To);
                if (existing.Any(slot.Overlaps))
                {
                    skipped++;
                }
                else
                {
                    slots.Add(slot);
                }
                start = slot.To;
            }
            return new InstanceGenerationResult(slots, skipped);
        }

        /// <summary>Checks that a range (or a single instance) is non-empty and at most <see cref="MaxRangeDays"/> days long.</summary>
        /// <exception cref="ArgumentException">Invalid range.</exception>
        public static void ValidateRange(TimeSlot range)
        {
            if (range.To <= range.From)
            {
                throw new ArgumentException($"'to' ({ScheduleDateFormat.ToIso(range.To)}) must be later than 'from' ({ScheduleDateFormat.ToIso(range.From)}).");
            }
            if ((range.To - range.From).TotalDays > MaxRangeDays)
            {
                throw new ArgumentException($"The range must be at most {MaxRangeDays} days.");
            }
        }
    }
}
