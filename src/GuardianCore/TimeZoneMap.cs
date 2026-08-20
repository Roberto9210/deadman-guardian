using System;
using System.Collections.Generic;
using System.Linq;

namespace GuardianCore
{
    /// <summary>
    /// SPEC section 5.1 - the IANA trap.
    ///
    /// GuardianCore runs inside NT8 on .NET Framework 4.8.1, where TimeZoneInfo.FindSystemTimeZoneById
    /// accepts ONLY Windows ids. Verified on the development machine under .NET Framework 4.8.9300:
    ///     FindSystemTimeZoneById("America/Chicago")       -> TimeZoneNotFoundException
    ///     FindSystemTimeZoneById("Central Standard Time") -> OK
    /// IANA ids resolve only on .NET 6+. A test suite on a modern runtime would therefore pass while
    /// the same configuration is rejected every time inside real NT8.
    ///
    /// So: the config keeps IANA ids, and Core carries this minimal embedded map. No dependency,
    /// no TimeZoneConverter package. The map grows by commit, never by guessing.
    /// </summary>
    public static class TimeZoneMap
    {
        private static readonly Dictionary<string, string> IanaToWindows =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "America/Chicago",  "Central Standard Time" },
                { "America/New_York", "Eastern Standard Time" },
                { "UTC",              "UTC" },
            };

        public static IReadOnlyList<string> SupportedIds => IanaToWindows.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        public static bool IsSupported(string ianaId) => ianaId != null && IanaToWindows.ContainsKey(ianaId);

        /// <summary>Resolution order of SPEC 5.1: try the id as given (works on .NET 6+), and on
        /// TimeZoneNotFoundException fall back to the mapped Windows id (the NT8 path).
        /// <paramref name="lookup"/> exists so a test can simulate the .NET Framework behaviour that
        /// the modern test runner cannot reproduce (G12).</summary>
        public static bool TryResolve(string ianaId, out TimeZoneInfo zone, out string error,
                                      Func<string, TimeZoneInfo> lookup = null)
        {
            zone = null; error = null;
            if (string.IsNullOrEmpty(ianaId)) { error = "time zone id is empty"; return false; }
            if (!IanaToWindows.TryGetValue(ianaId, out var windowsId))
            {
                error = "unsupported time zone id '" + ianaId + "'; supported: " + string.Join(", ", SupportedIds);
                return false;
            }

            lookup = lookup ?? TimeZoneInfo.FindSystemTimeZoneById;
            try
            {
                zone = lookup(ianaId);
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
                // The .NET Framework / NT8 path.
            }
            catch (InvalidTimeZoneException ex)
            {
                error = "time zone data for '" + ianaId + "' is corrupt: " + ex.Message;
                return false;
            }

            try
            {
                zone = lookup(windowsId);
                return true;
            }
            catch (Exception ex)
            {
                error = "neither '" + ianaId + "' nor '" + windowsId + "' resolves on this machine: " + ex.Message;
                return false;
            }
        }
    }
}
