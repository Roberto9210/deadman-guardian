using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GuardianCore
{
    /// <summary>Core constants of SPEC 5.6 and 6.4. Not configuration: these are engineering limits,
    /// not risk preferences the trader gets to tune.</summary>
    public static class Constants
    {
        public const int PnlEvaluationIntervalMs = 1_000;
        public const int PnlCheckpointIntervalMs = 300_000;
        public const int ClockDivergenceToleranceMs = 120_000;
        public const int MaxFlattenAttempts = 3;
    }

    public sealed class GuardianStatus
    {
        public StateKind Kind { get; }
        public string DayKey { get; }
        public string SealHash { get; }
        public string Reason { get; }
        public bool EntriesAllowed => Kind == StateKind.Disarmed || Kind == StateKind.Armed;
        public bool Sealed => SealHash != null;

        public GuardianStatus(StateKind kind, string dayKey, string sealHash, string reason)
        { Kind = kind; DayKey = dayKey; SealHash = sealHash; Reason = reason; }
    }

    public sealed class OperationResult
    {
        public bool Ok { get; }
        public IReadOnlyList<string> Reasons { get; }
        private OperationResult(bool ok, IReadOnlyList<string> reasons) { Ok = ok; Reasons = reasons ?? new List<string>(); }
        public static OperationResult Success() => new OperationResult(true, null);
        public static OperationResult Failure(params string[] reasons) => new OperationResult(false, reasons.ToList());
        public static OperationResult Failure(IEnumerable<string> reasons) => new OperationResult(false, reasons.ToList());
        public override string ToString() => Ok ? "OK" : string.Join("; ", Reasons);
    }

    public sealed class GuardianOptions
    {
        public IClock Clock { get; set; }
        public IFileStore Store { get; set; }
        public IBrokerActions Broker { get; set; }
        public IAccountFeed Feed { get; set; }
        /// <summary>Host-level paths. They are NOT taken from the configuration, because a lockout must
        /// remain readable when the configuration is missing or invalid - otherwise deleting config.json
        /// would orphan the lockout. The configuration must declare the same paths. Amendment A4.</summary>
        public string StatePath { get; set; }
        public string LedgerPath { get; set; }
        /// <summary>Identifies this process run. Monotonic continuity exists only within one run
        /// (SPEC 6.4, 17.2).</summary>
        public string RunId { get; set; }

        /// <summary>Optional observer for ledger appends (SPEC section 14). Best-effort and never
        /// load-bearing: an exception from it cannot break an append or stop a lockout, and its
        /// failures are counted and published rather than swallowed.</summary>
        public Action<LedgerEntry> LedgerObserver { get; set; }
        public Func<string, TimeZoneInfo> ZoneLookup { get; set; }
    }

    /// <summary>
    /// The state machine of SPEC section 8, the lockout of section 9, and the fail-closed rules of
    /// section 10. Pure: everything it needs arrives through the four ports of section 14.
    /// </summary>
    public sealed class Guardian
    {
        private readonly IClock _clock;
        private readonly IFileStore _store;
        private readonly IBrokerActions _broker;
        private readonly IAccountFeed _feed;
        private readonly string _statePath;
        private readonly string _ledgerPath;
        private readonly string _runId;
        private readonly Func<string, TimeZoneInfo> _zoneLookup;

        private Ledger _ledger;
        private PersistedState _state;
        private GuardianConfig _config;
        private SessionCalendar _calendar;
        private readonly PnlBook _book = new PnlBook();
        private long _lastCheckpointMono;
        /// <summary>True when THIS observation of the clock was incoherent. A clock unknown cannot be
        /// cleared by the same tick that detected it: SPEC 10 clears an unknown through a
        /// re-computation, and for the clock the re-computation is the next coherent observation.
        /// Amendment A6.</summary>
        private bool _clockIncoherent;
        private bool _ledgerUsable = true;
        private readonly Action<LedgerEntry> _ledgerObserver;

        /// <summary>Restart baseline (Option A). Set at Start when a same-day seal was restored: the
        /// book is empty but the platform remembers the session, so before ANY snapshot is evaluated
        /// the day's figures must be re-adopted - or refused, loudly. While pending, entries stay
        /// blocked; nothing is evaluated against half a picture.</summary>
        private bool _baselinePending;
        private Dictionary<string, decimal> _checkpointGross;   // last same-day checkpointed realised, per account
        private bool _baselineRefusalLogged;

        public Guardian(GuardianOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _clock = options.Clock ?? throw new ArgumentNullException(nameof(options.Clock));
            _store = options.Store ?? throw new ArgumentNullException(nameof(options.Store));
            _broker = options.Broker ?? throw new ArgumentNullException(nameof(options.Broker));
            _feed = options.Feed ?? throw new ArgumentNullException(nameof(options.Feed));
            _statePath = options.StatePath ?? throw new ArgumentNullException(nameof(options.StatePath));
            _ledgerPath = options.LedgerPath ?? throw new ArgumentNullException(nameof(options.LedgerPath));
            _runId = options.RunId ?? Guid.NewGuid().ToString("N");
            _zoneLookup = options.ZoneLookup;
            _ledgerObserver = options.LedgerObserver;
        }

        public GuardianStatus Status =>
            new GuardianStatus(_state?.Kind ?? StateKind.Disarmed, _state?.DayKey, _state?.Seal?.SealHash, _state?.Reason);

        /// <summary>The accounts this session is actually guarding, taken from the SEALED config -
        /// so it is right after a restore, when nobody re-armed and there is no other place to learn it
        /// from. Null until a config is in force, which is a real answer and not an empty list: "we do
        /// not know yet" and "we guard nothing" are different, and an adapter must not subscribe on the
        /// strength of the second when it means the first.
        ///
        /// M15: the adapter used to default to a hardcoded "Sim101" and only overwrite it inside Arm().
        /// A restart with a restored ARMED seal never runs Arm, so it watched Sim101 whatever the seal
        /// said - invisible on a machine whose account IS Sim101, broken from the first restart for
        /// anybody else.</summary>
        public IReadOnlyList<string> GuardedAccounts => _config?.Accounts;

        /// <summary>LT-2. The three configured values a RESTORE has but the arm path is the only thing
        /// that ever assigned in the adapter. Same shape as GuardedAccounts and for the same reason:
        /// _config is reparsed from the sealed snapshot at Start, so after a restart these are known
        /// even though nobody re-armed - and the adapter had them at their type's default instead.
        ///
        /// Null means "no configuration is in force", which is a real answer and not the same as zero.
        /// The reset time is formatted here so there is one dialect of it rather than one per caller.</summary>
        public decimal? SealedPersonalDailyLossLimit => _config?.PersonalDailyLossLimit;

        public string SealedSessionResetLocalTime =>
            _config == null ? null : _config.SessionResetLocalTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

        public string SealedSessionResetTimeZone => _config?.SessionResetTimeZone;

        public LedgerVerifyResult VerifyLedger() => _ledger?.Verify() ?? LedgerVerifyResult.Good();

        /// <summary>True only inside the run that armed: monotonic counters restart with the process
        /// (SPEC 6.4, 17.2).</summary>
        public bool HasMonotonicContinuity => _state?.Seal != null && _state.Seal.RunId == _runId;

        // ---------------- startup ----------------

        /// <summary>SPEC 6.2: read state, verify the ledger, verify the seal - in that order, because
        /// each step's trust depends on the previous one.</summary>
        public void Start()
        {
            _ledger = new Ledger(_store, _ledgerPath) { Observer = _ledgerObserver };

            // 1. state
            if (!_store.Exists(_statePath))
            {
                _state = new PersistedState
                {
                    Kind = StateKind.Disarmed,
                    LastSeenUtc = _clock.UtcNow,
                    LastMonotonicMs = _clock.MonotonicMs,
                    RunId = _runId
                };
                Log(Ev.GuardianStarted, JsonValue.Obj().Set("state", "DISARMED").Set("fresh", true));
                Persist();
                return;
            }

            string raw;
            try { raw = _store.ReadAllText(_statePath); }
            catch (Exception ex)
            {
                StartCorrupt("state file could not be read: " + ex.Message);
                return;
            }

            if (!PersistedState.TryParse(raw, out var loaded, out var parseError))
            {
                StartCorrupt(parseError);
                return;
            }
            _state = loaded;

            // 2. ledger chain
            var verify = _ledger.Verify();
            if (!verify.Ok)
            {
                Log(Ev.LedgerVerifyFailed, JsonValue.Obj().Set("brokenSeq", verify.BrokenSeq ?? -1).Set("reason", verify.Reason ?? ""));
                EnterFailClosed("ledger chain is broken at seq " + (verify.BrokenSeq ?? -1));
                return;
            }

            Log(Ev.GuardianStarted, JsonValue.Obj().Set("state", _state.Kind.ToString().ToUpperInvariant()));
            Log(Ev.StateRestored, JsonValue.Obj()
                .Set("state", _state.Kind.ToString().ToUpperInvariant())
                .Set("dayKey", _state.DayKey ?? "")
                .Set("sealHash", _state.Seal?.SealHash ?? ""));

            // 3. seal
            if (_state.Seal != null)
            {
                if (!_state.Seal.SnapshotMatchesHash())
                {
                    Log(Ev.SealMismatch, JsonValue.Obj()
                        .Set("expectedHash", _state.Seal.SealHash)
                        .Set("actualHash", Hashing.Sha256Hex(_state.Seal.ConfigSnapshot ?? "")));
                    EnterLockout("the sealed configuration in the state file was edited by hand", null);
                    return;
                }
                Log(Ev.SealVerified, JsonValue.Obj().Set("sealHash", _state.Seal.SealHash));

                // The sealed snapshot - not any file on disk - is what remains in force (SPEC 7.4).
                var reparsed = GuardianConfig.Parse(_state.Seal.ConfigSnapshot, _zoneLookup);
                if (reparsed.Ok)
                {
                    _config = reparsed.Config;
                    SessionCalendar.TryCreate(_config, out _calendar, out _, _zoneLookup);
                }
                else
                {
                    EnterFailClosed("the sealed configuration no longer parses: " + reparsed);
                    return;
                }
            }

            // 4. clock, then expiry
            CheckClock(startup: true);
            if (_state.Kind == StateKind.FailClosed && _state.Reason != null && _state.Reason.StartsWith("clock", StringComparison.Ordinal))
            {
                Persist();
                return;
            }
            if (CheckExpiry()) return;

            // 5. a lockout that was interrupted resumes here (SPEC 9, G7).
            if (_state.Kind == StateKind.Locked && !_state.LockoutVerified) RunLockoutSteps();

            // 6. restart baseline (Option A, M2/M3). A same-day seal means the platform remembers a
            // session this process has no memory of. The book must be re-seeded from a corroborated
            // figure before any snapshot is trusted - and until that happens, entries stay blocked.
            //
            // NT8 exposes NOTHING that states the period of its GrossRealizedProfitLoss (verified by
            // reflection over Account and AccountItem: bare numbers, no "since when"). The only
            // corroboration available is this guardian's own last PNL_CHECKPOINT for the same dayKey,
            // which is why the checkpoint now records the per-account realised figure.
            if (_state.Kind != StateKind.Disarmed && _state.Kind != StateKind.Locked &&
                _state.Seal != null && _config != null && _calendar != null &&
                _calendar.DayKey(_clock.UtcNow) == _state.DayKey)
            {
                _baselinePending = true;
                _checkpointGross = LoadSameDayCheckpointGross();
            }

            Persist();
        }

        /// <summary>The per-account realised figure from the last PNL_CHECKPOINT of the CURRENT day,
        /// read from this guardian's own ledger. Null when no such checkpoint exists - including
        /// ledgers written before the field existed, which then refuse to corroborate rather than
        /// pretend (SPEC 10: no plausible substitute).</summary>
        private Dictionary<string, decimal> LoadSameDayCheckpointGross()
        {
            try
            {
                Dictionary<string, decimal> result = null;
                var inCurrentDay = false;
                foreach (var entry in _ledger.ReadAll())
                {
                    var ev = entry.GetString("event");
                    if (ev == Ev.DayOpened)
                    {
                        var payload = entry["payload"] as JsonObject;
                        inCurrentDay = payload != null && payload.GetString("dayKey") == _state.DayKey;
                        if (inCurrentDay) result = null;   // a fresh DAY_OPENED restarts the search
                    }
                    else if (ev == Ev.PnlCheckpoint && inCurrentDay)
                    {
                        var payload = entry["payload"] as JsonObject;
                        var gross = payload?["grossRealizedPerAccount"] as JsonObject;
                        if (gross == null) continue;       // older schema: no corroboration available
                        var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
                        foreach (var key in gross.Keys)
                        {
                            decimal v;
                            if (Money.TryParse(gross.GetString(key), out v)) map[key] = v;
                        }
                        result = map;
                    }
                }
                return result;
            }
            catch { return null; }
        }

        /// <summary>Attempts the adoption. On success clears _baselinePending; on refusal enters
        /// fail-closed with the reason and logs PNL_BASELINE_REFUSED once; while simply unreadable
        /// (account not yet connected) it stays pending quietly.</summary>
        private void TryAdoptBaseline()
        {
            foreach (var account in _config.Accounts)
            {
                var state = _feed.GetState(account);
                if (state == null || !state.Known || state.Connection != ConnectionState.Connected)
                    return;                                    // not readable yet; stay pending

                var platform = _feed.GetPlatformPnl(account) ?? PlatformPnl.Unknown();
                if (!platform.GrossRealized.HasValue)
                    return;                                    // platform not reporting yet

                var p = platform.GrossRealized.Value;
                decimal? c = null;
                decimal cv;
                if (_checkpointGross != null && _checkpointGross.TryGetValue(account, out cv)) c = cv;

                // CONDITION 3: the period. NT8 does not say what period its figure covers, so the only
                // establishment is agreement with our own same-day record. No record, or a figure that
                // moved beyond tolerance while we were dead, cannot be told apart from a platform
                // session reset - so nothing is adopted and the reason says why.
                decimal adopted;
                if (c == null && p == 0m)
                {
                    adopted = 0m;                              // nothing happened; trivially established
                }
                else if (c == null)
                {
                    RefuseBaseline(account, null, p,
                        "the platform reports realised P&L but no same-day checkpoint exists to corroborate its period");
                    return;
                }
                else if (Math.Abs(p - c.Value) > _config.PnlToleranceUsd)
                {
                    RefuseBaseline(account, c, p,
                        "platform and last same-day checkpoint differ beyond tolerance; fills while this " +
                        "guardian was not running and a platform session reset are indistinguishable");
                    return;
                }
                else
                {
                    // CONDITION 2: within tolerance they may still differ - adopt whichever leaves the
                    // trader CLOSER to the limit, and both figures go to the ledger with their source.
                    adopted = Math.Min(p, c.Value);
                }

                // Open positions (M3). Every position needs its entry price, or later realised P&L is
                // garbage; a position the platform cannot price refuses the whole adoption.
                IReadOnlyList<PositionSnapshot> positions;
                try { positions = _broker.GetPositions(account); }
                catch (Exception ex)
                {
                    RefuseBaseline(account, c, p, "open positions could not be read: " + ex.Message);
                    return;
                }
                var open = new List<PositionSnapshot>();
                foreach (var pos in positions)
                {
                    if (pos == null || pos.Quantity == 0) continue;
                    if (!pos.AveragePrice.HasValue || pos.AveragePrice.Value <= 0m)
                    {
                        RefuseBaseline(account, c, p,
                            "open position on '" + pos.Instrument + "' has no usable average price");
                        return;
                    }
                    open.Add(pos);
                }

                // Commit: figures first, then positions, then the trace.
                _book.AdoptBaseline(account, adopted);
                foreach (var pos in open)
                    _book.AdoptPosition(account, pos.Instrument, pos.Quantity, pos.AveragePrice.Value);

                Log(Ev.PnlBaselineAdopted, JsonValue.Obj()
                    .Set("account", account)
                    .Set("dayKey", _state.DayKey ?? "")
                    .Set("coreCheckpoint", c.HasValue ? Money.Format(c.Value) : "none")
                    .SetMoney("platform", p)
                    .SetMoney("adopted", adopted)
                    .Set("why", c.HasValue && adopted != p
                        ? "checkpoint is the more conservative of the two"
                        : c.HasValue && adopted != c.Value
                            ? "platform is the more conservative of the two"
                            : "sources agree")
                    .Set("source", "min(platform, same-day checkpoint), both recorded")
                    .Set("positionsAdopted", open.Count));
            }
            _baselinePending = false;
        }

        private void RefuseBaseline(string account, decimal? core, decimal platform, string why)
        {
            if (!_baselineRefusalLogged)
            {
                _baselineRefusalLogged = true;
                Log(Ev.PnlBaselineRefused, JsonValue.Obj()
                    .Set("account", account)
                    .Set("dayKey", _state.DayKey ?? "")
                    .Set("coreCheckpoint", core.HasValue ? Money.Format(core.Value) : "none")
                    .SetMoney("platform", platform)
                    .Set("why", why));
            }
            EnterFailClosed("restart baseline refused on " + account + ": " + why);
        }

        private void StartCorrupt(string reason)
        {
            _state = new PersistedState
            {
                Kind = StateKind.FailClosed,
                LastSeenUtc = _clock.UtcNow,
                LastMonotonicMs = _clock.MonotonicMs,
                RunId = _runId,
                Reason = reason
            };
            Log(Ev.StateCorrupt, JsonValue.Obj().Set("error", reason));
            Log(Ev.FailClosedEntered, JsonValue.Obj().Set("reason", reason));
            Persist();
        }

        // ---------------- arming ----------------

        /// <summary>SPEC 7.1. Arming is a deliberate act, once per trading day.</summary>
        public OperationResult Arm(string configText)
        {
            EnsureStarted();

            if (_state.Kind == StateKind.Locked)
                return Reject("locked until the seal expires; a lockout has no manual exit (SPEC 8)");
            if (_state.Kind == StateKind.FailClosed)
                return Reject("the guardian is fail-closed: " + (_state.Reason ?? "unknown state"));
            if (_state.Seal != null && !IsSealExpired())
                return TryChangeConfig(configText);

            var parsed = GuardianConfig.Parse(configText, _zoneLookup);
            if (!parsed.Ok)
            {
                Log(Ev.ConfigRejected, JsonValue.Obj().Set("reasons", ToArray(parsed.Reasons)));
                return OperationResult.Failure(parsed.Reasons);   // SPEC 4: not arming is not a lockout
            }

            var reasons = ValidateAgainstEnvironment(parsed.Config);
            if (reasons.Count > 0)
            {
                Log(Ev.ConfigRejected, JsonValue.Obj().Set("reasons", ToArray(reasons)));
                return OperationResult.Failure(reasons);
            }

            if (!SessionCalendar.TryCreate(parsed.Config, out var calendar, out var calError, _zoneLookup))
            {
                Log(Ev.ConfigRejected, JsonValue.Obj().Set("reasons", ToArray(new[] { calError })));
                return OperationResult.Failure(calError);
            }

            _config = parsed.Config;
            _calendar = calendar;

            var now = _clock.UtcNow;
            var expires = _calendar.SessionEndUtc(now);
            var dayKey = _calendar.DayKey(now);
            var duration = (long)(expires - now).TotalMilliseconds;

            Log(Ev.ConfigLoaded, JsonValue.Obj().Set("configHash", _config.Hash()));

            _book.ResetDay();
            // A fresh arm has a fresh book: nothing to adopt, nothing pending. Without this, a
            // baseline refused before an expiry HAUNTED the next arm - the expiry disarms without
            // rolling the day, and Arm sets the new dayKey directly, so RollDayIfNeeded never fired
            // and the stale pending re-evaluated a restart that never happened (found by the pre-F5
            // contingency question, 2026-08-25).
            _baselinePending = false;
            _checkpointGross = null;
            _baselineRefusalLogged = false;
            _state.Kind = StateKind.Armed;
            _state.DayKey = dayKey;
            _state.Reason = null;
            _state.LockoutVerified = false;
            _state.FlattenAttempts = 0;
            _state.Seal = new Seal(_config.Hash(), _config.Canonical, now, expires, dayKey,
                                   _ledger.Head, _clock.MonotonicMs, duration, _runId);
            Persist();

            Log(Ev.Armed, JsonValue.Obj()
                .Set("dayKey", dayKey)
                .SetMoney("personalLimit", _config.PersonalDailyLossLimit)
                .SetMoney("firmLimit", _config.FirmDailyLossLimit)
                .Set("accounts", ToArray(_config.Accounts)));
            Log(Ev.SealCreated, JsonValue.Obj()
                .Set("sealHash", _state.Seal.SealHash)
                .Set("expiresAtUtc", Iso.Utc(expires))
                .Set("ledgerHeadHash", _state.Seal.LedgerHeadHash)
                .Set("sealDurationMs", duration));
            Log(Ev.DayOpened, JsonValue.Obj().Set("dayKey", dayKey));
            _lastCheckpointMono = _clock.MonotonicMs;
            return OperationResult.Success();
        }

        /// <summary>SPEC 7.2: while sealed, EVERY configuration change is rejected - including one that
        /// looks stricter. A change that is safe today is a debate at 14:30 tomorrow.</summary>
        public OperationResult TryChangeConfig(string configText)
        {
            EnsureStarted();
            if (_state.Seal == null || IsSealExpired())
                return Reject("no seal in force; call Arm to start a session");

            var offeredHash = "unparseable";
            var changed = new List<string>();
            var parsed = GuardianConfig.Parse(configText, _zoneLookup);
            if (parsed.Ok)
            {
                offeredHash = parsed.Config.Hash();
                changed = ChangedKeys(_state.Seal.ConfigSnapshot, parsed.Config.Canonical);
            }

            var minutesLeft = (long)Math.Max(0, (_state.Seal.ExpiresAtUtc - _clock.UtcNow).TotalMinutes);
            Log(Ev.ConfigChangeRejected, JsonValue.Obj()
                .Set("offeredHash", offeredHash)
                .Set("changedKeys", ToArray(changed))
                .Set("minutesToExpiry", minutesLeft));

            return OperationResult.Failure(
                "the configuration is sealed until " + Iso.Utc(_state.Seal.ExpiresAtUtc) +
                " (" + minutesLeft.ToString(CultureInfo.InvariantCulture) + " minutes); every change is rejected while sealed, " +
                "including a stricter one (SPEC 7.2)");
        }

        /// <summary>SPEC 7.4: a config file that no longer matches the sealed snapshot is treated as an
        /// attempt to trade past the limit, because that is what it is.</summary>
        public void OnConfigFileObserved(string configTextOnDisk)
        {
            EnsureStarted();
            if (_state.Seal == null || IsSealExpired()) return;

            var parsed = GuardianConfig.Parse(configTextOnDisk, _zoneLookup);
            var onDiskHash = parsed.Ok ? parsed.Config.Hash() : Hashing.Sha256Hex(configTextOnDisk ?? "");
            if (onDiskHash == _state.Seal.SealHash) return;

            Log(Ev.ConfigTampered, JsonValue.Obj()
                .Set("sealedHash", _state.Seal.SealHash)
                .Set("onDiskHash", onDiskHash)
                .Set("changedKeys", ToArray(parsed.Ok ? ChangedKeys(_state.Seal.ConfigSnapshot, parsed.Config.Canonical) : new List<string>())));
            EnterLockout("the configuration file was edited while sealed", null);
        }

        // ---------------- the loop ----------------

        public void OnExecution(ExecutionRecord execution)
        {
            EnsureStarted();
            if (!_book.Apply(execution, out var problem) && problem != null)
                Log(Ev.PnlUncomputable, JsonValue.Obj()
                    .Set("account", execution?.Account ?? "")
                    .Set("instrument", execution?.Instrument ?? "")
                    .Set("problem", problem));
            Tick();   // SPEC 5.6: a breach is always decided on an evaluation
        }

        /// <summary>SPEC 9.5: a single flatten is not a lockout. The DOM, a chart and a running strategy
        /// can all still submit while LOCKED.</summary>
        public void OnOrderObserved(OrderSnapshot order)
        {
            EnsureStarted();
            if (order == null || _state.Kind != StateKind.Locked) return;

            // M1. The only place this library asks a broker to act on an account it was TOLD about
            // rather than one it CHOSE, and until 2026-08-22 it never checked. Cancelling on
            // order.Account meant that whatever the adapter forwarded got cancelled - and what a
            // cancel destroys is a protective stop, on an account that may hold real money. The
            // decision layer was trusting its caller, which is the inversion of how everything else
            // here is built.
            //
            // Refusing SILENTLY would hide it. If a foreign order ever reaches this method the wiring
            // changed underneath us, and that is precisely the thing worth seeing, so it costs one
            // ledger line and zero broker calls.
            //
            // A null config cannot be verified against, so it is treated as foreign too: unable to
            // confirm the account is ours is not permission to act on it.
            var guarded = _config?.Accounts;
            if (guarded == null || !guarded.Contains(order.Account, StringComparer.Ordinal))
            {
                Log(Ev.ForeignAccountOrderObserved, JsonValue.Obj()
                    .Set("account", order.Account ?? "")
                    .Set("instrument", order.Instrument ?? "")
                    .Set("orderId", order.OrderId ?? "")
                    .Set("guarded", guarded == null ? "<none>" : string.Join(",", guarded)));
                return;
            }

            // LT-1, 2026-08-26. This line used to be _broker.CancelAllOrders(order.Account), and the
            // live test proved what that costs: the guardian OBSERVED ITS OWN FLATTEN ORDER, saw
            // itself LOCKED, and cancelled it 1ms after the venue accepted it. 167 loops,
            // FLATTEN_VERIFIED zero, position never closed. The twelve ORDER_REJECTED_LOCKED it wrote
            // that night were Sell, SellShort and BuyToCover - the trader's own exits.
            //
            // Twenty lines above, the M1 fix already said "what a cancel destroys is a protective
            // stop, on an account that may hold real money". We fixed WHO it happens to and left WHAT
            // IT IS on the guarded account untouched.
            //
            // THE DOCTRINE: the guardian never acts on the account on a premise it could not verify.
            // Cancelling is ACTING, not refusing to act, so the fail-closed instinct does not reach
            // it. And the worst cases are not symmetric: cancelling wrongly means the trader cannot
            // exit a sinking position - unbounded, and caused by us - while NOT cancelling means one
            // order opens exposure and the next cycle's flatten closes it, bounded by one cycle.
            //
            // So nothing is cancelled here. An order that fills after the lockout is closed by the
            // flatten, which is where the protection actually lives.
            //
            // ORDER_REJECTED_LOCKED is not written either, and its absence is deliberate: nothing is
            // rejected any more, and a name that asserts a rejection that did not happen is exactly
            // the defect class this repository chases. The event returns - truthfully - when
            // classification lands and the orders that INCREASE exposure are cancelled again. Until
            // then the certificate's ordersRejectedWhileLocked is 0, which is true.
            return;
        }

        public void Tick()
        {
            EnsureStarted();

            // Published OUTSIDE the append path, on the tick after the failure, so that recording a
            // notification failure never appends from inside an append.
            var notifyFailures = _ledger.TakeObserverFailures();
            if (notifyFailures > 0)
                Log(Ev.NotifyFailed, JsonValue.Obj().Set("count", notifyFailures));

            CheckClock(startup: false);
            if (CheckExpiry()) return;
            if (_state.Kind == StateKind.Disarmed) { Persist(); return; }

            RollDayIfNeeded();

            if (_state.Kind == StateKind.Locked)
            {
                if (!_state.LockoutVerified) RunLockoutSteps();
                Persist();
                return;
            }

            if (_config == null) { Persist(); return; }

            // Restart baseline: nothing is evaluated against half a picture. While pending, entries
            // stay blocked; a refusal keeps its own reason, a mere can't-read-yet gets a generic one.
            if (_baselinePending)
            {
                TryAdoptBaseline();
                if (_baselinePending)
                {
                    if (_state.Kind != StateKind.FailClosed)
                        EnterFailClosed("restart baseline not yet corroborated: waiting to read the platform's session figures");
                    else
                        Persist();
                    return;
                }
            }

            var snapshot = _book.Snapshot(_config.Accounts, _feed, _config.PnlToleranceUsd);
            if (!snapshot.Ok)
            {
                var problem = snapshot.FirstProblem;
                switch (problem.Status)
                {
                    case PnlStatus.AccountUnknown:
                        Log(Ev.AccountUnknown, JsonValue.Obj().Set("account", problem.Account).Set("detail", problem.Detail ?? ""));
                        break;
                    case PnlStatus.NoPriceForOpenPosition:
                        Log(Ev.PnlUncomputable, JsonValue.Obj().Set("account", problem.Account).Set("detail", problem.Detail ?? ""));
                        break;
                    case PnlStatus.SourcesDisagree:
                        Log(Ev.PnlDisagreement, JsonValue.Obj()
                            .Set("account", problem.Account)
                            .SetMoney("coreValue", problem.GrossRealized)
                            .Set("platformValue", problem.PlatformGrossRealized.HasValue ? Money.Format(problem.PlatformGrossRealized.Value) : "null")
                            .Set("detail", problem.Detail ?? ""));
                        break;
                    default:
                        Log(Ev.PnlUncomputable, JsonValue.Obj().Set("account", problem.Account).Set("detail", problem.Detail ?? ""));
                        break;
                }
                EnterFailClosed(problem.Status + " on " + problem.Account + ": " + (problem.Detail ?? ""));
                return;
            }

            // CONDITION 1 of the restart baseline, checked BEFORE the clear branch so a standing
            // baseline-only breach cannot flap between cleared and re-entered. An adopted figure may
            // BLOCK entries but may never FLATTEN: a flatten needs at least one fill this guardian
            // observed itself. Blocking over-costs opportunity; flattening over-costs money.
            // M22. "!HasObservedFill" was a PROXY for condition 1, and a wider one than the condition:
            // TotalDayLoss is not made of adopted figures alone. Unrealized arrives live from the
            // platform every tick and was never adopted - adopting a position decides only whether it
            // is READ - so a breach carried entirely by a moving live loss was being refused a flatten
            // on the grounds that an adopted figure caused it, when that figure could be ZERO.
            //
            // The condition itself, now: block without flattening only when the breach DISAPPEARS once
            // the adopted part is removed. If the observed loss reaches the limit on its own, this
            // falls through to the ordinary lockout below and the position is closed.
            if (snapshot.TotalDayLoss >= _config.PersonalDailyLossLimit &&
                snapshot.TotalDayLossObserved < _config.PersonalDailyLossLimit &&
                !_book.HasObservedFill)
            {
                // The message reports the arithmetic instead of asserting a cause. "on adopted figures
                // alone" was written over a figure that can be 0.00 - text claiming more than its own
                // code checked, which is this project's house defect.
                var reason = Messages.ReasonLimitNotFlattened +
                             ": $" + Money.Format(snapshot.TotalDayLoss) + " against your $" +
                             Money.Format(_config.PersonalDailyLossLimit) + " limit, of which $" +
                             Money.Format(snapshot.TotalDayLossObserved) + " happened while I was watching" +
                             " (baseline adopted at restart: $" + Money.Format(snapshot.TotalAdoptedBaseline) + ").";
                var alreadyThis = _state.Kind == StateKind.FailClosed &&
                                  Messages.IsLimitNotFlattened(_state.Reason);
                if (!alreadyThis)
                    Log(Ev.LimitBreachedBaselineOnly, JsonValue.Obj()
                        .SetMoney("dayLoss", snapshot.TotalDayLoss)
                        .SetMoney("dayLossObserved", snapshot.TotalDayLossObserved)
                        .SetMoney("adoptedBaseline", snapshot.TotalAdoptedBaseline)
                        .SetMoney("limit", _config.PersonalDailyLossLimit)
                        .Set("perAccount", PerAccount(snapshot))
                        .Set("flattened", false)
                        .Set("why", "the limit is reached only once figures adopted at restart are counted; " +
                                    "the loss observed by this session is under the limit on its own, and " +
                                    "adopted figures may block but never flatten"));
                EnterFailClosed(reason);
                return;
            }

            // SPEC 10: an unknown clears by re-computation, never by assumption.
            if (_state.Kind == StateKind.FailClosed && !_clockIncoherent)
            {
                Log(Ev.FailClosedCleared, JsonValue.Obj().Set("previousReason", _state.Reason ?? ""));
                _state.Kind = StateKind.Armed;
                _state.Reason = null;
                Checkpoint(snapshot, "transition");
            }

            // SPEC 8: >= , not > . Landing exactly on the limit is a breach.
            //
            // Reaching here no longer implies HasObservedFill, and saying it did would be a comment
            // that lies. Since M22 what it implies is the thing condition 1 actually protects: either
            // this session witnessed a fill, or the loss it DID observe reaches the limit without any
            // help from an adopted figure. Both are breaches this guardian is entitled to act on.
            if (snapshot.TotalDayLoss >= _config.PersonalDailyLossLimit)
            {
                Log(Ev.LimitBreached, JsonValue.Obj()
                    .SetMoney("dayLoss", snapshot.TotalDayLoss)
                    .SetMoney("limit", _config.PersonalDailyLossLimit)
                    .Set("perAccount", PerAccount(snapshot)));
                EnterLockout("daily loss limit reached", snapshot);
                return;
            }

            if (_clock.MonotonicMs - _lastCheckpointMono >= Constants.PnlCheckpointIntervalMs)
                Checkpoint(snapshot, "interval");

            Persist();
        }

        public void Stop()
        {
            if (_state == null) return;

            // Drain into GUARDIAN_STOPPED as well as on the tick. The window that matters is exactly
            // the one a tick can miss: a trader who closes the platform seconds after a lockout, which
            // is when a notification failure would be both most likely and most worth knowing about.
            var pending = _ledger != null ? _ledger.TakeObserverFailures() : 0;

            Log(Ev.GuardianStopped, JsonValue.Obj()
                .Set("state", _state.Kind.ToString().ToUpperInvariant())
                .Set("notifyFailures", pending));
            Persist();
        }

        // ---------------- clock ----------------

        /// <summary>SPEC 6.4. The wall clock is the trader's to set; the monotonic counter is not.</summary>
        private void CheckClock(bool startup)
        {
            var now = _clock.UtcNow;
            var mono = _clock.MonotonicMs;
            var continuity = _state.RunId == _runId && !startup;

            var wallDelta = (long)(now - _state.LastSeenUtc).TotalMilliseconds;
            var monoDelta = mono - _state.LastMonotonicMs;
            _clockIncoherent = false;

            if (wallDelta < -Constants.ClockDivergenceToleranceMs)
            {
                // Always recorded, even when it changes nothing: this is the trace of SPEC 17.2.
                var payload = JsonValue.Obj()
                    .Set("lastSeenUtc", Iso.Utc(_state.LastSeenUtc))
                    .Set("nowUtc", Iso.Utc(now))
                    .Set("deltaSeconds", wallDelta / 1000)
                    .Set("sealMaintained", true);
                if (continuity) Log(Ev.ClockAnomaly, payload.Set("direction", "backward").Set("deltaMonoMs", monoDelta));
                else Log(Ev.ClockSuspect, payload);
                _clockIncoherent = true;
                EnterFailClosed("clock moved backwards by " + (-wallDelta / 1000).ToString(CultureInfo.InvariantCulture) + "s");
            }
            else if (continuity && wallDelta - monoDelta > Constants.ClockDivergenceToleranceMs)
            {
                // The forward jump: the wall clock moved without time passing.
                Log(Ev.ClockAnomaly, JsonValue.Obj()
                    .Set("direction", "forward")
                    .Set("lastSeenUtc", Iso.Utc(_state.LastSeenUtc))
                    .Set("nowUtc", Iso.Utc(now))
                    .Set("deltaWallMs", wallDelta)
                    .Set("deltaMonoMs", monoDelta)
                    .Set("sealMaintained", true));
                _clockIncoherent = true;
                EnterFailClosed("wall clock advanced " + ((wallDelta - monoDelta) / 1000).ToString(CultureInfo.InvariantCulture) +
                                "s more than real time");
            }

            _state.LastSeenUtc = now;
            _state.LastMonotonicMs = mono;
        }

        /// <summary>SPEC 7.5: with monotonic continuity the seal is measured on the monotonic clock, so
        /// no wall-clock value can release it early. Without continuity (after a restart) the wall clock
        /// is all the evidence there is - the documented gap of SPEC 17.2.</summary>
        private bool IsSealExpired()
        {
            if (_state?.Seal == null) return true;
            if (HasMonotonicContinuity)
                return _clock.MonotonicMs - _state.Seal.MonoAtArmMs >= _state.Seal.SealDurationMs;
            return _clock.UtcNow >= _state.Seal.ExpiresAtUtc;
        }

        private bool CheckExpiry()
        {
            if (_state.Seal == null || !IsSealExpired()) return false;

            var basis = HasMonotonicContinuity ? "monotonic" : "wallclock";
            var elapsed = HasMonotonicContinuity
                ? _clock.MonotonicMs - _state.Seal.MonoAtArmMs
                : (long)(_clock.UtcNow - _state.Seal.ArmedAtUtc).TotalMilliseconds;

            Log(Ev.SealExpired, JsonValue.Obj()
                .Set("dayKey", _state.Seal.DayKey)
                .Set("basis", basis)
                .Set("sealDurationMs", _state.Seal.SealDurationMs)
                .Set("elapsedMs", elapsed));

            if (_state.Kind == StateKind.Locked)
                Log(Ev.LockoutCleared, JsonValue.Obj().Set("dayKey", _state.Seal.DayKey));
            Log(Ev.DayClosed, JsonValue.Obj().Set("dayKey", _state.Seal.DayKey));
            Log(Ev.Disarmed, JsonValue.Obj().Set("dayKey", _state.Seal.DayKey));

            _state.Kind = StateKind.Disarmed;
            _state.Seal = null;
            _state.Reason = null;
            _state.LockoutVerified = false;
            _state.FlattenAttempts = 0;
            _config = null;
            _book.ResetDay();
            // The day this pending belonged to just ended; a Disarmed guardian has nothing to adopt.
            _baselinePending = false;
            _checkpointGross = null;
            _baselineRefusalLogged = false;
            Persist();
            return true;
        }

        private void RollDayIfNeeded()
        {
            if (_calendar == null) return;
            var key = _calendar.DayKey(_clock.UtcNow);
            if (key == _state.DayKey) return;
            Log(Ev.DayClosed, JsonValue.Obj().Set("dayKey", _state.DayKey ?? ""));
            Log(Ev.DayOpened, JsonValue.Obj().Set("dayKey", key));
            _state.DayKey = key;
            _book.ResetDay();
            // A fresh day has nothing to re-adopt: the platform's figure belongs to the old one.
            _baselinePending = false;
            _checkpointGross = null;
            _baselineRefusalLogged = false;
        }

        // ---------------- lockout ----------------

        /// <summary>SPEC 9. Ordered, idempotent, resumable. State is persisted BEFORE any broker call:
        /// a process killed here comes back LOCKED, not "armed and fine".</summary>
        private void EnterLockout(string reason, DayPnlSnapshot snapshot)
        {
            _state.Kind = StateKind.Locked;
            _state.Reason = reason;
            _state.LockoutVerified = false;
            _state.FlattenAttempts = 0;
            Persist();                       // step 1, before anything reaches the broker
            SweepRestingOrders();            // step 2, ONCE - see the method
            RunLockoutSteps();
            Persist();
        }

        /// <summary>Clears the orders resting AT the moment the lockout begins. It lives here, and not
        /// in RunLockoutSteps, because RunLockoutSteps RE-ENTERS on every tick until the flatten
        /// verifies - and a blind account-wide cancel running every second would kill a flatten order
        /// still in flight, which is LT-1's slow half. Placing it at the call site makes "once" a
        /// property of WHERE THE CODE LIVES rather than a rule somebody has to remember to check: a
        /// flag gets forgotten, a call site does not.
        ///
        /// And it is an OPTIMISATION, not a protection - the distinction matters and was verified
        /// rather than assumed. Every path was constructed: a resting order that fills after the
        /// lockout opens exposure, and the next cycle's flatten closes it. Even the worst one - an
        /// orphaned stop firing after the position closed, opening the other way - ends at the
        /// flatten, one cycle later. The sweep prevents a FILL; the flatten UNDOES one. That is a
        /// difference of magnitude, not of kind, so best-effort once is correct: it is recorded if it
        /// fails and never retried.</summary>
        private void SweepRestingOrders()
        {
            var accounts = _config?.Accounts ?? (IReadOnlyList<string>)new List<string>();
            foreach (var account in accounts)
            {
                try
                {
                    var working = _broker.GetWorkingOrders(account);
                    _broker.CancelAllOrders(account);
                    Log(Ev.OrdersCancelled, JsonValue.Obj()
                        .Set("account", account)
                        .Set("count", working?.Count ?? 0)
                        .Set("orderIds", ToArray((working ?? new List<OrderSnapshot>()).Select(o => o.OrderId ?? ""))));
                }
                catch (Exception ex)
                {
                    Log(Ev.LockoutIncomplete, JsonValue.Obj().Set("account", account).Set("step", "cancel").Set("error", ex.Message));
                }
            }
        }

        private void RunLockoutSteps()
        {
            var accounts = _config?.Accounts ?? (IReadOnlyList<string>)new List<string>();
            if (accounts.Count == 0) { _state.LockoutVerified = false; return; }

            foreach (var account in accounts)
            {
                try
                {
                    var positions = _broker.GetPositions(account);
                    _broker.Flatten(account);
                    Log(Ev.FlattenRequested, JsonValue.Obj()
                        .Set("account", account)
                        .Set("instruments", ToArray((positions ?? new List<PositionSnapshot>()).Select(p => p.Instrument ?? ""))));
                }
                catch (Exception ex)
                {
                    // A throw here is exactly the "killed mid-flatten" case: state already says LOCKED,
                    // the attempt is recorded, and the next tick or the next process resumes.
                    Log(Ev.LockoutIncomplete, JsonValue.Obj().Set("account", account).Set("step", "flatten").Set("error", ex.Message));
                }
            }

            _state.FlattenAttempts++;

            // Step 4: verify, never assume.
            var remaining = new List<string>();
            foreach (var account in accounts)
            {
                var positions = _broker.GetPositions(account) ?? new List<PositionSnapshot>();
                var orders = _broker.GetWorkingOrders(account) ?? new List<OrderSnapshot>();
                if (positions.Any(p => p.Quantity != 0) || orders.Count > 0) remaining.Add(account);
            }

            if (remaining.Count == 0)
            {
                _state.LockoutVerified = true;
                Log(Ev.FlattenVerified, JsonValue.Obj()
                    .Set("accounts", ToArray(accounts))
                    .Set("attempts", _state.FlattenAttempts));
            }
            else
            {
                _state.LockoutVerified = false;
                Log(Ev.LockoutIncomplete, JsonValue.Obj()
                    .Set("accounts", ToArray(remaining))
                    .Set("attempts", _state.FlattenAttempts)
                    .Set("exhausted", _state.FlattenAttempts >= Constants.MaxFlattenAttempts));
            }
        }

        // ---------------- helpers ----------------

        private void EnterFailClosed(string reason)
        {
            if (_state.Kind == StateKind.Locked)
            {
                // A lockout outranks an unknown: never downgrade LOCKED (SPEC 8).
                _state.Reason = _state.Reason ?? reason;
                Persist();
                return;
            }
            if (_state.Kind != StateKind.FailClosed)
                Log(Ev.FailClosedEntered, JsonValue.Obj().Set("reason", reason));
            _state.Kind = StateKind.FailClosed;
            _state.Reason = reason;
            Persist();
        }

        private List<string> ValidateAgainstEnvironment(GuardianConfig config)
        {
            var reasons = new List<string>();
            if (config.LedgerPath != _ledgerPath)
                reasons.Add("'ledgerPath' must be " + _ledgerPath + " (the path this guardian was started with)");
            if (config.StatePath != _statePath)
                reasons.Add("'statePath' must be " + _statePath + " (the path this guardian was started with)");

            foreach (var account in config.Accounts)
            {
                var state = _feed.GetState(account);
                if (state == null || !state.Known)
                {
                    reasons.Add("account '" + account + "' is not known to the platform");
                    continue;
                }
                if (!string.Equals(state.Denomination, config.Currency, StringComparison.Ordinal))
                    reasons.Add("account '" + account + "' is denominated in " + (state.Denomination ?? "unknown") +
                                ", not " + config.Currency + "; cross-currency arithmetic is a guess");
            }
            return reasons;
        }

        private static List<string> ChangedKeys(string sealedCanonical, string offeredCanonical)
        {
            var changed = new List<string>();
            if (!JsonParser.TryParse(sealedCanonical ?? "", out var a, out _) || !(a is JsonObject oldObj)) return changed;
            if (!JsonParser.TryParse(offeredCanonical ?? "", out var b, out _) || !(b is JsonObject newObj)) return changed;
            foreach (var key in oldObj.Keys.Concat(newObj.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
            {
                var oldValue = oldObj[key]?.ToCanonical();
                var newValue = newObj[key]?.ToCanonical();
                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal)) changed.Add(key);
            }
            return changed;
        }

        private void Checkpoint(DayPnlSnapshot snapshot, string trigger)
        {
            var grossPerAccount = JsonValue.Obj();
            foreach (var a in snapshot.Accounts) grossPerAccount.SetMoney(a.Account, a.GrossRealized);
            Log(Ev.PnlCheckpoint, JsonValue.Obj()
                .SetMoney("dayLoss", snapshot.TotalDayLoss)
                .Set("trigger", trigger)
                .Set("perAccount", PerAccount(snapshot))
                // The restart baseline corroborates against THIS field (per-account realised, gross).
                // perAccount above is DayPnl - it includes unrealised - and cannot serve: the platform
                // figure being corroborated is realised-only. Removing this field silently disables
                // every future restart adoption, so it is not optional.
                .Set("grossRealizedPerAccount", grossPerAccount));
            _lastCheckpointMono = _clock.MonotonicMs;
        }

        private static JsonObject PerAccount(DayPnlSnapshot snapshot)
        {
            var o = JsonValue.Obj();
            foreach (var a in snapshot.Accounts) o.SetMoney(a.Account, a.DayPnl);
            return o;
        }

        private static JsonArray ToArray(IEnumerable<string> items)
        {
            var arr = JsonValue.Arr();
            foreach (var i in items) arr.Add(JsonValue.Str(i));
            return arr;
        }

        private void Log(string ev, JsonObject payload)
        {
            if (!_ledgerUsable) return;
            try { _ledger.Append(ev, _clock.UtcNow, payload); }
            catch (Exception ex)
            {
                // SPEC 11.5: a guardian that cannot record cannot protect.
                _ledgerUsable = false;
                if (_state != null && _state.Kind != StateKind.Locked)
                {
                    _state.Kind = StateKind.FailClosed;
                    _state.Reason = "ledger is not writable: " + ex.Message;
                    try { Persist(); } catch { /* nothing left to do but stay blocked */ }
                }
            }
        }

        private void Persist()
        {
            try { _store.WriteAtomic(_statePath, _state.ToJson().ToCanonical()); }
            catch (Exception ex)
            {
                _state.Kind = _state.Kind == StateKind.Locked ? StateKind.Locked : StateKind.FailClosed;
                _state.Reason = "state is not writable: " + ex.Message;
            }
        }

        private OperationResult Reject(string reason) => OperationResult.Failure(reason);

        private void EnsureStarted()
        {
            if (_state == null) throw new InvalidOperationException("Start() must be called before using the guardian");
        }
    }
}
