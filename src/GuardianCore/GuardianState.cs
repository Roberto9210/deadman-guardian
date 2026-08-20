using System;
using System.Globalization;

namespace GuardianCore
{
    public enum StateKind { Disarmed, Armed, Locked, FailClosed }

    /// <summary>
    /// The commitment seal of SPEC section 7.
    ///
    /// configSnapshot is stored as the canonical TEXT of the configuration rather than as a nested
    /// object (SPEC 7.1 shows an object). Storing the exact bytes that were hashed removes any chance
    /// that a re-serialisation difference changes the hash of an unchanged configuration. Noted for
    /// amendment (A5).
    /// </summary>
    public sealed class Seal
    {
        public const int SealVersion = 1;

        public string SealHash { get; }
        public string ConfigSnapshot { get; }
        public DateTime ArmedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public string DayKey { get; }
        public string LedgerHeadHash { get; }
        public long MonoAtArmMs { get; }
        public long SealDurationMs { get; }
        public string RunId { get; }

        public Seal(string sealHash, string configSnapshot, DateTime armedAtUtc, DateTime expiresAtUtc,
                    string dayKey, string ledgerHeadHash, long monoAtArmMs, long sealDurationMs, string runId)
        {
            SealHash = sealHash; ConfigSnapshot = configSnapshot; ArmedAtUtc = armedAtUtc;
            ExpiresAtUtc = expiresAtUtc; DayKey = dayKey; LedgerHeadHash = ledgerHeadHash;
            MonoAtArmMs = monoAtArmMs; SealDurationMs = sealDurationMs; RunId = runId;
        }

        /// <summary>SPEC 7.4: recompute the hash of the stored snapshot and compare. A mismatch means
        /// the state file was edited by hand.</summary>
        public bool SnapshotMatchesHash() => Hashing.Sha256Hex(ConfigSnapshot ?? "") == SealHash;

        public JsonObject ToJson() =>
            JsonValue.Obj()
                .Set("sealVersion", SealVersion)
                .Set("sealHash", SealHash)
                .Set("configSnapshot", ConfigSnapshot)
                .Set("armedAtUtc", Iso.Utc(ArmedAtUtc))
                .Set("expiresAtUtc", Iso.Utc(ExpiresAtUtc))
                .Set("dayKey", DayKey)
                .Set("ledgerHeadHash", LedgerHeadHash)
                .Set("monoAtArmMs", MonoAtArmMs)
                .Set("sealDurationMs", SealDurationMs)
                .Set("runId", RunId);

        public static bool TryFromJson(JsonObject o, out Seal seal, out string error)
        {
            seal = null; error = null;
            if (o == null) { error = "seal is missing"; return false; }
            var version = o.GetInt("sealVersion");
            if (!version.HasValue || version.Value != SealVersion)
            { error = "unsupported sealVersion"; return false; }

            var hash = o.GetString("sealHash");
            var snapshot = o.GetString("configSnapshot");
            var dayKey = o.GetString("dayKey");
            var head = o.GetString("ledgerHeadHash");
            var runId = o.GetString("runId");
            var mono = o.GetInt("monoAtArmMs");
            var duration = o.GetInt("sealDurationMs");
            if (hash == null || snapshot == null || dayKey == null || head == null || runId == null
                || !mono.HasValue || !duration.HasValue)
            { error = "seal is missing a required field"; return false; }
            if (!Iso.TryParseUtc(o.GetString("armedAtUtc"), out var armed) ||
                !Iso.TryParseUtc(o.GetString("expiresAtUtc"), out var expires))
            { error = "seal has an unparseable timestamp"; return false; }

            seal = new Seal(hash, snapshot, armed, expires, dayKey, head, mono.Value, duration.Value, runId);
            return true;
        }
    }

    /// <summary>
    /// The persisted state of SPEC section 6. Written atomically, and always before the broker call it
    /// describes (SPEC 9.1) - that ordering is what makes a process killed mid-flatten come back locked.
    /// </summary>
    public sealed class PersistedState
    {
        public const int SchemaVersion = 1;

        public StateKind Kind { get; set; }
        public string DayKey { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public long LastMonotonicMs { get; set; }
        public string RunId { get; set; }
        public string Reason { get; set; }
        public Seal Seal { get; set; }
        public bool LockoutVerified { get; set; }
        public int FlattenAttempts { get; set; }

        public JsonObject ToJson()
        {
            var o = JsonValue.Obj()
                .Set("schemaVersion", SchemaVersion)
                .Set("state", Kind.ToString().ToUpperInvariant())
                .Set("dayKey", DayKey ?? "")
                .Set("lastSeenUtc", Iso.Utc(LastSeenUtc))
                .Set("lastMonotonicMs", LastMonotonicMs)
                .Set("runId", RunId ?? "")
                .Set("reason", Reason ?? "")
                .Set("lockoutVerified", LockoutVerified)
                .Set("flattenAttempts", FlattenAttempts);
            if (Seal != null) o.Set("seal", Seal.ToJson());
            return o;
        }

        /// <summary>SPEC 6.3: unreadable, missing-when-expected, or unknown schema is FAIL_CLOSED,
        /// never DISARMED. This method reports the failure; it never invents a state.</summary>
        public static bool TryParse(string text, out PersistedState state, out string error)
        {
            state = null; error = null;
            if (!JsonParser.TryParse(text, out var v, out var jsonError) || !(v is JsonObject o))
            { error = "state file is not valid JSON: " + jsonError; return false; }

            var schema = o.GetInt("schemaVersion");
            if (!schema.HasValue) { error = "state file has no schemaVersion"; return false; }
            if (schema.Value != SchemaVersion)
            { error = "unknown state schemaVersion " + schema.Value.ToString(CultureInfo.InvariantCulture); return false; }

            var kindText = o.GetString("state");
            if (kindText == null) { error = "state file has no state"; return false; }
            StateKind kind;
            switch (kindText)
            {
                case "DISARMED": kind = StateKind.Disarmed; break;
                case "ARMED": kind = StateKind.Armed; break;
                case "LOCKED": kind = StateKind.Locked; break;
                case "FAILCLOSED": kind = StateKind.FailClosed; break;
                default: error = "unknown state '" + kindText + "'"; return false;
            }

            if (!Iso.TryParseUtc(o.GetString("lastSeenUtc"), out var lastSeen))
            { error = "state file has an unparseable lastSeenUtc"; return false; }
            var mono = o.GetInt("lastMonotonicMs");
            if (!mono.HasValue) { error = "state file has no lastMonotonicMs"; return false; }

            Seal seal = null;
            if (o.Has("seal") && !Seal.TryFromJson(o["seal"] as JsonObject, out seal, out error)) return false;

            state = new PersistedState
            {
                Kind = kind,
                DayKey = o.GetString("dayKey"),
                LastSeenUtc = lastSeen,
                LastMonotonicMs = mono.Value,
                RunId = o.GetString("runId"),
                Reason = o.GetString("reason"),
                Seal = seal,
                LockoutVerified = (o["lockoutVerified"] as JsonBool)?.Value ?? false,
                FlattenAttempts = (int)(o.GetInt("flattenAttempts") ?? 0)
            };
            return true;
        }
    }
}
