using System.Globalization;

namespace BL.Models.Schedule
{
    /// <summary>
    /// Datetime format of the agent-facing endpoints: ISO local wall-clock (Asia/Jerusalem) without offset,
    /// e.g. <c>2026-11-12T08:00:00</c>. Mission daily times (FromTime/ToTime) are <c>HH:mm</c>.
    /// </summary>
    public static class ScheduleDateFormat
    {
        public const string Format = "yyyy-MM-dd'T'HH:mm:ss";

        /// <summary>Date-only form accepted where a range boundary may default to a mission's daily time.</summary>
        public const string DateFormat = "yyyy-MM-dd";

        private static readonly string[] DateTimeFormats = [Format, "yyyy-MM-dd'T'HH:mm"];
        private static readonly string[] TimeOfDayFormats = ["H:mm", "HH:mm"];

        public static string ToIso(DateTime value) => value.ToString(Format, CultureInfo.InvariantCulture);

        public static string? ToIso(DateTime? value) => value.HasValue ? ToIso(value.Value) : null;

        /// <summary>
        /// Parses <c>yyyy-MM-ddTHH:mm[:ss]</c> (used as is) or <c>yyyy-MM-dd</c> (that date at <paramref name="timeOfDay"/>).
        /// Offsets and other formats are rejected.
        /// </summary>
        /// <param name="name">Field name used in the error message.</param>
        /// <exception cref="ArgumentException">Missing or invalid value.</exception>
        public static DateTime ParseDateOrDateTime(string? value, TimeSpan timeOfDay, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"'{name}' is required.");
            }
            var text = value.Trim();
            if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
            {
                return dateTime;
            }
            if (DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date.Date + timeOfDay;
            }
            throw new ArgumentException($"'{name}' must be a local date (yyyy-MM-dd) or datetime (yyyy-MM-ddTHH:mm:ss) without an offset.");
        }

        /// <summary>Parses a daily time <c>H:mm</c> / <c>HH:mm</c> (00:00–23:59).</summary>
        public static bool TryParseTimeOfDay(string? value, out TimeSpan time)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && DateTime.TryParseExact(value.Trim(), TimeOfDayFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                time = parsed.TimeOfDay;
                return true;
            }
            time = default;
            return false;
        }

        /// <summary>Formats a daily time as <c>HH:mm</c>.</summary>
        public static string FormatTimeOfDay(TimeSpan time) => time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }
}
