// deadman-guardian — the AddOn itself.
//
// Account-level, not a chart indicator (SPEC §3.3): it loads once with NinjaTrader and lives for the
// whole session, so it cannot be removed by closing a chart. Verified in-process: the lifecycle is
// SetDefaults -> Configure -> Active -> Terminated (nt/STEP3_FINDINGS.md §3).
//
// THE RULE (SPEC §3.2): no decision lives here. This file wires NT8 events into Core and executes what
// Core decides. Every threshold, comparison and state transition is in GuardianCore.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GuardianCore;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.DeadmanGuardian;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class DeadmanGuardianAddOn : AddOnBase
    {
        private static readonly string HomeDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         "NinjaTrader 8", "deadman-guardian");

        private static readonly string ConfigPath = Path.Combine(HomeDir, "config.json");
        private static readonly string StatePath = Path.Combine(HomeDir, "state.json");
        private static readonly string LedgerPath = Path.Combine(HomeDir, "ledger.jsonl");
        // Comfort, not commitment. NEVER config.json: that one is SEALED, and a window position that
        // could write CONFIG_TAMPERED would lock a trader out for tidying their desktop.
        private static readonly string UiPrefsPath = Path.Combine(HomeDir, "ui.json");

        /// <summary>Long enough to act, far too short to forget. No cap on how many times: a counter
        /// that traps someone after three tries arrives without warning, which is worse than having
        /// no way out at all.</summary>
        private const int SnoozeMs = 60000;
        private Timer _snoozeTimer;
        private bool _snoozed;
        private string _snoozedUnder;     // the rendered state when it went to sleep
        private bool _stopping;
        private static readonly string AdapterLogPath = Path.Combine(HomeDir, "adapter.log");

        private readonly object _gate = new object();
        private Guardian _guardian;
        private NtAccountFeed _feed;
        private Timer _timer;
        private GuardianStatusWindow _window;
        private Account _subscribed;
        /// <summary>Null until resolved from the SEALED config - never a hardcoded default.
        ///
        /// M15: this used to be "Sim101", overwritten only inside the arm path. A restart with a
        /// restored ARMED seal never arms, so the adapter watched Sim101 whatever the seal said.
        /// Resolution now happens through GuardedAccountRule at boot (after Core restores) and again
        /// on arm, and its outcome is logged every time so an auditor can see which account each
        /// session actually watched - or why it watched none.</summary>
        private string _guardedAccount;
        // LT-2. These five only ever existed if this process was present at a particular instant, and
        // three of them lied about it because their TYPE gave them no way to be absent. A real person
        // read "your limit is $0.00" with a $40 limit on 2026-08-26, after an F5 restored ARMED from
        // the seal without re-arming.
        //
        // The three that come from configuration are gone: they are read from Core, which reparses the
        // SEALED snapshot at Start and therefore knows them after a restart - the same fix M15 applied
        // to the guarded account, now applied to the family it should have swept.
        //
        // The two that come from OBSERVED EVENTS stay here and are nullable, because they genuinely
        // can be unknown: a restart during a lockout misses LIMIT_BREACHED and ORDERS_CANCELLED, which
        // were written before this process existed. Null is that fact; 0 is a different fact.
        private int? _lastCancelCount;                 // what ORDERS_CANCELLED reported, if we saw it
        private decimal? _breachDayLoss;               // the figure LIMIT_BREACHED carried, if we saw it
        private string _lastConfigText;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "deadman-guardian";
            }
            else if (State == State.Configure)
            {
                try { Boot(); }
                catch (Exception ex) { AdapterLog("Boot FAILED: " + ex); }
            }
            else if (State == State.Terminated)
            {
                try { Shutdown(); }
                catch (Exception ex) { AdapterLog("Shutdown FAILED: " + ex); }
            }
        }

        // ---------------- boot ----------------

        private void Boot()
        {
            Directory.CreateDirectory(HomeDir);
            AdapterLog("boot; home=" + HomeDir);

            _feed = new NtAccountFeed();
            _guardian = new Guardian(new GuardianOptions
            {
                Clock = new NtClock(),
                Store = new NtFileStore(),
                Broker = new NtBrokerActions(AdapterLog),
                Feed = _feed,
                StatePath = StatePath,
                LedgerPath = LedgerPath,
                // One run id per process: monotonic continuity exists only inside it (SPEC §6.4, §17.2).
                RunId = Guid.NewGuid().ToString("N"),
                LedgerObserver = OnLedgerEntry
            });

            lock (_gate) _guardian.Start();
            AdapterLog("Core started; state=" + _guardian.Status.Kind);

            // M15: the guarded account comes from the restored seal or it does not exist. This runs
            // BEFORE SubscribeToAccount, which used to act on a hardcoded default at this exact point.
            ResolveGuardedAccount("boot");

            // Only now. Account.AccountStatusUpdate is a STATIC event (found by compiling against the
            // real assemblies) and NinjaTrader fires it immediately: registering it before Core exists
            // made the handler run twice against a null guardian on the very first real startup.
            Account.AccountStatusUpdate += OnAccountStatusUpdate;
            // SubscribeToAccount already ran inside ResolveGuardedAccount("boot") above.
            // Session-end hooks. Their job CHANGED when e.Cancel was removed and the comment must
            // say the new one rather than the old: nothing is refused any more, so these no longer
            // prevent a hang - by construction there is none. What they prevent is scheduling a
            // RETURN inside a process that is going away. Shutdown() covers that too, but it may run
            // after the window has already closed, so these arrive first.
            //
            // Both are subscribed rather than one: SystemEvents does not need Application.Current,
            // which RunOnUi already treats as possibly null.
            try { Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding; }
            catch (Exception ex) { AdapterLog("SessionEnding hook: " + ex.Message); }
            RunOnUi(() =>
            {
                try { if (Application.Current != null) Application.Current.SessionEnding += OnAppSessionEnding; }
                catch (Exception ex) { AdapterLog("App SessionEnding hook: " + ex.Message); }
            });

            ShowWindow();

            // SPEC §5.6: the evaluation floor. Everything else is event-driven.
            _timer = new Timer(_ => Tick(), null, Constants.PnlEvaluationIntervalMs,
                               Constants.PnlEvaluationIntervalMs);
        }

        private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e)
        {
            AdapterLog("windows session ending - the panel stops arguing");
            _stopping = true;
            RunOnUi(() => _window?.AllowClose());
        }

        private void OnAppSessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            AdapterLog("application session ending - the panel stops arguing");
            _stopping = true;
            _window?.AllowClose();
        }

        /// <summary>The nap. The window is gone; this brings it back WHERE THE TRADER LEFT IT - the
        /// position lives in ui.json, so it reappears in place rather than in a corner - and without
        /// stealing focus, because the window is created with ShowActivated = false.</summary>
        private void Snooze()
        {
            _snoozed = true;
            _snoozedUnder = RenderedState();
            try { if (_snoozeTimer != null) _snoozeTimer.Dispose(); } catch { }
            _snoozeTimer = new Timer(_ => WakeFromSnooze(), null, SnoozeMs, Timeout.Infinite);
        }

        private void WakeFromSnooze()
        {
            if (_stopping) return;
            _snoozed = false;
            try { if (_snoozeTimer != null) _snoozeTimer.Dispose(); } catch { }
            _snoozeTimer = null;
            ShowWindow();
        }

        /// <summary>What the panel is currently showing, as one string. A CHANGE of this cuts a nap
        /// short: the nap is about the window, not about the state, and a state change is exactly the
        /// moment there is something new to say.
        ///
        /// KNOWN LIMITATION, with its trigger written down: a FailClosed that FLAPS - a data feed
        /// reconnecting repeatedly, which this machine's own logs show happening - would wake the
        /// panel on every flap. If that is ever observed, the fix is to require the new state to
        /// differ from the one napped under rather than merely to have changed. Not built now:
        /// speculating about which flap matters is how the 165-message storm got designed.</summary>
        private string RenderedState()
        {
            lock (_gate)
            {
                var s = _guardian.Status;
                return s.Kind + "/" + (_guardian.LockoutNeedsHuman ? "needs" : "-") + "/" + (s.Reason ?? "");
            }
        }

        private void Shutdown()
        {
            _stopping = true;
            RunOnUi(() => _window?.AllowClose());
            try { Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding; } catch { }
            RunOnUi(() =>
            {
                try { if (Application.Current != null) Application.Current.SessionEnding -= OnAppSessionEnding; }
                catch { }
            });
            try { if (_snoozeTimer != null) _snoozeTimer.Dispose(); } catch { }
            try { _timer?.Dispose(); } catch { }
            try { Account.AccountStatusUpdate -= OnAccountStatusUpdate; } catch { }
            UnsubscribeFromAccount();
            lock (_gate) { try { _guardian?.Stop(); } catch (Exception ex) { AdapterLog("Stop: " + ex.Message); } }
            CloseWindow();
            AdapterLog("shutdown complete");
        }

        // ---------------- NT8 events in ----------------

        /// <summary>One resolver for boot and arm. Reads Core's sealed config, decides through
        /// GuardedAccountRule (pure, tested without the platform), logs the outcome either way, and
        /// only then touches the subscription.</summary>
        private void ResolveGuardedAccount(string when)
        {
            GuardedAccountDecision decision;
            lock (_gate) decision = GuardedAccountRule.Decide(_guardian.GuardedAccounts);

            _guardedAccount = decision.Account;
            AdapterLog("guarded account (" + when + "): " + decision);
            SubscribeToAccount();
        }

        private void SubscribeToAccount()
        {
            UnsubscribeFromAccount();
            if (_guardedAccount == null)
            {
                // Not an oversight: no configuration is in force, so there is nothing safe to watch.
                // Core reports the truth through the feed on its next tick (SPEC §10).
                AdapterLog("no subscription: no guarded account is resolved");
                return;
            }
            var a = Accounts.Find(_guardedAccount);
            if (a == null)
            {
                // Not an error and not a guess: Core is told the truth by the feed on the next tick,
                // and an account it cannot see is an unknown, which blocks entries (SPEC §10).
                AdapterLog("account '" + _guardedAccount + "' not present yet");
                return;
            }

            a.ExecutionUpdate += OnExecutionUpdate;
            a.OrderUpdate += OnOrderUpdate;
            _subscribed = a;
            AdapterLog("subscribed to " + a.Name);
        }

        private void UnsubscribeFromAccount()
        {
            if (_subscribed == null) return;
            try
            {
                _subscribed.ExecutionUpdate -= OnExecutionUpdate;
                _subscribed.OrderUpdate -= OnOrderUpdate;
            }
            catch { }
            _subscribed = null;
        }

        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                if (e?.Execution == null) return;
                var record = ExecutionTranslator.Translate(e.Execution);
                lock (_gate) _guardian.OnExecution(record);
                RefreshWindow();
            }
            catch (Exception ex) { AdapterLog("OnExecutionUpdate: " + ex.Message); }
        }

        /// <summary>SPEC §9.5. There is no pre-submit veto in NT8 — verified at runtime across 2,912
        /// types, zero candidate events — so enforcement is detect-and-cancel and this handler is the
        /// detect half. It hands the order to Core; Core decides whether it gets cancelled.</summary>
        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            try
            {
                if (e?.Order == null) return;
                if (!Accounts.IsWorking(e.OrderState)) return;

                var snapshot = new OrderSnapshot(
                    _guardedAccount,
                    e.Order.OrderId ?? e.Order.Id.ToString(CultureInfo.InvariantCulture),
                    e.Order.Instrument == null ? "?" : e.Order.Instrument.FullName,
                    e.Order.OrderAction.ToString());

                lock (_gate) _guardian.OnOrderObserved(snapshot);
                RefreshWindow();
            }
            catch (Exception ex) { AdapterLog("OnOrderUpdate: " + ex.Message); }
        }

        private void OnAccountStatusUpdate(object sender, AccountStatusEventArgs e)
        {
            if (_guardian == null) return;
            // The connection arrives after Configure (verified). Re-resolve so a replaced instance
            // cannot leave us subscribed to a dead object.
            try { SubscribeToAccount(); Tick(); }
            catch (Exception ex) { AdapterLog("OnAccountStatusUpdate: " + ex.Message); }
        }

        private void Tick()
        {
            if (_guardian == null) return;   // not yet built; see Boot()
            try
            {
                lock (_gate)
                {
                    _guardian.Tick();
                    // SPEC §7.4: a config file that no longer matches the sealed snapshot is tampering.
                    if (File.Exists(ConfigPath))
                    {
                        var text = File.ReadAllText(ConfigPath);
                        if (text != _lastConfigText)
                        {
                            _lastConfigText = text;
                            _guardian.OnConfigFileObserved(text);
                        }
                    }
                }
                RefreshWindow();
            }
            catch (Exception ex) { AdapterLog("Tick: " + ex.Message); }
        }

        // ---------------- arming, from the window ----------------

        private string Arm()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return "no config at " + ConfigPath;

                var text = File.ReadAllText(ConfigPath);
                _lastConfigText = text;

                OperationResult result;
                lock (_gate) result = _guardian.Arm(text);

                if (result.Ok)
                {
                    // The guarded account may have changed with the config; same resolver as boot,
                    // so there is exactly one dialect of this decision.
                    // The reset time, the zone and the limit used to be copied out of the config
                    // here - the only place that ever assigned them, which is why a restore left them
                    // at their type's default (LT-2). They are read from Core now, at the point of use.
                    ResolveGuardedAccount("arm");
                    AdapterLog("ARMED");
                    return null;
                }

                AdapterLog("arm rejected: " + result);
                return result.ToString();
            }
            catch (Exception ex)
            {
                AdapterLog("Arm: " + ex);
                return ex.Message;
            }
        }

        // ---------------- exporting, from the window ----------------

        /// <summary>SPEC section 3c: the certificate exists ONLY because a human pressed this.
        /// Nothing on the engine's side calls it - not the timer, not a breach, not shutdown.
        /// It writes two files next to the ledger and sends nothing anywhere.</summary>
        private string ExportDay()
        {
            try
            {
                // The adapter already owns these paths; there is no need to reach into Core for them.
                if (!File.Exists(LedgerPath)) return "no ledger at " + LedgerPath;
                if (!File.Exists(StatePath)) return "no state file yet";

                PersistedState state; string stateError;
                if (!PersistedState.TryParse(File.ReadAllText(StatePath), out state, out stateError))
                    return "state unreadable: " + stateError;
                if (state.Seal == null) return "nothing armed today, so there is no commitment to certify";

                var store = new NtFileStore();
                var ledger = new Ledger(store, LedgerPath);
                var verify = ledger.Verify();
                var entries = ledger.ReadAll().ToList();

                var request = new CertificateRequest
                {
                    Alias = ReadAlias(),
                    DayKey = state.DayKey,
                    AccountSalt = LoadOrCreateSalt(),
                    IssuerVersion = IssuerIdentity.VersionOf(typeof(Certificate).Assembly),
                    IssuerBuildHash = IssuerIdentity.BuildHashOf(ReadCoreAssemblyBytes()),
                    // DaysCovered is no longer set: since cert-1 the emitter derives it from the
                    // day span, so passing a number here would be stating a figure nobody checks.
                };

                var result = Certificate.Issue(entries, state, request, verify.Ok);
                if (!result.Ok) return result.Reason;

                var dir = Path.Combine(HomeDir, "certificates");
                Directory.CreateDirectory(dir);
                var stem = Path.Combine(dir, "certificate-" + request.DayKey);
                File.WriteAllText(stem + ".json", result.Json, new UTF8Encoding(false));
                File.WriteAllText(stem + ".html", result.Html, new UTF8Encoding(false));

                AdapterLog("certificate issued " + result.CertHash.Substring(0, 12) + " -> " + stem + ".json");
                if (!verify.Ok)
                    AdapterLog("certificate says ledgerVerified=false (chain breaks at seq " + verify.BrokenSeq + ")");
                return null;
            }
            catch (Exception ex)
            {
                AdapterLog("ExportDay: " + ex);
                return ex.Message;
            }
        }

        /// <summary>The alias is the trader's, so it comes from a file they control. No alias
        /// file means no invented alias: the emitter refuses and says so.</summary>
        private string ReadAlias()
        {
            var path = Path.Combine(HomeDir, "alias.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }

        /// <summary>GuardianCore's own bytes, so issuer.buildHash fingerprints the build that
        /// actually produced the document. Null when unreadable, and the field is then omitted -
        /// the old code hashed Assembly.FullName, which only changes when the version does.</summary>
        private static byte[] ReadCoreAssemblyBytes()
        {
            try
            {
                var path = typeof(Certificate).Assembly.Location;
                return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : File.ReadAllBytes(path);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>SPEC A.7: 32 random bytes, made once, kept here, never in the document.</summary>
        private string LoadOrCreateSalt()
        {
            var path = Path.Combine(HomeDir, "account_salt.txt");
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (existing.Length >= 32) return existing;
            }
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var sb = new StringBuilder(64);
            foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            var salt = sb.ToString();
            File.WriteAllText(path, salt, new UTF8Encoding(false));
            AdapterLog("created account_salt.txt - keep it with the ledger; it is never published");
            return salt;
        }

        // ---------------- the window ----------------

        /// <summary>Both halves swallow everything, by contract. The guardian must be able to LOSE
        /// this file with no consequence: missing, corrupt, unreadable or unwritable all mean a fresh
        /// panel in the corner and nothing else. No exception, no fail-closed, no ledger event -
        /// comfort is not a premise this guardian acts on.</summary>
        private static UiPrefs LoadUiPrefs()
        {
            try { return UiPrefs.Parse(File.Exists(UiPrefsPath) ? File.ReadAllText(UiPrefsPath) : null); }
            catch { return new UiPrefs(); }
        }

        private static void SaveUiPrefs(UiPrefs prefs)
        {
            try { File.WriteAllText(UiPrefsPath, prefs.Format(), new UTF8Encoding(false)); }
            catch { }
        }

        private void ShowWindow()
        {
            RunOnUi(() =>
            {
                _window = new GuardianStatusWindow(Arm, ExportDay, LoadUiPrefs, SaveUiPrefs);
                _window.SnoozeRequested = Snooze;
                _window.Show();
                _window.Render(Snapshot());
            });
        }

        private void CloseWindow()
        {
            RunOnUi(() => { try { _window?.Close(); } catch { } _window = null; });
        }

        /// <summary>The two lockout messages, and the only place the platform is told anything.
        ///
        /// Best-effort by contract: an exception here cannot break the append or stop the lockout, and
        /// Core counts the failure and publishes it (NOTIFY_FAILED, and again in GUARDIAN_STOPPED) so
        /// that "the guardian explains what happened" is a checkable claim rather than a hope.
        ///
        /// The strings come from GuardianCore.Messages - the same ones the status window renders, per
        /// AMENDMENTS A10. Not a version for the log and a version for the window.</summary>
        private void OnLedgerEntry(LedgerEntry entry)
        {
            if (entry == null) return;

            var until = Messages.Until(_guardian.SealedSessionResetLocalTime, _guardian.SealedSessionResetTimeZone);

            switch (entry.Event)
            {
                case Ev.LimitBreached:
                    // FIRST message: written before the broker has been touched, so it speaks in the
                    // future and claims nothing was done. It also warns about NinjaTrader's own
                    // "Disabling NinjaScript strategy" BEFORE NinjaTrader writes it - the Log is read
                    // downwards, and an explanation arriving afterwards corrects nothing.
                    _breachDayLoss = MoneyOf(entry, "dayLoss");
                    Announce(Messages.LockoutImminent(_guardedAccount, _breachDayLoss,
                                                      _guardian.SealedPersonalDailyLossLimit));
                    break;

                case Ev.OrdersCancelled:
                    // (int?) rather than ?? 0: an event without the field is unknown, not zero.
                    _lastCancelCount = (int?)entry.Payload?.GetInt("count");
                    break;

                case Ev.FlattenVerified:
                    // SECOND message: past tense, real figures, only now that they are true.
                    // The figure LIMIT_BREACHED carried, not a fresh read: message 2 reports the loss
                    // that caused the lockout, and a re-read could show a different number for a
                    // reason the reader has no way to see.
                    Announce(Messages.LockoutComplete(_guardedAccount, _breachDayLoss,
                                                      _guardian.SealedPersonalDailyLossLimit,
                                                      _lastCancelCount, until));
                    break;

                case Ev.LockoutIncomplete:
                    // ONLY when the event says it is terminal. The first real run (2026-08-22) showed
                    // the transient one appearing ~500 ms BEFORE a successful FLATTEN_VERIFIED: firing
                    // here unconditionally would tell every user, in every ordinary lockout, to go and
                    // hand-close a position that is closing itself.
                    //
                    // And the field must be PRESENT and true. Two other sites emit this event for
                    // per-step exceptions and carry NEITHER key; absence is not false, it is a
                    // different event, so it is required rather than inferred.
                    //
                    // TWO KEYS, ON PURPOSE, AND NOT FOREVER-BY-ACCIDENT. The key was renamed
                    // `exhausted` -> `needsHuman` on 2026-09-02 to match Guardian.LockoutNeedsHuman.
                    // The 169 entries written before that carry the old key and are NOT rewritten -
                    // editing an append-only record to make a rename tidy is the one thing this
                    // product exists not to do - so a reader that dropped `exhausted` would go blind
                    // on every one of them. New key first: once both are present the new one wins,
                    // and nothing here has to know which emitter wrote the line.
                    var needsHuman = entry.Payload?.GetBool("needsHuman")
                                     ?? entry.Payload?.GetBool("exhausted");
                    if (needsHuman == true)
                        Announce(Messages.LockoutStillOpen(_guardedAccount,
                                                           (int)(entry.Payload.GetInt("attempts") ?? 0)));
                    break;
            }
        }

        /// <summary>One line, at the loudest level NinjaTrader has, in a category that is not the
        /// `Default` its own strategy-disabling message uses - so the guardian's explanation is not
        /// buried beside it.</summary>
        private void Announce(string message)
        {
            // Fully qualified: inside namespace NinjaTrader.NinjaScript.AddOns, a bare `NinjaScript`
            // resolves to the enclosing NAMESPACE, not to the class of the same name.
            // LogLevel.Alert is the loudest NinjaTrader has and the rarest, so the guardian's
            // explanation is not buried next to its own informational messages.
            try { global::NinjaTrader.NinjaScript.NinjaScript.Log(message, global::NinjaTrader.Cbi.LogLevel.Alert); }
            catch (Exception ex) { AdapterLog("announce failed: " + ex.Message); throw; }
        }

        private static decimal MoneyOf(LedgerEntry entry, string key)
        {
            decimal v;
            var raw = entry.Payload?.GetString(key);
            return raw != null && Money.TryParse(raw, out v) ? v : 0m;
        }

        // ---------------- the audible channel (docs/proposals/the-channel-20260831.md) ----------------
        //
        // WHY THIS EXISTS AT ALL: asked whether he reads the NinjaTrader log, the one person using this
        // product said "no lo miro en realidad". The panel can be collapsed, snoozed, or behind a
        // chart. Sound is the only channel that reaches someone who is not looking - and it is still
        // only an ATTEMPT, because nothing here can observe whether a human heard anything. That is
        // why every word it puts on screen says what was CHECKED, never what was concluded, and why
        // the acknowledgement - the only thing that would close the gap - is a separate piece waiting
        // on the extension contract.
        //
        // The decisions are all in SoundChannel (pure, tested in Snd1_SoundChannelTests). This method
        // does only what a test cannot: read NT8's settings and make a noise.
        private bool _everSounded;
        private long _lastSoundMs;
        private string _soundNote;

        private static long MonotonicMs()
        {
            // Not DateTime: a wall-clock jump must not silence an alert for five minutes, and this
            // project has a whole rule family about pairing on clocks it did not verify.
            return System.Diagnostics.Stopwatch.GetTimestamp() / (System.Diagnostics.Stopwatch.Frequency / 1000L);
        }

        /// <summary>Sounds while the guardian NEEDS A PERSON - the one state where this product cannot
        /// finish on its own. Immediate, then every five minutes, flat, for as long as the condition
        /// lasts. Deliberately NOT bounded: the alert's real job is to reach someone who stepped away,
        /// and an alarm that gives up loses exactly the case it was built for.</summary>
        private void AlertIfNeeded(GuardianStatusWindow.View v)
        {
            // The latch drops when the condition clears, so the next episode starts loud rather than
            // serving out the old interval. The rule is SoundChannel.KeepSoundedLatch and it is tested
            // (Snd1k) - written as an assignment here it would have been a decision no test can reach.
            _everSounded = SoundChannel.KeepSoundedLatch(v.NeedsHuman, _everSounded);
            if (!v.NeedsHuman)
            {
                _soundNote = null;
                return;
            }

            var now = MonotonicMs();
            if (!SoundChannel.ShouldSoundNow(_everSounded, _lastSoundMs, now)) return;
            _lastSoundMs = now;
            _everSounded = true;

            // Both settings are read into locals BEFORE either is assigned, so a throw on the second
            // read cannot leave the first one looking authoritative. Failing to read a setting is not
            // evidence that the setting is fine: it becomes Unknown, and Unknown falls back.
            double? volume = null;
            string path = null;
            try
            {
                var options = NinjaTrader.Core.Globals.GeneralOptions;
                var v0 = options.SoundVolume;
                var p0 = options.SoundAnnouncement;
                volume = v0;
                path = p0;
            }
            catch (Exception ex) { AdapterLog("sound settings unreadable: " + ex.Message); }

            var health = SoundChannel.Assess(volume, path, File.Exists);
            _soundNote = Messages.DetailSoundChannel(health);      // null when healthy - no line at all

            try
            {
                if (SoundChannel.UseFallback(health))
                {
                    // Ignores NinjaTrader's sound configuration, which is the point: it is the way out
                    // when that configuration is what is broken. It is a SECOND ATTEMPT BY ANOTHER
                    // ROUTE and is never described as more than that - whether it is audible depends on
                    // the Windows mixer, the output device and the room, none of which are observable
                    // from here.
                    System.Media.SystemSounds.Exclamation.Play();
                    AdapterLog("alert: fallback sound, channel " + health);
                }
                else
                {
                    // The trader's own configured announcement file, whose existence was just verified.
                    // Announcement is the only SoundType that does not lie about what happened - using
                    // OrderFilled for our alert would be a falsehood in the audio channel.
                    NinjaTrader.Core.Globals.PlaySound(path);
                    AdapterLog("alert: announcement sound");
                }
            }
            catch (Exception ex) { AdapterLog("PlaySound: " + ex.Message); }
        }

        private void RefreshWindow()
        {
            // Hoisted ABOVE the nap check on purpose: napping is a decision about the WINDOW, and the
            // audible channel exists precisely for the moments the window is not being looked at.
            var snapshot = Snapshot();
            AlertIfNeeded(snapshot);
            snapshot.SoundNote = _soundNote;   // after the alert: the note is what THIS pass established

            // A state change CUTS THE NAP SHORT. The nap is about the window, not about the state.
            if (_snoozed && !_stopping && RenderedState() != _snoozedUnder)
            {
                AdapterLog("state changed while the panel was napping - bringing it back");
                WakeFromSnooze();
                return;
            }

            RunOnUi(() => _window?.Render(snapshot));
        }

        private GuardianStatusWindow.View Snapshot()
        {
            lock (_gate)
            {
                var s = _guardian.Status;
                return new GuardianStatusWindow.View
                {
                    Kind = s.Kind,
                    Reason = s.Reason,
                    Account = _guardedAccount,
                    SecondsToExpiry = SecondsToExpiry(),
                    HasSeal = s.Sealed,
                    Until = Messages.Until(_guardian.SealedSessionResetLocalTime, _guardian.SealedSessionResetTimeZone),
                    ConfigPath = ConfigPath,
                    NeedsHuman = _guardian.LockoutNeedsHuman,
                    Limit = _guardian.SealedPersonalDailyLossLimit
                };
            }
        }

        /// <summary>Presentation only. The seal's expiry is decided by Core (SPEC §7.5); this is the
        /// countdown the trader sees, and it is deliberately computed from the same wall-clock value
        /// Core stores rather than from a second source of truth.</summary>
        private long SecondsToExpiry()
        {
            try
            {
                if (!File.Exists(StatePath)) return -1;
                var text = File.ReadAllText(StatePath);
                if (!PersistedState.TryParse(text, out var state, out _)) return -1;
                if (state.Seal == null) return -1;
                var remaining = (state.Seal.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds;
                return remaining < 0 ? 0 : (long)remaining;
            }
            catch { return -1; }
        }

        private static void RunOnUi(Action action)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;
                if (app.Dispatcher.CheckAccess()) action();
                else app.Dispatcher.BeginInvoke(action);
            }
            catch { }
        }

        private static void AdapterLog(string line)
        {
            try
            {
                Directory.CreateDirectory(HomeDir);
                File.AppendAllText(AdapterLogPath,
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine);
            }
            catch { }
        }
    }

    /// <summary>Minimal and visible, as SPEC §9.4 requires of a lockout that could not complete: the
    /// trader must be able to tell, at a glance and without opening anything, whether they are
    /// protected. Three states, one colour each, and the reason spelled out when they are not.</summary>
    public class GuardianStatusWindow : Window
    {
        public class View
        {
            public StateKind Kind;
            public string Reason;
            public string Account;
            public long SecondsToExpiry;
            public bool HasSeal;
            public string Until;
            public string ConfigPath;

            /// <summary>What the guardian established about its OWN alert channel, in words, or null
            /// when there is nothing to say. Null on a healthy channel on purpose: a line that appears
            /// every time is a line nobody reads, which is the lesson the 165-warning storm paid for.
            /// It never claims the trader heard anything - that is unobservable from inside.</summary>
            public string SoundNote;

            /// <summary>LT-4 / candidate 8. Derived in Core from state that is already persisted, so
            /// it survives a restart - an adapter-side flag would be the LT-2 family again.</summary>
            public bool NeedsHuman;

            /// <summary>From the SEALED config, so it is right after a restore. Nullable because an
            /// absent figure is suppressed rather than printed as $0.00 (LT-2).</summary>
            public decimal? Limit;
        }

        private readonly Func<string> _arm;
        private readonly Func<string> _export;
        private readonly TextBlock _headline = new TextBlock();
        private readonly TextBlock _detail = new TextBlock();
        private readonly TextBlock _countdown = new TextBlock();
        private readonly Button _armButton = new Button();
        private readonly Button _exportButton = new Button();
        private readonly Border _root = new Border();

        private readonly Action<UiPrefs> _savePrefs;
        private double _appliedWidth;
        private bool _repositioning;      // true while WE move it, so our own move is not saved as theirs
        private readonly TextBlock _strip = new TextBlock();
        private readonly Button _collapseButton = new Button();
        private bool _collapsed;
        private bool _stripAllowed = true;
        private StateKind _renderedKind = StateKind.Disarmed;
        private bool _haveRendered;       // the first render counts as a transition
        private bool _sealInForce;
        private bool _allowClose;

        /// <summary>Raised when a close was turned into a nap instead of a refusal. The window cannot
        /// bring itself back once closed, so the addon owns the timer.</summary>
        public Action SnoozeRequested;

        /// <summary>Opened by the addon on its own shutdown AND on Windows ending the session. After
        /// this the window never argues with anything again.</summary>
        public void AllowClose() { _allowClose = true; }

        public GuardianStatusWindow(Func<string> arm, Func<string> export,
                                    Func<UiPrefs> loadPrefs, Action<UiPrefs> savePrefs)
        {
            _arm = arm;
            _export = export;
            _savePrefs = savePrefs;

            Title = "deadman-guardian";
            Width = 330;
            // Size to content, never a fixed height: the detail line wraps to a variable number of
            // lines because the config path differs per machine, and the first real run clipped the
            // Arm button to about 8 visible pixels - the one control the trader has to press.
            SizeToContent = SizeToContent.Height;
            MinHeight = 190;
            WindowStyle = WindowStyle.ToolWindow;
            Topmost = true;
            // REVERSED 2026-08-31, and it is the reversal of a deliberate choice, not the repair of
            // an oversight. It was off for a good reason: a small tool window has no business
            // cluttering the taskbar. The reason that now outweighs it is that the taskbar is the
            // CHEAPEST attention mechanism that exists, and the product had it switched off on the
            // day it needed attention - 2026-08-26, when it asked for help 165 times through a Log
            // tab the trader does not read, and the position stood for five days.
            ShowInTaskbar = true;
            ResizeMode = ResizeMode.NoResize;
            // Comes back WITHOUT stealing focus. Show() activates by default, and a panel that grabs
            // the keyboard once a minute from someone sending orders would be worse than the problem
            // it solves - the same reason Activate() was ruled out.
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            // WHERE THE TRADER PUT IT, not where we like it. Roberto said "me sale en primer plano y
            // lo muevo para una esquina" in the HABITUAL PRESENT: he did that every session, because
            // the position lived only in this constructor and every F5 rebuilds the window. A panel
            // that forgets is a panel that has to be tidied away daily, and the thing a trader tidies
            // away daily is the thing they eventually close.
            var area = SystemParameters.WorkArea;
            var prefs = loadPrefs == null ? new UiPrefs() : loadPrefs();
            var start = prefs.HasPosition
                ? PanelPlacement.Clamp(prefs.Left.Value, prefs.Top.Value, Width, MinHeight,
                                       area.Left, area.Top, area.Right, area.Bottom)
                : PanelPlacement.Default(Width, area.Top, area.Right);
            Left = start.Left;
            Top = start.Top;
            _appliedWidth = Width;
            _collapsed = prefs.Collapsed;

            // Saved on every move rather than on close, because the process can end without one -
            // an F5, a crash, or NinjaTrader going away. A preference kept only in memory is a
            // preference that survives exactly the sessions that did not need it.
            // STEP 3, and it is ONE mechanism rather than two: while a commitment is in force, the
            // panel cannot be dismissed FOR THE DAY. It can always be dismissed for a minute.
            //
            // An earlier draft refused the close outright (e.Cancel) where the strip was available.
            // It was removed before shipping, for five reasons and the first is decisive:
            //
            //   1. Closing cannot tell a close by the USER from a close by NINJATRADER, so refusing
            //      is always a bet on the platform's internal shutdown order. We do not gamble with
            //      anyone's machine shutting down.
            //   2. One behaviour, not two. A user does not distinguish them; a maintainer in six
            //      months would have to reconstruct why they differ.
            //   3. IT DEGRADES VISIBLY. If refusal broke - an NT8 change, a close path that skips
            //      Closing - it would break silently and we would never learn. If the nap breaks, the
            //      window does not come back, and that is seen.
            //   4. It is the honest promise. The guardian cannot guarantee the window is never
            //      closed; it can guarantee it comes back. Promising the second is what we did all
            //      day with the messages, and behaviour should not follow a different rule than text.
            //   5. It is enough for what this product IS. Roberto is protecting himself from himself,
            //      not from an attacker. A window that returns every minute is expensive and
            //      deliberate to evade - it has to be decided again, consciously, every minute -
            //      without the guardian claiming a power it does not have.
            //
            // Flat sixty seconds, never escalating: A BRAKE THAT ESCALATES THE FIGHT IS A BRAKE THAT
            // EARNS AN ENEMY.
            Closing += (s, e) =>
            {
                if (_allowClose || !_sealInForce) return;   // no commitment in force, no argument
                var nap = SnoozeRequested;
                if (nap != null) nap();
            };

            LocationChanged += (s, e) =>
            {
                if (_repositioning || _savePrefs == null) return;
                SavePrefs();
            };

            _headline.FontSize = 22;
            _headline.FontWeight = FontWeights.Bold;
            _headline.Foreground = Brushes.White;
            _headline.TextWrapping = TextWrapping.Wrap;

            _detail.FontSize = 12;
            _detail.Foreground = Brushes.White;
            _detail.TextWrapping = TextWrapping.Wrap;
            _detail.Margin = new Thickness(0, 6, 0, 0);

            _countdown.FontSize = 12;
            _countdown.Foreground = Brushes.White;
            _countdown.Margin = new Thickness(0, 6, 0, 0);

            _armButton.Content = "Arm for today";
            _armButton.Margin = new Thickness(0, 10, 0, 0);
            _armButton.Padding = new Thickness(8, 3, 8, 3);
            _armButton.HorizontalAlignment = HorizontalAlignment.Left;
            _armButton.Click += (s, e) =>
            {
                var error = _arm();
                if (error != null) _detail.Text = "Not armed: " + error;
            };

            // The strip: what stays on screen when the trader takes their desktop back. It is a
            // button so it is obviously clickable - a line of text that silently expands is a line of
            // text nobody discovers.
            _strip.FontSize = 13;
            _strip.FontWeight = FontWeights.Bold;
            _strip.Foreground = Brushes.White;
            // WRAP, not NoWrap. At 330px minus the chevron, "ARMED - $600.00 until 17:00
            // (America/Chicago)" does not fit on one line, and NoWrap would cut it SILENTLY from
            // the right - losing the timezone first, which is the one thing LT-2 established is
            // never dropped from a time. Wrapping costs a second line and loses nothing; the window
            // sizes to content, so it just gets slightly taller.
            _strip.TextWrapping = TextWrapping.Wrap;
            _strip.Visibility = Visibility.Collapsed;
            _strip.Cursor = System.Windows.Input.Cursors.Hand;
            _strip.MouseLeftButtonUp += (s, e) => SetCollapsed(false);

            // A CHEVRON, NOT A MINUS, and the change is not cosmetic. "-" promises MINIMISE, and
            // minimise is the one thing this product deliberately refuses to offer: it would send the
            // only channel that reaches this trader into nothing. So the glyph was promising the
            // function the code exists to withhold - this house's defect class, in one character.
            // Roberto pressed it expecting a minimise and reported that the window did not minimise.
            //
            // A chevron is the standard collapse gesture, it is symmetric (up closes, down opens),
            // and it promises nothing else. It also has NO LANGUAGE: NinjaTrader here is in Spanish
            // and this product speaks English, so a word would open a translation argument that a
            // glyph does not.
            //
            // THE FONT IS SET EXPLICITLY BECAUSE SEGOE UI DOES NOT HAVE THIS GLYPH - verified, not
            // assumed: U+2303 and U+2304 are absent from Segoe UI and present in Segoe UI Symbol.
            // WPF would probably resolve it by fallback, and "probably" is how a box character
            // ships. The escapes keep this file ASCII, which is the same reason Messages.cs is.
            _collapseButton.FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI");
            _collapseButton.Width = 22;
            _collapseButton.Padding = new Thickness(0);
            _collapseButton.HorizontalAlignment = HorizontalAlignment.Right;
            _collapseButton.Click += (s, e) => SetCollapsed(!_collapsed);

            _exportButton.Content = "Export my day";
            _exportButton.Margin = new Thickness(0, 6, 0, 0);
            _exportButton.Padding = new Thickness(8, 3, 8, 3);
            _exportButton.HorizontalAlignment = HorizontalAlignment.Left;
            _exportButton.Click += (s, e) =>
            {
                var error = _export();
                _detail.Text = error == null
                    ? "Certificate written to the certificates folder. Verify it with: "
                      + "python -m deadman.verify_certificate"
                    : "Not exported: " + error;
            };

            // The strip and the chevron share ONE ROW. Stacked vertically they needed vertical
            // space the collapsed panel does not have, and the button - second in the stack - was
            // simply clipped off the bottom. Roberto: "luego que lo minimise no me da la opcion de
            // maximisarlo". It was not unresponsive; it was not on screen.
            var topRow = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_collapseButton, Dock.Right);
            topRow.Children.Add(_collapseButton);
            topRow.Children.Add(_strip);

            var panel = new StackPanel { Margin = new Thickness(14, 14, 14, 18) };
            panel.Children.Add(topRow);
            panel.Children.Add(_headline);
            panel.Children.Add(_detail);
            panel.Children.Add(_countdown);
            panel.Children.Add(_armButton);
            panel.Children.Add(_exportButton);

            _root.Child = panel;
            Content = _root;
        }

        /// <summary>Collapse to a strip, or come back. The strip STAYS ON SCREEN - it is not a
        /// minimise. Minimising to the taskbar would send the only channel that reaches this trader
        /// into nothing, which is candidate 9 with an official button instead of the X.</summary>
        private void SetCollapsed(bool collapsed)
        {
            _collapsed = collapsed;
            ApplyCollapsed();
            SavePrefs();
        }

        private void ApplyCollapsed()
        {
            var showStrip = _collapsed ? Visibility.Visible : Visibility.Collapsed;
            var showFull = _collapsed ? Visibility.Collapsed : Visibility.Visible;

            _strip.Visibility = showStrip;
            _headline.Visibility = showFull;
            _detail.Visibility = showFull;
            _countdown.Visibility = showFull;
            // ONE button, two glyphs, symmetric: up closes, down opens. It lives in BOTH modes -
            // collapsed, it is how you come back - and is HIDDEN, never disabled, where collapsing is
            // refused: a button that does not respond reads as a broken product, an absent one reads
            // as a state that does not offer it.
            _collapseButton.Content = _collapsed ? "\u2304" : "\u2303";
            _collapseButton.ToolTip = _collapsed
                ? "Show the whole panel again."
                : "Shrink to a strip. It stays on screen - this is not a minimise.";
            _collapseButton.Visibility = _stripAllowed ? Visibility.Visible : Visibility.Collapsed;
            if (_collapsed) { _armButton.Visibility = Visibility.Collapsed;
                              _exportButton.Visibility = Visibility.Collapsed; }
            else if (_exportButton.Visibility != Visibility.Visible) _exportButton.Visibility = Visibility.Visible;

            // NEVER A FIXED HEIGHT, and the constructor says why twenty lines up: "the first real
            // run clipped the Arm button to about 8 visible pixels - the one control the trader has
            // to press". The first draft of this method set Height = 40 and reintroduced exactly
            // that, in the same file, against a warning already written in it. Only MinHeight moves;
            // the content decides the rest, in both modes.
            MinHeight = _collapsed ? 0 : 190;
            SizeToContent = SizeToContent.Height;
        }

        /// <summary>One writer for the comfort file, so position and collapsed can never be saved
        /// separately and disagree.</summary>
        /// <summary>One writer for the comfort file, so position and collapsed can never be saved
        /// separately and disagree - and the collapsed flag goes through PanelCollapse.PersistCollapsed
        /// rather than straight out of _collapsed. See that method: writing it raw is what let a
        /// collapse made in DISARMED survive into the next day.</summary>
        private void SavePrefs()
        {
            if (_savePrefs == null) return;
            _savePrefs(new UiPrefs
            {
                Left = Left,
                Top = Top,
                Collapsed = PanelCollapse.PersistCollapsed(_collapsed, _renderedKind)
            });
        }

        /// <summary>Resize WITHOUT moving house. The first draft recomputed Left from the corner of
        /// the work area, in Render, which runs on every refresh - so a panel the trader had dragged
        /// would have walked back to the corner about once a second. It was caught by one question
        /// nobody had asked all day: can the user MOVE this thing? A message review is not a
        /// manipulation review.
        ///
        /// The right edge stays put, so it grows leftward from wherever they left it, and the clamp
        /// only intervenes if that would put it off screen. And it runs ONLY when the width actually
        /// changes, so an unchanged panel is never repositioned at all.</summary>
        private void ApplyWidth(double wanted)
        {
            if (Math.Abs(wanted - _appliedWidth) < 0.5) return;

            var area = SystemParameters.WorkArea;
            var wantedLeft = PanelPlacement.LeftAfterWidthChange(Left, _appliedWidth, wanted);
            var height = ActualHeight > 0 ? ActualHeight : MinHeight;

            _repositioning = true;
            try
            {
                Width = wanted;
                var p = PanelPlacement.Clamp(wantedLeft, Top, wanted, height,
                                             area.Left, area.Top, area.Right, area.Bottom);
                Left = p.Left;
                Top = p.Top;
            }
            finally { _repositioning = false; }

            _appliedWidth = wanted;
        }

        public void Render(View v)
        {
            if (v == null) return;

            // Free channel, read without switching windows - and until 2026-08-31 it was the constant
            // "deadman-guardian", which tells the reader only what they already know.
            Title = Messages.WindowTitle(v.Kind, v.NeedsHuman);

            // AUTO-EXPAND, and it is not a courtesy. Two states may not be a strip in a corner: the
            // one that needs a person, and the one where the guardian is BLIND - there the trader
            // believes he has a brake and does not, which is worse than knowing his day is over. If
            // the panel is collapsed when either arrives, it opens itself.
            _sealInForce = v.HasSeal;
            _stripAllowed = PanelCollapse.MayCollapse(v.Kind, v.NeedsHuman, v.Reason);
            if (_collapsed && !_stripAllowed) _collapsed = false;

            // And DISARMED always opens. Collapse on Tuesday, the session close leaves it DISARMED,
            // and Wednesday would open to a strip reading NOT ARMED that asks for nothing - a product
            // that depends on one voluntary act per day, with its only button behind a click nobody
            // remembers. Every day starts with the product asking for its own.
            // A TRANSITION, NOT A STANDING CONDITION - and getting that wrong was a real defect,
            // reported by Roberto on the first boot: he pressed the collapse button while DISARMED,
            // the panel collapsed, and a second later it opened by itself. From the outside that is
            // indistinguishable from a button that does nothing.
            //
            // The rule agreed was "on ENTERING Disarmed the panel opens itself" - the moment the day
            // closes, so tomorrow starts with the product asking for its own. It was implemented as
            // "force it open WHILE Disarmed", which is a standing condition, and that quietly removed
            // a capability the same design had granted: collapsing in Disarmed for the session.
            //
            // The first render counts as a transition on purpose: booting into Disarmed with a
            // remembered collapse is exactly the Tuesday-to-Wednesday case the rule exists for.
            var forcedOpen = PanelCollapse.ShouldOpenItself(_collapsed, v.Kind, _renderedKind, !_haveRendered);
            _renderedKind = v.Kind;
            _haveRendered = true;
            if (forcedOpen) _collapsed = false;

            switch (v.Kind)
            {
                // Every string below comes from GuardianCore.Messages, which the NinjaTrader Log also
                // consumes (AMENDMENTS A10). Not a version for the window and a version for the log:
                // two copies of a sentence never diverge together - one gets corrected and the other
                // does not, so the survivor is by construction the stale one.

                case StateKind.Armed:
                    _root.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));   // green
                    _headline.Text = Messages.Headline(StateKind.Armed);
                    _detail.Text = Messages.DetailArmed(v.Account);
                    _armButton.Visibility = Visibility.Collapsed;
                    break;

                case StateKind.Locked:
                    // Two lockouts share a Kind and they are not the same situation. The ordinary one
                    // promises that no position stays open. The other one is the single state where
                    // this product depends on a person - and until 2026-08-31 it said the ordinary
                    // text while a position stood open and stuck, because `exhausted` is a field on an
                    // event and Render only ever switched on Kind. The panel is the one surface a
                    // trader sees without looking for it; message 3 went to a Log tab he does not read.
                    // COLOURS CORRECTED before this ever shipped: the first draft painted the
                    // needs-a-human state ORANGE - softer than the ordinary lockout's red, and the
                    // same orange FailClosed already uses. That is the day's own defect in the visual
                    // channel: the most urgent signal rendered less urgently than the routine one, and
                    // ambiguous with a different state on top of it. The stuck state is now the
                    // hottest thing the panel can be.
                    _root.Background = v.NeedsHuman
                        ? new SolidColorBrush(Color.FromRgb(0xD5, 0x00, 0x00))    // vivid: act NOW
                        : new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));   // dark red: closed
                    _headline.Text = v.NeedsHuman
                        ? Messages.HeadlineNeedsYou
                        : Messages.Headline(StateKind.Locked);
                    _detail.Text = v.NeedsHuman
                        ? Messages.DetailNeedsYou(v.Account)
                        : Messages.DetailLocked(v.Account, v.Until);
                    // The guardian reporting on the health of its OWN alert channel - the opposite of
                    // what it did on 2026-08-31, which was to believe it had warned someone. Appended
                    // rather than replacing: the instruction the person needs comes first, and the
                    // note about the channel is a qualifier on it.
                    if (v.NeedsHuman && !string.IsNullOrEmpty(v.SoundNote))
                        _detail.Text += "\n\n" + v.SoundNote;
                    // A panel that only changes hue is invisible to someone watching charts. This one
                    // GROWS - bigger headline, wider window - because peripheral vision catches a
                    // change of shape long before a change of tone.
                    _headline.FontSize = v.NeedsHuman ? 30 : 22;
                    ApplyWidth(v.NeedsHuman ? 430 : 330);
                    _armButton.Visibility = Visibility.Collapsed;
                    break;

                case StateKind.FailClosed:
                    // Used to share its headline with Disarmed (see Messages.Retired) - and they are
                    // opposites. Here the seal is alive, the guardian IS armed and IS blocking new
                    // entries, and only sight of the account is missing. The old wording misled toward
                    // the dangerous side, and on 2026-08-22 a real person went looking for an Arm
                    // button that is hidden precisely because there is nothing to arm.
                    _root.Background = new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00));   // orange
                    // FailClosed has more than one cause, and one of them makes the state headline
                    // outright false rather than merely coarse: at the daily limit on adopted figures
                    // the guardian sees the account perfectly - what it will not do is act on a number
                    // it did not witness. Telling that reader "cannot see your account", with a
                    // position still open at their limit, sends them to fix the wrong thing (M22).
                    // The rest of ui-1 - the headline deriving from the state at all - stays open.
                    _headline.Text = Messages.Headline(StateKind.FailClosed, v.Reason);
                    _detail.Text = Messages.IsLimitNotFlattened(v.Reason)
                        ? Messages.DetailLimitNotFlattened(v.Account, v.Reason, v.Until)
                        : Messages.DetailCannotSee(v.Reason, v.HasSeal, v.Until);
                    _armButton.Visibility = Visibility.Collapsed;
                    break;

                default:
                    _root.Background = new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42));   // grey
                    _headline.Text = Messages.Headline(StateKind.Disarmed);
                    _detail.Text = string.IsNullOrEmpty(v.Reason)
                        ? Messages.DetailNotArmed(v.ConfigPath)
                        : v.Reason;
                    _armButton.Visibility = Visibility.Visible;
                    break;
            }

            // AFTER the switch on purpose: the switch sets button visibility per state, and the
            // collapsed panel has no buttons at all.
            _strip.Text = Messages.Strip(v.Kind, v.NeedsHuman, v.Reason, v.Limit, v.Until);
            ApplyCollapsed();

            // If the state just took the collapse away, the FILE has to hear about it too. Leaving it
            // saying true was the whole defect: the panel obeyed the rule and the file did not, and
            // the file is what the next boot reads.
            if (forcedOpen) SavePrefs();

            if (v.SecondsToExpiry >= 0)
            {
                var t = TimeSpan.FromSeconds(v.SecondsToExpiry);
                _countdown.Text = "Seal expires in " +
                    ((int)t.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":" +
                    t.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                    t.Seconds.ToString("00", CultureInfo.InvariantCulture);
            }
            else _countdown.Text = "";
        }
    }
}
