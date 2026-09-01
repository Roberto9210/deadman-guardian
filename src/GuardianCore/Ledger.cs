using System;
using System.Collections.Generic;
using System.Globalization;

namespace GuardianCore
{
    /// <summary>The event catalogue of SPEC section 12. Codes are data, not prose: they are what a
    /// later dispute is read from.</summary>
    public static class Ev
    {
        public const string GuardianStarted = "GUARDIAN_STARTED";
        public const string GuardianStopped = "GUARDIAN_STOPPED";
        public const string StateRestored = "STATE_RESTORED";
        public const string StateCorrupt = "STATE_CORRUPT";
        public const string ConfigLoaded = "CONFIG_LOADED";
        public const string ConfigRejected = "CONFIG_REJECTED";
        public const string Armed = "ARMED";
        public const string SealCreated = "SEAL_CREATED";
        public const string SealVerified = "SEAL_VERIFIED";
        public const string SealMismatch = "SEAL_MISMATCH";
        public const string ConfigTampered = "CONFIG_TAMPERED";
        public const string ConfigChangeRejected = "CONFIG_CHANGE_REJECTED";
        public const string DayOpened = "DAY_OPENED";
        public const string DayClosed = "DAY_CLOSED";
        public const string PnlCheckpoint = "PNL_CHECKPOINT";
        public const string PnlDisagreement = "PNL_DISAGREEMENT";
        public const string PnlUncomputable = "PNL_UNCOMPUTABLE";
        public const string AccountUnknown = "ACCOUNT_UNKNOWN";
        public const string ClockAnomaly = "CLOCK_ANOMALY";
        public const string ClockSuspect = "CLOCK_SUSPECT";
        public const string FailClosedEntered = "FAIL_CLOSED_ENTERED";
        public const string FailClosedCleared = "FAIL_CLOSED_CLEARED";
        public const string LimitBreached = "LIMIT_BREACHED";
        public const string OrdersCancelled = "ORDERS_CANCELLED";
        public const string FlattenRequested = "FLATTEN_REQUESTED";
        public const string FlattenVerified = "FLATTEN_VERIFIED";
        public const string LockoutIncomplete = "LOCKOUT_INCOMPLETE";
        public const string OrderRejectedLocked = "ORDER_REJECTED_LOCKED";
        public const string SealExpired = "SEAL_EXPIRED";
        public const string LockoutCleared = "LOCKOUT_CLEARED";
        public const string Disarmed = "DISARMED";
        public const string LedgerVerifyFailed = "LEDGER_VERIFY_FAILED";
        public const string NotifyFailed = "NOTIFY_FAILED";
        public const string ForeignAccountOrderObserved = "FOREIGN_ACCOUNT_ORDER_OBSERVED";
        public const string PnlBaselineAdopted = "PNL_BASELINE_ADOPTED";
        public const string PnlBaselineRefused = "PNL_BASELINE_REFUSED";
        public const string LimitBreachedBaselineOnly = "LIMIT_BREACHED_BASELINE_ONLY";
    }

    public sealed class LedgerEntry
    {
        public long Seq { get; }
        public DateTime TsUtc { get; }
        public string Event { get; }
        public int SchemaVersion { get; }
        public JsonObject Payload { get; }
        public string Prev { get; }
        public string Hash { get; }

        public LedgerEntry(long seq, DateTime tsUtc, string ev, int schemaVersion, JsonObject payload, string prev, string hash)
        {
            Seq = seq; TsUtc = tsUtc; Event = ev; SchemaVersion = schemaVersion;
            Payload = payload; Prev = prev; Hash = hash;
        }
    }

    public sealed class LedgerVerifyResult
    {
        public bool Ok { get; }
        public long? BrokenSeq { get; }
        public string Reason { get; }
        private LedgerVerifyResult(bool ok, long? brokenSeq, string reason)
        { Ok = ok; BrokenSeq = brokenSeq; Reason = reason; }

        public static LedgerVerifyResult Good() => new LedgerVerifyResult(true, null, null);
        public static LedgerVerifyResult Broken(long seq, string reason) => new LedgerVerifyResult(false, seq, reason);
        public override string ToString() => Ok ? "OK" : ("BROKEN at seq " + BrokenSeq + ": " + Reason);
    }

