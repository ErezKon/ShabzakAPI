using BL.Models.Schedule;

namespace ShabzakAPI.Tests
{
    public class ScheduleDateFormatTests
    {
        [Theory]
        [InlineData("2026-11-15T08:30:00")]
        [InlineData("2026-11-15T08:30")]
        [InlineData(" 2026-11-15T08:30:00 ")]
        public void ParseDateOrDateTime_UsesDatetimesAsIs(string value)
        {
            Assert.Equal(new DateTime(2026, 11, 15, 8, 30, 0), ScheduleDateFormat.ParseDateOrDateTime(value, TimeSpan.FromHours(6), "from"));
        }

        [Fact]
        public void ParseDateOrDateTime_DateTakesTheGivenTimeOfDay()
        {
            Assert.Equal(new DateTime(2026, 11, 15, 6, 0, 0), ScheduleDateFormat.ParseDateOrDateTime("2026-11-15", TimeSpan.FromHours(6), "from"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("2026-11-15T08:00:00Z")]
        [InlineData("2026-11-15T08:00:00+02:00")]
        [InlineData("15/11/2026 08:00")]
        [InlineData("2026-11-15 08:00")]
        public void ParseDateOrDateTime_RejectsMissingValuesOffsetsAndOtherFormats(string? value)
        {
            var ex = Assert.Throws<ArgumentException>(() => ScheduleDateFormat.ParseDateOrDateTime(value, TimeSpan.Zero, "to"));
            Assert.Contains("'to'", ex.Message);
        }

        [Theory]
        [InlineData("08:00", 8, 0)]
        [InlineData("8:05", 8, 5)]
        [InlineData("23:59", 23, 59)]
        [InlineData(" 00:00 ", 0, 0)]
        public void TryParseTimeOfDay_AcceptsDailyTimes(string value, int hours, int minutes)
        {
            Assert.True(ScheduleDateFormat.TryParseTimeOfDay(value, out var time));
            Assert.Equal(new TimeSpan(hours, minutes, 0), time);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("N/A")]
        [InlineData("24:00")]
        [InlineData("08:00:00")]
        [InlineData("8")]
        public void TryParseTimeOfDay_RejectsAnythingElse(string? value)
        {
            Assert.False(ScheduleDateFormat.TryParseTimeOfDay(value, out _));
        }

        [Fact]
        public void FormatTimeOfDay_IsTwoDigitHoursAndMinutes()
        {
            Assert.Equal("08:05", ScheduleDateFormat.FormatTimeOfDay(new TimeSpan(8, 5, 0)));
        }
    }
}
