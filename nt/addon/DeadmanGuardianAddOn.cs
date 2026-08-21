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
        private string _guardedAccount = "Sim101";     // overwritten by config on arm
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
                RunId = Guid.NewGuid().ToString("N")
            });

            lock (_gate) _guardian.Start();
            AdapterLog("Core started; state=" + _guardian.Status.Kind);

            // Only now. Account.AccountStatusUpdate is a STATIC event (found by compiling against the
            // real assemblies) and NinjaTrader fires it immediately: registering it before Core exists
            // made the handler run twice against a null guardian on the very first real startup.
            Account.AccountStatusUpdate += OnAccountStatusUpdate;
            SubscribeToAccount();
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

        private void SubscribeToAccount()
        {
            UnsubscribeFromAccount();
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
                    // The guarded account may have changed with the config; re-resolve.
                    var parsed = GuardianConfig.Parse(text);
                    if (parsed.Ok && parsed.Config.Accounts.Count > 0)
                    {
                        _guardedAccount = parsed.Config.Accounts[0];
                        SubscribeToAccount();
                    }
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
                    IssuerVersion = typeof(Certificate).Assembly.GetName().Version.ToString(),
                    IssuerBuildHash = Hashing.Sha256Hex(typeof(Certificate).Assembly.FullName).Substring(0, 16),
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
                    ConfigPath = ConfigPath
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
            public string ConfigPath;
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
            ShowInTaskbar = false;
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
                case StateKind.Armed:
                    _root.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));   // green
                    _headline.Text = "ARMED";
                    _detail.Text = "Watching " + v.Account + ". Entries allowed.";
                    _armButton.Visibility = Visibility.Collapsed;
                    break;

                case StateKind.Locked:
                    _root.Background = new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C));   // red
                    _headline.Text = "LOCKED";
                    _detail.Text = string.IsNullOrEmpty(v.Reason)
                        ? "Daily limit reached. No new entries."
                        : v.Reason;
                    _armButton.Visibility = Visibility.Collapsed;
                    break;

                case StateKind.FailClosed:
                    _root.Background = new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00));   // orange
                    _headline.Text = "NOT PROTECTED";
                    _detail.Text = "Blocked, state unknown: " + (v.Reason ?? "unknown");
                    _armButton.Visibility = Visibility.Collapsed;
                    break;

                default:
                    _root.Background = new SolidColorBrush(Color.FromRgb(0x42, 0x42, 0x42));   // grey
                    _headline.Text = "NOT PROTECTED";
                    _detail.Text = string.IsNullOrEmpty(v.Reason)
                        ? "Disarmed. Nothing is being watched. Config: " + v.ConfigPath
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
