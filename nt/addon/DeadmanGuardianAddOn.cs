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
            ShowWindow();

            // SPEC §5.6: the evaluation floor. Everything else is event-driven.
            _timer = new Timer(_ => Tick(), null, Constants.PnlEvaluationIntervalMs,
                               Constants.PnlEvaluationIntervalMs);
        }

        private void Shutdown()
        {
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
                    DaysCovered = 1,
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

        private void ShowWindow()
        {
            RunOnUi(() =>
            {
                _window = new GuardianStatusWindow(Arm, ExportDay);
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
                    // per-step exceptions and carry no `exhausted` at all; absence is not false, it is
                    // a different event, so it is required rather than inferred.
                    var exhausted = entry.Payload?.GetBool("exhausted");
                    if (exhausted == true)
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

        private void RefreshWindow()
        {
            var snap = Snapshot();
            RunOnUi(() => _window?.Render(snap));
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
                    NeedsHuman = _guardian.LockoutNeedsHuman
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

            /// <summary>LT-4 / candidate 8. Derived in Core from state that is already persisted, so
            /// it survives a restart - an adapter-side flag would be the LT-2 family again.</summary>
            public bool NeedsHuman;
        }

        private readonly Func<string> _arm;
        private readonly Func<string> _export;
        private readonly TextBlock _headline = new TextBlock();
        private readonly TextBlock _detail = new TextBlock();
        private readonly TextBlock _countdown = new TextBlock();
        private readonly Button _armButton = new Button();
        private readonly Button _exportButton = new Button();
        private readonly Border _root = new Border();

        public GuardianStatusWindow(Func<string> arm, Func<string> export)
        {
            _arm = arm;
            _export = export;

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
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Right - Width - 20;
            Top = SystemParameters.WorkArea.Top + 20;

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

            var panel = new StackPanel { Margin = new Thickness(14, 14, 14, 18) };
            panel.Children.Add(_headline);
            panel.Children.Add(_detail);
            panel.Children.Add(_countdown);
            panel.Children.Add(_armButton);
            panel.Children.Add(_exportButton);

            _root.Child = panel;
            Content = _root;
        }

        public void Render(View v)
        {
            if (v == null) return;

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
                    _root.Background = v.NeedsHuman
                        ? new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00))    // orange: act
                        : new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));   // red: closed
                    _headline.Text = v.NeedsHuman
                        ? Messages.HeadlineNeedsYou
                        : Messages.Headline(StateKind.Locked);
                    _detail.Text = v.NeedsHuman
                        ? Messages.DetailNeedsYou(v.Account)
                        : Messages.DetailLocked(v.Account, v.Until);
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
