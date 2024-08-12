using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Extensions
{
    public static class DateTimeExtension
    {
        public static bool IsBetweenDates(this DateTime date, DateTime start, DateTime end)
        {
            return date >= start && date <= end;
        }
    }
}
