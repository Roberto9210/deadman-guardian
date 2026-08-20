using System;
using System.Globalization;

namespace GuardianCore
{
    /// <summary>
    /// Money parsing and formatting. SPEC section 4 rule 7 and G21: money is decimal, never double,
    /// and is written to the ledger as a string with exactly two decimals (SPEC section 11.2).
    /// </summary>
    public static class Money
    {
        /// <summary>Parses a decimal string with at most 2 decimal places. Fail-closed: no culture guessing,
        /// no rounding of a third decimal, no leading/trailing whitespace tolerance.</summary>
        public static bool TryParse(string text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrEmpty(text)) return false;
            if (text != text.Trim()) return false;
            if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                  CultureInfo.InvariantCulture, out var parsed)) return false;
            if (Decimals(parsed) > 2) return false;
            value = parsed;
            return true;
        }

        /// <summary>Canonical money rendering: two decimals, invariant culture, no thousands separators.</summary>
        public static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private static int Decimals(decimal d)
        {
            // decimal.GetBits()[3] carries the scale in bits 16-23.
            var bits = decimal.GetBits(d);
            return (bits[3] >> 16) & 0xFF;
        }
    }
}
