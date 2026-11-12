using BL.Services;

namespace ShabzakAPI.Tests
{
    public class MissionInstanceGeneratorTests
    {
        private static readonly DateTime Day = new(2026, 11, 15);

        private static TimeSlot Slot(double fromHour, double toHour) => new(Day.AddHours(fromHour), Day.AddHours(toHour));

        private static string Hours(InstanceGenerationResult result) =>
            string.Join(" ", result.Slots.Select(s => $"{(s.From - Day).TotalHours}-{(s.To - Day).TotalHours}"));

        [Fact]
        public void Slots_AreContinuousAndBackToBack_FromTheRangeStart()
        {
            var result = MissionInstanceGenerator.Generate(Slot(8, 20), 4);

            Assert.Equal("8-12 12-16 16-20", Hours(result));
            Assert.Equal(0, result.SkippedExisting);
        }

        [Fact]
        public void LastSlot_IsTruncatedAtTheRangeEnd()
        {
            Assert.Equal("8-12 12-16 16-18", Hours(MissionInstanceGenerator.Generate(Slot(8, 18), 4)));
        }

        [Fact]
        public void Slots_CrossMidnight()
        {
            Assert.Equal("20-28 28-32", Hours(MissionInstanceGenerator.Generate(Slot(20, 32), 8)));
        }

        [Fact]
        public void DurationOver24Hours_SpansSeveralDays()
        {
            Assert.Equal("8-56 56-104 104-110", Hours(MissionInstanceGenerator.Generate(Slot(8, 110), 48)));
        }

        [Fact]
        public void DatesTakeTheMissionsDailyTimes_LikeTheUiDialog()
        {
            // Daily window 06:00–22:00, 8h shifts, dates 15 → 16: slots run continuously from 15 06:00 until 16 22:00 (as in the UI).
            var range = MissionInstanceGenerator.ResolveRange("2026-11-15", "2026-11-16", "06:00", "22:00");

            Assert.Equal(Slot(6, 46), range);
            Assert.Equal("6-14 14-22 22-30 30-38 38-46", Hours(MissionInstanceGenerator.Generate(range, 8)));
        }

        [Fact]
        public void RoundTheClockMission_EndsAtMidnightOfTheLastDate()
        {
            var range = MissionInstanceGenerator.ResolveRange("2026-11-15", "2026-11-17", "00:00", "00:00");
            var result = MissionInstanceGenerator.Generate(range, 4);

            Assert.Equal(12, result.Slots.Count);
            Assert.Equal(Slot(0, 4), result.Slots[0]);
            Assert.Equal(Slot(44, 48), result.Slots[^1]);
        }

        [Fact]
        public void ExplicitDatetimes_AreUsedAsIs_AndMissingMissionTimesMeanMidnight()
        {
            Assert.Equal(Slot(9.5, 30), MissionInstanceGenerator.ResolveRange("2026-11-15T09:30:00", "2026-11-16T06:00", "06:00", "22:00"));
            Assert.Equal(Slot(0, 24), MissionInstanceGenerator.ResolveRange("2026-11-15", "2026-11-16", null, "N/A"));
        }

        [Fact]
        public void SlotsOverlappingExistingInstances_AreSkippedAndCounted()
        {
            var existing = new[] { Slot(12, 16), Slot(17, 18) };

            var result = MissionInstanceGenerator.Generate(Slot(8, 24), 4, existing);

            Assert.Equal("8-12 20-24", Hours(result));
            Assert.Equal(2, result.SkippedExisting);
        }

        [Fact]
        public void TouchingExistingInstances_AreNotSkipped()
        {
            var result = MissionInstanceGenerator.Generate(Slot(8, 20), 4, [Slot(4, 8), Slot(20, 24)]);

            Assert.Equal(3, result.Slots.Count);
            Assert.Equal(0, result.SkippedExisting);
        }

        [Fact]
        public void RegeneratingTheSameRange_SkipsEverything()
        {
            var first = MissionInstanceGenerator.Generate(Slot(0, 24), 6);

            var second = MissionInstanceGenerator.Generate(Slot(0, 24), 6, first.Slots.ToList());

            Assert.Empty(second.Slots);
            Assert.Equal(4, second.SkippedExisting);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-4)]
        public void DurationBelowOneHour_Throws(int duration)
        {
            Assert.Throws<ArgumentException>(() => MissionInstanceGenerator.Generate(Slot(8, 20), duration));
        }

        [Theory]
        [InlineData(8, 8)]
        [InlineData(8, 6)]
        public void EmptyOrReversedRange_Throws(double fromHour, double toHour)
        {
            Assert.Throws<ArgumentException>(() => MissionInstanceGenerator.Generate(Slot(fromHour, toHour), 4));
        }

        [Fact]
        public void RangeLimit_Is62Days()
        {
            var limit = new TimeSlot(Day, Day.AddDays(MissionInstanceGenerator.MaxRangeDays));

            Assert.Equal(62, MissionInstanceGenerator.Generate(limit, 24).Slots.Count);
            Assert.Throws<ArgumentException>(() => MissionInstanceGenerator.Generate(limit with { To = limit.To.AddMinutes(1) }, 24));
        }

        [Fact]
        public void SlotLimit_CountsEveryGeneratedSlot()
        {
            Assert.Equal(5, MissionInstanceGenerator.Generate(Slot(0, 5), 1, maxSlots: 5).Slots.Count);
            Assert.Throws<ArgumentException>(() => MissionInstanceGenerator.Generate(Slot(0, 5.5), 1, maxSlots: 5));
            Assert.Throws<ArgumentException>(() => MissionInstanceGenerator.Generate(Slot(0, 6), 1, [Slot(0, 6)], maxSlots: 5));
        }

        [Theory]
        [InlineData(null, "2026-11-16")]
        [InlineData("2026-11-15", "")]
        [InlineData("15/11/2026", "2026-11-16")]
        [InlineData("2026-11-15T08:00:00Z", "2026-11-16")]
        public void ResolveRange_InvalidBoundary_Throws(string? from, string? to)
        {
            Assert.Throws<ArgumentException>(() => MissionInstanceGenerator.ResolveRange(from, to, "08:00", "20:00"));
        }
    }
}
