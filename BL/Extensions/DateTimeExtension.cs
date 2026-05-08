using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Extensions
{
    /// <summary>
    /// Extension methods for DateTime comparison.
    /// </summary>
    public static class DateTimeExtension
    {
        /// <summary>
        /// Checks if a date falls within a range (inclusive on both ends).
        /// </summary>
        /// <param name="date">The date to check.</param>
        /// <param name="start">Range start (inclusive).</param>
        /// <param name="end">Range end (inclusive).</param>
        /// <returns>True if date is between start and end.</returns>
        public static bool IsBetweenDates(this DateTime date, DateTime start, DateTime end)
        {
            return date >= start && date <= end;
        }
    }
}
