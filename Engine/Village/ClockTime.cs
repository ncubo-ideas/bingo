using System;
using System.Globalization;

namespace Village
{
    public sealed class ClockTime : IEquatable<ClockTime>
    {
        public ClockTime(int hour, int minute)
        {
            if (hour < 0 || hour > 23)
                throw new VillageRuleException("a clock reads an hour of the day, not " + hour);

            if (minute < 0 || minute > 59)
                throw new VillageRuleException("a clock reads a minute of the hour, not " + minute);

            Hour = hour;
            Minute = minute;
        }

        public int Hour { get; }

        public int Minute { get; }

        public bool Equals(ClockTime other) => other != null && other.Hour == Hour && other.Minute == Minute;

        public override bool Equals(object obj) => Equals(obj as ClockTime);

        public override int GetHashCode() => HashCode.Combine(Hour, Minute);

        public override string ToString() =>
            Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + Minute.ToString("00", CultureInfo.InvariantCulture);
    }
}
