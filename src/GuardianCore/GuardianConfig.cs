using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GuardianCore
{
    /// <summary>
    /// SPEC section 4: no defaults, ever. A field that is missing, empty, unparseable or out of range
    /// does not fall back to a plausible value - there is no plausible value for someone else's risk
    /// limit. Every rejection reason is collected, not just the first: a trader fixing one typo at a
    /// time is a trader who is not trading with protection on.
    /// </summary>
    public sealed class GuardianConfig
    {
        public const int SupportedSchemaVersion = 1;

        public static readonly string[] RequiredKeys =
        {
            "schemaVersion", "accounts", "currency", "firmDailyLossLimit", "personalDailyLossLimit",
            "sessionResetTimeZone", "sessionResetLocalTime", "ledgerPath", "statePath", "pnlToleranceUsd"
        };

        public IReadOnlyList<string> Accounts { get; private set; }
        public string Currency { get; private set; }
        public decimal FirmDailyLossLimit { get; private set; }
        public decimal PersonalDailyLossLimit { get; private set; }
        public string SessionResetTimeZone { get; private set; }
        public TimeSpan SessionResetLocalTime { get; private set; }
        public string LedgerPath { get; private set; }
        public string StatePath { get; private set; }
        public decimal PnlToleranceUsd { get; private set; }

        /// <summary>The exact text that was validated. The seal hashes this, not a re-serialisation
        /// of the parsed object (SPEC 7.1).</summary>
        public string RawText { get; private set; }

        /// <summary>Canonical form of the config, used for the seal hash so that whitespace and key
        /// order cannot change the hash of an unchanged configuration.</summary>
        public string Canonical { get; private set; }

        private GuardianConfig() { }

        public static ConfigResult Parse(string text, Func<string, TimeZoneInfo> zoneLookup = null)
        {
            var reasons = new List<string>();
            if (text == null) return ConfigResult.Rejected(new[] { "config is null" });

            if (!JsonParser.TryParse(text, out var value, out var jsonError))
                return ConfigResult.Rejected(new[] { "config is not valid JSON: " + jsonError });
            if (!(value is JsonObject o))
                return ConfigResult.Rejected(new[] { "config root must be an object" });

            // Rule 1: any unknown key is a rejection. A typo'd key is a rule the user thinks is active.
            foreach (var key in o.Keys.OrderBy(k => k, StringComparer.Ordinal))
                if (!RequiredKeys.Contains(key, StringComparer.Ordinal))
                    reasons.Add("unknown key '" + key + "'");
            foreach (var key in RequiredKeys)
                if (!o.Has(key)) reasons.Add("missing key '" + key + "'");

            // Rule 2: unknown schema version - never best-effort on a schema we do not understand.
            var schema = o.GetInt("schemaVersion");
            if (o.Has("schemaVersion") && !schema.HasValue) reasons.Add("'schemaVersion' must be an integer");
            else if (schema.HasValue && schema.Value != SupportedSchemaVersion)
                reasons.Add("unsupported schemaVersion " + schema.Value.ToString(CultureInfo.InvariantCulture) +
                            " (this build supports " + SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture) + ")");

            var cfg = new GuardianConfig { RawText = text };

            // accounts
            if (o["accounts"] is JsonArray accounts)
            {
                var names = new List<string>();
                foreach (var item in accounts.Items)
                {
                    if (item is JsonString s && !string.IsNullOrWhiteSpace(s.Value)) names.Add(s.Value);
                    else reasons.Add("'accounts' must contain non-empty strings");
                }
                if (names.Count == 0) reasons.Add("'accounts' must not be empty");
                if (names.Count != names.Distinct(StringComparer.Ordinal).Count()) reasons.Add("'accounts' contains duplicates");
                // M16, and it is a REFUSAL rather than a limitation quietly honoured by half. The
                // adapter subscribes to one account, so a second one would have its post-lockout
                // orders left uncancelled and an open position invisible until something realises.
                // Accepting the config and keeping half the promise is the worst of the options;
                // refusing is reversible the day multi-account is actually supported, and Core's
                // own plural handling is left in place for that day (see OnOrderObserved).
                if (names.Count > 1)
                    reasons.Add("'accounts' lists " + names.Count + " accounts and only one is supported: " +
                                "the platform adapter watches a single account, so the others would be guarded " +
                                "only in part. This refusal is deliberate, not a bug");
                cfg.Accounts = names;
            }
            else if (o.Has("accounts")) reasons.Add("'accounts' must be an array");

            // currency
            var currency = o.GetString("currency");
            if (o.Has("currency") && string.IsNullOrWhiteSpace(currency)) reasons.Add("'currency' must be a non-empty string");
            cfg.Currency = currency;

            // money fields
            decimal firm = 0m, personal = 0m, tolerance = 0m;
            if (o.Has("firmDailyLossLimit"))
            {
                var raw = o.GetString("firmDailyLossLimit");
                if (raw == null) reasons.Add("'firmDailyLossLimit' must be a decimal string, not a number (SPEC 11.2)");
                else if (!Money.TryParse(raw, out firm)) reasons.Add("'firmDailyLossLimit' is not a decimal with at most 2 places: '" + raw + "'");
                else if (firm <= 0m) reasons.Add("'firmDailyLossLimit' must be greater than 0");
            }
            if (o.Has("personalDailyLossLimit"))
            {
                var raw = o.GetString("personalDailyLossLimit");
                if (raw == null) reasons.Add("'personalDailyLossLimit' must be a decimal string, not a number (SPEC 11.2)");
                else if (!Money.TryParse(raw, out personal)) reasons.Add("'personalDailyLossLimit' is not a decimal with at most 2 places: '" + raw + "'");
                else if (personal <= 0m) reasons.Add("'personalDailyLossLimit' must be greater than 0");
            }
            // Rule 3: the whole product is the gap between those two numbers.
            if (firm > 0m && personal > 0m && personal >= firm)
                reasons.Add("'personalDailyLossLimit' (" + Money.Format(personal) + ") must be STRICTLY LESS than " +
                            "'firmDailyLossLimit' (" + Money.Format(firm) + "); without a gap there is nothing to protect");
            cfg.FirmDailyLossLimit = firm;
            cfg.PersonalDailyLossLimit = personal;

            if (o.Has("pnlToleranceUsd"))
            {
                var raw = o.GetString("pnlToleranceUsd");
                if (raw == null) reasons.Add("'pnlToleranceUsd' must be a decimal string, not a number (SPEC 11.2)");
                else if (!Money.TryParse(raw, out tolerance)) reasons.Add("'pnlToleranceUsd' is not a decimal with at most 2 places: '" + raw + "'");
                else if (tolerance < 0m) reasons.Add("'pnlToleranceUsd' must be zero or greater");
            }
            cfg.PnlToleranceUsd = tolerance;

            // Rule 5b: time zone must be in the embedded map, and must resolve on this machine.
            var tz = o.GetString("sessionResetTimeZone");
            if (o.Has("sessionResetTimeZone"))
            {
                if (string.IsNullOrWhiteSpace(tz)) reasons.Add("'sessionResetTimeZone' must be a non-empty IANA id");
                else if (!TimeZoneMap.TryResolve(tz, out _, out var tzError, zoneLookup)) reasons.Add(tzError);
            }
            cfg.SessionResetTimeZone = tz;

            var resetTime = o.GetString("sessionResetLocalTime");
            if (o.Has("sessionResetLocalTime"))
            {
                if (resetTime == null || !TimeSpan.TryParseExact(resetTime, "hh\\:mm", CultureInfo.InvariantCulture, out var parsedTime))
                    reasons.Add("'sessionResetLocalTime' must be HH:mm, got '" + (resetTime ?? "null") + "'");
                else cfg.SessionResetLocalTime = parsedTime;
            }

            foreach (var pathKey in new[] { "ledgerPath", "statePath" })
            {
                if (!o.Has(pathKey)) continue;
                var p = o.GetString(pathKey);
                if (string.IsNullOrWhiteSpace(p)) reasons.Add("'" + pathKey + "' must be a non-empty path");
                else if (pathKey == "ledgerPath") cfg.LedgerPath = p;
                else cfg.StatePath = p;
            }

            if (reasons.Count > 0) return ConfigResult.Rejected(reasons);

            cfg.Canonical = o.ToCanonical();
            return ConfigResult.Accepted(cfg);
        }

        /// <summary>SPEC 7.1: the seal hashes the canonical form of the configuration.</summary>
        public string Hash() => Hashing.Sha256Hex(Canonical);
    }

    public sealed class ConfigResult
    {
        public bool Ok { get; }
        public GuardianConfig Config { get; }
        public IReadOnlyList<string> Reasons { get; }

        private ConfigResult(bool ok, GuardianConfig config, IReadOnlyList<string> reasons)
        { Ok = ok; Config = config; Reasons = reasons ?? new List<string>(); }

        public static ConfigResult Accepted(GuardianConfig c) => new ConfigResult(true, c, null);
        public static ConfigResult Rejected(IEnumerable<string> reasons) => new ConfigResult(false, null, reasons.ToList());
        public override string ToString() => Ok ? "OK" : string.Join("; ", Reasons);
    }
}