    /// <summary>Append-only, hash-chained (SPEC section 11). Every entry carries the SHA-256 of the
    /// previous one, so editing the past is detectable by anyone who re-runs Verify().</summary>
    public sealed class Ledger
    {
        public const int SchemaVersion = 1;

        private readonly IFileStore _store;
        private readonly string _path;
        private long _seq;
        private string _head = Hashing.Genesis;

        public Ledger(IFileStore store, string path)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _path = path ?? throw new ArgumentNullException(nameof(path));
            LoadHead();
        }

        public string Head => _head;
        public long LastSeq => _seq;

        /// <summary>Optional, best-effort, NEVER load-bearing: called after each successful append so
        /// an adapter can react to an event (the lockout messages are the reason it exists). It
        /// receives Core types only, so G22 holds.
        ///
        /// Best-effort must not mean invisible, which is why failures are COUNTED rather than
        /// swallowed - a path that fails without leaving a trace is the defect this project keeps
        /// finding, and "the guardian explains what happened" is a product claim resting on this
        /// callback having run.</summary>
        public Action<LedgerEntry> Observer { get; set; }

        /// <summary>Failures since the last read. Never recorded from inside the notification: doing
        /// that would append from inside the append, putting recursion in the lockout's critical
        /// path. The Guardian drains this on its next tick and on Stop().</summary>
        public int ObserverFailures { get; private set; }

        public int TakeObserverFailures()
        {
            var n = ObserverFailures;
            ObserverFailures = 0;
            return n;
        }

        /// <summary>Per-thread, so an observer that appends cannot recurse. That is a bug in the
        /// observer, but the ledger must not break its own chain because of someone else's bug.</summary>
        [ThreadStatic] private static bool _notifying;

        private void Notify(LedgerEntry entry)
        {
            var observer = Observer;
            if (observer == null || _notifying) return;
            _notifying = true;
            try { observer(entry); }
            catch { ObserverFailures++; }   // the append already succeeded; this cannot undo it
            finally { _notifying = false; }
        }

