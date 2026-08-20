using System;
using System.Globalization;

namespace GuardianCore
{
    /// <summary>
    /// SPEC section 5.1: the trading day runs [reset time on D, reset time on D+1) in the configured
    /// zone, DST-aware. Labelled by the date on which the session ENDS, which is the convention prop
    /// firms use when they say "your Tuesday".
    /// </summary>
    public sealed class SessionCalendar
    {
        private readonly TimeZoneInfo _zone;
        private readonly TimeSpan _resetLocal;

        public SessionCalendar(TimeZoneInfo zone, TimeSpan resetLocalTime)
        {
            _zone = zone ?? throw new ArgumentNullException(nameof(zone));
            _resetLocal = resetLocalTime;
        }

        public static bool TryCreate(GuardianConfig config, out SessionCalendar calendar, out string error,
                                     Func<string, TimeZoneInfo> lookup = null)
        {
            calendar = null;
            if (!TimeZoneMap.TryResolve(config.SessionResetTimeZone, out var zone, out error, lookup)) return false;
            calendar = new SessionCalendar(zone, config.SessionResetLocalTime);
            return true;
        }

        /// <summary>The key of the trading day that contains this instant.</summary>
        public string DayKey(DateTime utc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);
            var date = local.TimeOfDay >= _resetLocal ? local.Date.AddDays(1) : local.Date;
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>The instant at which the trading day containing <paramref name="utc"/> ends.
        /// DST-aware: computed in local time and converted back, so 17:00 local stays 17:00 local
        /// across the March and November transitions.</summary>
        public DateTime SessionEndUtc(DateTime utc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);
            var endDate = local.TimeOfDay >= _resetLocal ? local.Date.AddDays(1) : local.Date;
            return ToUtc(endDate.Add(_resetLocal));
        }

        private DateTime ToUtc(DateTime local)
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            // A local time inside the spring-forward gap does not exist; step forward until it does.
            // Fail-closed direction: a later boundary means protection stays on longer (SPEC 7.5).
            var probe = unspecified;
            for (int i = 0; i < 4 && _zone.IsInvalidTime(probe); i++) probe = probe.AddMinutes(30);
            return TimeZoneInfo.ConvertTimeToUtc(probe, _zone);
        }
    }
}
