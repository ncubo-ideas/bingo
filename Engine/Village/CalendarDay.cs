using System;
using System.Globalization;

namespace Village
{
    public sealed class CalendarDay : IEquatable<CalendarDay>
    {
        private readonly DateOnly _day;

        public CalendarDay(int year, int month, int day)
        {
            _day = new DateOnly(year, month, day);
        }

        public int Year => _day.Year;

        public int Month => _day.Month;

        public int Day => _day.Day;

        public int DaysUntil(CalendarDay other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            return other._day.DayNumber - _day.DayNumber;
        }

        public bool IsBefore(CalendarDay other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            return _day < other._day;
        }

        public bool Equals(CalendarDay other) => other != null && other._day == _day;

        public override bool Equals(object obj) => Equals(obj as CalendarDay);

        public override int GetHashCode() => _day.GetHashCode();

        public override string ToString() => _day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
