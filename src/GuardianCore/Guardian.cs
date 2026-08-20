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
        }

        public GuardianStatus Status =>
            new GuardianStatus(_state?.Kind ?? StateKind.Disarmed, _state?.DayKey, _state?.Seal?.SealHash, _state?.Reason);

        public LedgerVerifyResult VerifyLedger() => _ledger?.Verify() ?? LedgerVerifyResult.Good();

        /// <summary>True only inside the run that armed: monotonic counters restart with the process
        /// (SPEC 6.4, 17.2).</summary>
        public bool HasMonotonicContinuity => _state?.Seal != null && _state.Seal.RunId == _runId;

        // ---------------- startup ----------------

        /// <summary>SPEC 6.2: read state, verify the ledger, verify the seal - in that order, because
        /// each step's trust depends on the previous one.</summary>
        public void Start()
        {
            _ledger = new Ledger(_store, _ledgerPath);

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
            Persist();
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

            _broker.CancelAllOrders(order.Account);
            Log(Ev.OrderRejectedLocked, JsonValue.Obj()
                .Set("account", order.Account)
                .Set("orderId", order.OrderId ?? "")
                .Set("instrument", order.Instrument ?? "")
                .Set("action", order.Action ?? ""));
        }

        public void Tick()
        {
            EnsureStarted();

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

            // SPEC 10: an unknown clears by re-computation, never by assumption.
            if (_state.Kind == StateKind.FailClosed && !_clockIncoherent)
            {
                Log(Ev.FailClosedCleared, JsonValue.Obj().Set("previousReason", _state.Reason ?? ""));
                _state.Kind = StateKind.Armed;
                _state.Reason = null;
                Checkpoint(snapshot, "transition");
            }

            // SPEC 8: >= , not > . Landing exactly on the limit is a breach.
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
            Log(Ev.GuardianStopped, JsonValue.Obj().Set("state", _state.Kind.ToString().ToUpperInvariant()));
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
            RunLockoutSteps();
            Persist();
        }

        private void RunLockoutSteps()
        {
            var accounts = _config?.Accounts ?? (IReadOnlyList<string>)new List<string>();
            if (accounts.Count == 0) { _state.LockoutVerified = false; return; }

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
            Log(Ev.PnlCheckpoint, JsonValue.Obj()
                .SetMoney("dayLoss", snapshot.TotalDayLoss)
                .Set("trigger", trigger)
                .Set("perAccount", PerAccount(snapshot)));
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