        private void LoadHead()
        {
            if (!_store.Exists(_path)) { _seq = 0; _head = Hashing.Genesis; return; }
            foreach (var line in _store.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!JsonParser.TryParse(line, out var v, out _) || !(v is JsonObject o)) continue;
                var h = o.GetString("hash");
                var s = o.GetInt("seq");
                if (h != null && s.HasValue) { _head = h; _seq = s.Value; }
            }
        }

        /// <summary>Serialises everything but the hash, in canonical form (SPEC 11.2).</summary>
        internal static JsonObject Unhashed(long seq, DateTime tsUtc, string ev, JsonObject payload, string prev)
        {
            return JsonValue.Obj()
                .Set("seq", seq)
                .Set("tsUtc", Iso.Utc(tsUtc))
                .Set("event", ev)
                .Set("schemaVersion", SchemaVersion)
                .Set("payload", payload ?? JsonValue.Obj())
                .Set("prev", prev);
        }

        /// <summary>Appends one event. Throws whatever the store throws: an unwritable ledger is an
        /// unknown, and the caller turns it into FAIL_CLOSED (SPEC 11.5).</summary>
        public LedgerEntry Append(string ev, DateTime tsUtc, JsonObject payload)
        {
            var seq = _seq + 1;
            var unhashed = Unhashed(seq, tsUtc, ev, payload, _head);
            var hash = Hashing.Sha256Hex(unhashed.ToCanonical());
            var full = unhashed.Set("hash", hash);
            _store.AppendLine(_path, full.ToCanonical());
            _seq = seq;
            _head = hash;
            var entry = new LedgerEntry(seq, tsUtc, ev, SchemaVersion, payload, unhashed.GetString("prev"), hash);
            Notify(entry);
            return entry;
        }

        /// <summary>SPEC 11.3: Ok, or the seq of the first broken link. Walks the FILE.</summary>
        public LedgerVerifyResult Verify()
        {
            if (!_store.Exists(_path)) return LedgerVerifyResult.Good();
            string prev = Hashing.Genesis;
            long expectedSeq = 1;
            foreach (var line in _store.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!JsonParser.TryParse(line, out var v, out var err) || !(v is JsonObject o))
                    return LedgerVerifyResult.Broken(expectedSeq, "unparseable line: " + err);

                var broken = CheckLink(o, expectedSeq, prev, out prev);
                if (broken != null) return broken;
                expectedSeq++;
            }
            return LedgerVerifyResult.Good();
        }

        /// <summary>THE SAME CHAIN CHECK, over a collection ALREADY IN MEMORY (2026-09-01).
        ///
        /// WHY IT EXISTS. The certificate emitter used to be told the chain was fine by whoever called
        /// it, and the addon established that with `ledger.Verify()` - which re-reads the FILE - and
        /// then handed over a SECOND read from `ledger.ReadAll()`. Two reads, with a live guardian
        /// appending between them: the sentence "ledgerVerified: true" was true of a set of bytes that
        /// was not the set the document described. When it came out right, it came out right by
        /// coincidence.
        ///
        /// The rule now is CERTIFY THE BYTES YOU READ: one read, verified, and everything printed
        /// derived from that read.
        ///
        /// AND IT IS NOT A SECOND IMPLEMENTATION, deliberately. CheckLink below is the only place the
        /// chain rule exists, reached from two entry points. Two independent implementations of the
        /// same wrong rule always agree, and their agreement reads as corroboration.
        ///
        /// One honest difference between the doors, named rather than hidden: this one cannot see an
        /// UNPARSEABLE line, because a caller that parsed already dropped it (ReadAll skips silently).
        /// What catches that here is the sequence: a dropped line makes seq jump, and a jump is a
        /// break. A file whose LAST line is garbage is the exception - the file door reports it and
        /// this one sees a shorter chain that is internally sound. It certifies what it read, which is
        /// the whole point, and it is why the two doors are not interchangeable for every purpose.</summary>
        public static LedgerVerifyResult VerifyEntries(IReadOnlyList<JsonObject> entries)
        {
            if (entries == null || entries.Count == 0) return LedgerVerifyResult.Good();
            string prev = Hashing.Genesis;
            long expectedSeq = 1;
            foreach (var o in entries)
            {
                if (o == null) return LedgerVerifyResult.Broken(expectedSeq, "null entry");
                var broken = CheckLink(o, expectedSeq, prev, out prev);
                if (broken != null) return broken;
                expectedSeq++;
            }
            return LedgerVerifyResult.Good();
        }

        /// <summary>One link of the chain: the required fields, the sequence, the back-pointer and the
        /// recomputed hash. Returns null when the link holds, and hands back this entry's hash so the
        /// caller can carry it forward.</summary>
        private static LedgerVerifyResult CheckLink(JsonObject o, long expectedSeq, string expectedPrev,
                                                    out string hashForNext)
        {
            hashForNext = expectedPrev;

            var seq = o.GetInt("seq");
            var hash = o.GetString("hash");
            var prevField = o.GetString("prev");
            var ev = o.GetString("event");
            var tsText = o.GetString("tsUtc");
            var schema = o.GetInt("schemaVersion");
            if (!seq.HasValue || hash == null || prevField == null || ev == null || tsText == null || !schema.HasValue)
                return LedgerVerifyResult.Broken(expectedSeq, "missing required field");
            if (seq.Value != expectedSeq)
                return LedgerVerifyResult.Broken(expectedSeq, "sequence jumped to " + seq.Value.ToString(CultureInfo.InvariantCulture));
            if (prevField != expectedPrev)
                return LedgerVerifyResult.Broken(seq.Value, "prev does not match the previous hash");

            var copy = JsonValue.Obj();
            foreach (var k in o.Keys) if (k != "hash") copy.Set(k, o[k]);
            if (Hashing.Sha256Hex(copy.ToCanonical()) != hash)
                return LedgerVerifyResult.Broken(seq.Value, "hash does not match the entry contents");

            hashForNext = hash;
            return null;
        }

        public IEnumerable<JsonObject> ReadAll()
        {
            if (!_store.Exists(_path)) yield break;
            foreach (var line in _store.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (JsonParser.TryParse(line, out var v, out _) && v is JsonObject o) yield return o;
            }
        }
    }
}
