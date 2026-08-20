using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GuardianCore
{
    /// <summary>SHA-256 over UTF-8, rendered as 64 lowercase hex characters (SPEC section 11.1).</summary>
    public static class Hashing
    {
        public const string Genesis = "genesis";

        public static string Sha256Hex(string utf8Text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = new UTF8Encoding(false).GetBytes(utf8Text);
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }

    /// <summary>Timestamps: ISO-8601 UTC with milliseconds and a trailing Z (SPEC section 11.2).</summary>
    public static class Iso
    {
        public const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

        public static string Utc(DateTime utc) =>
            DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString(Format, CultureInfo.InvariantCulture);

        public static bool TryParseUtc(string text, out DateTime utc)
        {
            utc = default(DateTime);
            if (string.IsNullOrEmpty(text)) return false;
            if (!DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)) return false;
            utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }
    }
}
