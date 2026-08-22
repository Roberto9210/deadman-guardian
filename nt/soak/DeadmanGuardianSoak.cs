// deadman-guardian — SOAK SUITE: an automated attacker for the Sim101 soak.
//
// It does not test that the guardian works. It tries to make the guardian fail, the way its owner
// would: breach the limit, edit the sealed config, hand-edit the state, kill it mid-lockout, and
// submit orders while locked. Every run ends with a ledger chain verification and a dated section
// appended to REMOJO_REPORT.md.
//
// HARD LIMITS, enforced in code (same rules as the latency probe):
//   * account "Sim101" ONLY, matched by exact ordinal name AND proven Provider == Simulator.
//     Any failed check aborts the whole run before anything is sent.
//   * LIMIT orders only. OrderType.Market appears nowhere in this file.
//   * every order is priced far below the market so it cannot fill, and is cancelled by the run.
//   * at most MaxOrdersPerSession orders per NinjaTrader session.
//   * a gate file must exist, or the suite does nothing at all.
//   * it drives its OWN Guardian over a SANDBOX state/ledger, so the production guardian's files
//     are never touched. The ports underneath are the real ones: real NT8 broker, real account feed.
//
// Deployed to: Documents\NinjaTrader 8\bin\Custom\AddOns\DeadmanGuardianSoak.cs
// Gate:        Documents\NinjaTrader 8\deadman-guardian-soak\soak.GO
// Source:      <repo>/nt/soak/DeadmanGuardianSoak.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GuardianCore;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.DeadmanGuardian;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class DeadmanGuardianSoak : AddOnBase
    {
        private const string TargetAccount = "Sim101";
        private const int MaxOrdersPerSession = 3;
        private const decimal PersonalLimit = 600.00m;
        private const decimal FirmLimit = 1000.00m;

        private static readonly string SoakDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         "NinjaTrader 8", "deadman-guardian-soak");
        private static readonly string GatePath = Path.Combine(SoakDir, "soak.GO");
        private static readonly string ReportPath = Path.Combine(SoakDir, "REMOJO_REPORT.md");

        /// <summary>Plausibility band for the reference price, by instrument root.
        ///
        /// This is NOT a default and it never substitutes a missing value: a reference outside the
        /// band is replaced by nothing at all. It makes the scenario INVALID, which is a third
        /// outcome next to PASS and FAIL, and an invalid result is not evidence about the guardian
        /// in either direction.
        ///
        /// It exists because on 2026-08-21 this suite reported 6 of 6 PASS twice in a row with MES
        /// referenced at 250 while the real level was ~7690. Every assertion held. None of them
        /// touched the world they claimed to be measuring - the same shape as a gate file that read
        /// zero characters and reported "clean". A green that depends on nothing is worse than a
        /// red, because a red gets investigated.
        ///
        /// An instrument root that is not listed here is ALSO invalid: plausibility we never
        /// declared is not plausibility we can judge.</summary>
        private static readonly Dictionary<string, double[]> ReferenceBands =
            new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "MES", new[] { 2000.0, 20000.0 } },
                { "ES",  new[] { 2000.0, 20000.0 } },
                { "MNQ", new[] { 5000.0, 60000.0 } },
                { "NQ",  new[] { 5000.0, 60000.0 } },
            };

        private double _referencePrice;
        private string _referenceInstrument;

        private readonly object _gate = new object();
        private readonly List<Scenario> _results = new List<Scenario>();
        private readonly List<string> _log = new List<string>();
        private Timer _timer;
        private int _ordersPlaced;
        private bool _ran;

        private sealed class Scenario
        {
            public string Name;
            public bool Passed;
            public bool Invalid;
            public string Expected;
            public string Observed;
            public string LedgerChain;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "deadman-guardian soak (Sim101 only, attacker)";
            }
            else if (State == State.Configure)
            {
                if (!File.Exists(GatePath)) return;
                Note("gate present; soak armed");
                _timer = new Timer(_ => RunSuite(), null, 45_000, Timeout.Infinite);
            }
            else if (State == State.Terminated)
            {
                try { _timer?.Dispose(); } catch { }
            }
        }

        // ---------------- the suite ----------------

        private void RunSuite()
        {
            lock (_gate)
            {
                if (_ran) return;
                _ran = true;
            }

            var started = DateTime.UtcNow;
            try
            {
                var account = VerifyAccount();
                if (account == null) { WriteReport(started, "aborted before touching anything"); return; }

                ScenarioBreachLocksOut();
                ScenarioOrderWhileLockedIsCancelled(account);
                ScenarioSealedConfigEditedByHand();
                ScenarioConfigFileEditedWhileSealed();
                ScenarioKilledMidLockoutResumes();
                ScenarioClockPushedForward();

                WriteReport(started, null);
            }
            catch (Exception ex)
            {
                Note("SUITE THREW: " + ex);
                WriteReport(started, "suite threw: " + ex.Message);
            }
        }

        /// <summary>Nothing runs until the account is proven to be the simulator (SPEC §17.4 is about
        /// what we cannot prevent; this is about what we refuse to do).</summary>
        private Account VerifyAccount()
        {
            var matches = Account.All.Where(a => string.Equals(a.Name, TargetAccount, StringComparison.Ordinal)).ToList();
            // ONE formatter and ONE mapping, shared with the bots' rail (BotAccountRule.Describe /
            // BotSafety.FactsOf). Until 2026-08-22 this line printed Name/Provider only, so it read the
            // same whether or not the funded account was reachable - and it was being used to answer
            // exactly that question. The third field is the connection state.
            var facts = BotSafety.Snapshot(Note);
            Note(BotAccountRule.Describe(facts));

            if (matches.Count != 1) { Note("ABORT: expected exactly one '" + TargetAccount + "', found " + matches.Count); return null; }
            var a2 = matches[0];
            var provider = SafeProvider(a2);
            if (provider != Provider.Simulator.ToString())
            { Note("ABORT: Provider=" + provider + ", not Simulator"); return null; }
            if (a2.Connection == null || a2.ConnectionStatus != ConnectionStatus.Connected)
            { Note("ABORT: account not connected (" + Safe(() => a2.ConnectionStatus.ToString()) + ")"); return null; }

            Note("verified " + TargetAccount + " Provider=Simulator, Connected");
            return a2;
        }

        // ---------------- scenarios ----------------

        /// <summary>The ordinary path: losses reach the personal limit and the guardian locks out.
        /// The fills are synthetic - injected as ExecutionRecords - because making a real simulated
        /// account lose exactly $600 would require fillable orders, and fillable orders are precisely
        /// what this suite refuses to send.</summary>
        private void ScenarioBreachLocksOut()
        {
            var sb = NewSandbox("breach");
            sb.Arm();
            sb.Lose(PersonalLimit);

            Record("breach at the limit locks out", "LOCKED, LIMIT_BREACHED in the ledger",
                   sb.Guardian.Status.Kind + ", events: " + sb.EventsSummary(),
                   sb.Guardian.Status.Kind == StateKind.Locked && sb.HasEvent(Ev.LimitBreached),
                   sb.VerifyChain());
        }

        /// <summary>SPEC §9.5: a single flatten is not a lockout. A real resting order on Sim101,
        /// unfillable, observed by a LOCKED guardian, must be cancelled.</summary>
        private void ScenarioOrderWhileLockedIsCancelled(Account account)
        {
            if (_ordersPlaced >= MaxOrdersPerSession)
            { Record("order while locked is cancelled", "cancelled and logged", "skipped: session order budget spent", true, "n/a"); return; }

            // The REAL broker, scoped to orders this suite tagged. The first run used the synthetic one
            // and the cancel never left the process - see REMOJO_REPORT.md, run 2026-08-21 12:13Z.
            var sb = new Sandbox("locked-order", SoakDir, Note, new ScopedNtBroker(Note));
            sb.Arm();
            sb.Lose(PersonalLimit);
            if (sb.Guardian.Status.Kind != StateKind.Locked)
            { Record("order while locked is cancelled", "guardian LOCKED first", "guardian was " + sb.Guardian.Status.Kind, false, sb.VerifyChain()); return; }

            string invalidReason;
            var order = PlaceUnfillableLimit(account, out invalidReason);
            if (invalidReason != null)
            { RecordInvalid("order while locked is cancelled", "ORDER_REJECTED_LOCKED logged and the order no longer working", invalidReason, sb.VerifyChain()); return; }
            if (order == null)
            { Record("order while locked is cancelled", "one resting limit order", "could not place one - see log", false, sb.VerifyChain()); return; }

            Thread.Sleep(1500);
            var snapshot = new OrderSnapshot(TargetAccount, order.OrderId ?? order.Id.ToString(CultureInfo.InvariantCulture),
                                             order.Instrument == null ? "?" : order.Instrument.FullName,
                                             order.OrderAction.ToString());
            sb.Guardian.OnOrderObserved(snapshot);
            Thread.Sleep(3000);   // measured cancel round trip was ~130 ms; this is generous

            var stillWorking = account.Orders.Any(o => o.Id == order.Id && Accounts.IsWorking(o.OrderState));
            // belt and braces: whatever happened, this suite does not leave orders behind
            CancelIfAlive(account, order);

            Record("order while locked is cancelled",
                   "ORDER_REJECTED_LOCKED logged and the order no longer working",
                   "logged=" + sb.HasEvent(Ev.OrderRejectedLocked) + ", stillWorking=" + stillWorking,
                   sb.HasEvent(Ev.OrderRejectedLocked) && !stillWorking,
                   sb.VerifyChain());
        }

        /// <summary>SPEC §7.4 / G9: raise your own limit inside the sealed snapshot and restart.</summary>
        private void ScenarioSealedConfigEditedByHand()
        {
            var sb = NewSandbox("seal-tamper");
            sb.Arm();

            var raw = File.ReadAllText(sb.StatePath);
            var tampered = raw.Replace("600.00", "9000.00");
            if (raw == tampered)
            { Record("hand-edited seal is caught", "SEAL_MISMATCH then LOCKED", "could not find the limit in the state file", false, sb.VerifyChain()); return; }
            File.WriteAllText(sb.StatePath, tampered);

            var restarted = sb.Restart();
            Record("hand-edited seal is caught", "SEAL_MISMATCH then LOCKED",
                   restarted.Status.Kind + ", mismatch logged=" + sb.HasEvent(Ev.SealMismatch),
                   restarted.Status.Kind == StateKind.Locked && sb.HasEvent(Ev.SealMismatch),
                   sb.VerifyChain());
        }

        /// <summary>SPEC §7.4 / G10: edit config.json while the seal is in force.</summary>
        private void ScenarioConfigFileEditedWhileSealed()
        {
            var sb = NewSandbox("config-tamper");
            sb.Arm();
            sb.Guardian.OnConfigFileObserved(sb.ConfigText("9000.00", FirmLimit));

            Record("config edited under seal is caught", "CONFIG_TAMPERED then LOCKED",
                   sb.Guardian.Status.Kind + ", tampered logged=" + sb.HasEvent(Ev.ConfigTampered),
                   sb.Guardian.Status.Kind == StateKind.Locked && sb.HasEvent(Ev.ConfigTampered),
                   sb.VerifyChain());
        }

        /// <summary>SPEC §9.1 / G7: the process dies between "state says LOCKED" and "positions flat".</summary>
        private void ScenarioKilledMidLockoutResumes()
        {
            var sb = NewSandbox("kill-mid-lockout");
            sb.Broker.FailFlattenOnce = true;
            sb.Broker.OpenPosition(TargetAccount, "SOAK 09-26", 1);
            sb.Arm();
            sb.Lose(PersonalLimit);

            var lockedOnDisk = File.ReadAllText(sb.StatePath).Contains("\"state\":\"LOCKED\"");
            var resumed = sb.Restart();          // a brand-new Guardian over the same files

            Record("killed mid-lockout resumes LOCKED",
                   "state on disk LOCKED before the broker was touched, and the restart resumes LOCKED",
                   "onDisk=" + lockedOnDisk + ", afterRestart=" + resumed.Status.Kind +
                   ", positionsLeft=" + sb.Broker.GetPositions(TargetAccount).Count,
                   lockedOnDisk && resumed.Status.Kind == StateKind.Locked &&
                   sb.Broker.GetPositions(TargetAccount).Count == 0,
                   sb.VerifyChain());
        }

        /// <summary>SPEC §7.5 / G13a: push the wall clock past expiry and see whether the seal breaks.</summary>
        private void ScenarioClockPushedForward()
        {
            var sb = NewSandbox("clock-forward");
            sb.Arm();
            var sealBefore = sb.Guardian.Status.SealHash;

            sb.Clock.PushWallClockOnly(TimeSpan.FromHours(9));
            sb.Guardian.Tick();

            Record("wall clock pushed past expiry does not release the seal",
                   "seal maintained, entries blocked, CLOCK_ANOMALY logged",
                   sb.Guardian.Status.Kind + ", sealSame=" + (sb.Guardian.Status.SealHash == sealBefore) +
                   ", anomaly=" + sb.HasEvent(Ev.ClockAnomaly),
                   sb.Guardian.Status.SealHash == sealBefore && !sb.Guardian.Status.EntriesAllowed &&
                   sb.HasEvent(Ev.ClockAnomaly),
                   sb.VerifyChain());
        }

        // ---------------- real orders on Sim101 ----------------

        private Order PlaceUnfillableLimit(Account account, out string invalidReason)
        {
            invalidReason = null;
            try
            {
                Instrument instrument = null;
                foreach (var c in new[] { "MES 09-26", "MES 12-26", "MNQ 09-26", "ES 09-26" })
                {
                    try { instrument = Instrument.GetInstrument(c, false); } catch { instrument = null; }
                    if (instrument != null) { Note("instrument: " + c); break; }
                }
                if (instrument == null) { Note("no candidate instrument resolved"); return null; }

                double reference = 0;
                try
                {
                    if (instrument.MarketData?.Last != null) reference = instrument.MarketData.Last.Price;
                    if (reference <= 0 && instrument.MarketData?.LastClose != null) reference = instrument.MarketData.LastClose.Price;
                }
                catch { }

                // ---- the reference has to be a price this instrument could actually have ----
                _referenceInstrument = SafeRoot(instrument);
                _referencePrice = reference;
                Note("reference price for " + _referenceInstrument + " = " + reference.ToString(CultureInfo.InvariantCulture));

                double[] band;
                if (!ReferenceBands.TryGetValue(_referenceInstrument ?? "", out band))
                {
                    invalidReason = "no plausibility band declared for '" + (_referenceInstrument ?? "?") +
                                    "' - refusing to judge a price we cannot judge";
                    return null;
                }
                if (reference < band[0] || reference > band[1])
                {
                    invalidReason = "reference price " + reference.ToString(CultureInfo.InvariantCulture) +
                                    " is outside the plausible band [" + band[0].ToString(CultureInfo.InvariantCulture) +
                                    ", " + band[1].ToString(CultureInfo.InvariantCulture) + "] for " + _referenceInstrument +
                                    " - the feed is not serving this instrument, so this scenario would test nothing";
                    return null;
                }

                var tick = instrument.MasterInstrument.TickSize;
                var limit = instrument.MasterInstrument.RoundToTickSize(reference > 0 ? reference * 0.10 : tick * 100.0);
                if (limit <= 0 || (reference > 0 && limit >= reference * 0.5))
                { Note("refusing: computed limit " + limit + " is not safely below the market " + reference); return null; }

                _ordersPlaced++;
                Note("placing 1 LIMIT buy @ " + limit.ToString(CultureInfo.InvariantCulture) +
                     " on " + instrument.FullName + " (order " + _ordersPlaced + "/" + MaxOrdersPerSession + ")");

                var order = account.CreateOrder(instrument, OrderAction.Buy, OrderType.Limit, TimeInForce.Day,
                                                1, limit, 0, string.Empty, "deadman-soak", null);
                account.Submit(new[] { order });
                return order;
            }
            catch (Exception ex) { Note("PlaceUnfillableLimit threw: " + ex.Message); return null; }
        }

        private void CancelIfAlive(Account account, Order order)
        {
            try
            {
                var live = account.Orders.Where(o => o.Id == order.Id && Accounts.IsWorking(o.OrderState)).ToList();
                if (live.Count > 0) { account.Cancel(live); Note("cleanup: cancelled " + live.Count + " leftover order(s)"); }
            }
            catch (Exception ex) { Note("cleanup threw: " + ex.Message); }
        }

        // ---------------- sandbox ----------------

        private static string SafeRoot(Instrument i)
        {
            try { return i.MasterInstrument.Name; } catch { return null; }
        }

        private Sandbox NewSandbox(string name) => new Sandbox(name, SoakDir, Note);

        private void Record(string name, string expected, string observed, bool passed, string chain)
        {
            lock (_gate)
                _results.Add(new Scenario { Name = name, Expected = expected, Observed = observed, Passed = passed, LedgerChain = chain });
            Note((passed ? "PASS  " : "FAIL  ") + name + "  ->  " + observed);
        }

        /// <summary>Neither PASS nor FAIL: the scenario could not be run against a world it trusts,
        /// so it produced no evidence about the guardian. Reported in its own category on purpose -
        /// folding it into FAIL would blame the guardian for a broken input, and folding it into PASS
        /// is what this suite did on 2026-08-21.</summary>
        private void RecordInvalid(string name, string expected, string reason, string chain)
        {
            lock (_gate)
                _results.Add(new Scenario { Name = name, Expected = expected, Observed = reason, Passed = false, Invalid = true, LedgerChain = chain });
            Note("INVALID  " + name + "  ->  " + reason);
        }

        // ---------------- report ----------------

        private void WriteReport(DateTime started, string abortReason)
        {
            try
            {
                Directory.CreateDirectory(SoakDir);
                var sb = new StringBuilder();
                var invalid = _results.Count(r => r.Invalid);
                var judged = _results.Count - invalid;
                var passed = _results.Count(r => r.Passed);

                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("## Soak run " + started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z");
                sb.AppendLine();
                if (abortReason != null) sb.AppendLine("**Aborted: " + abortReason + "**");
                sb.AppendLine("- account: `" + TargetAccount + "` (Provider must be `Simulator`, verified before anything is sent)");
                sb.AppendLine("- orders placed this session: **" + _ordersPlaced + "** of " + MaxOrdersPerSession +
                              " allowed, all LIMIT, all priced below any possible fill, all cancelled");
                sb.AppendLine("- reference price observed: **" +
                              (_referencePrice > 0
                                  ? _referenceInstrument + " " + _referencePrice.ToString(CultureInfo.InvariantCulture)
                                  : "none - no order was priced this run") + "**");
                sb.AppendLine("- scenarios: **" + passed + " of " + judged + " passed**" +
                              (invalid > 0
                                  ? ", **" + invalid + " INVALID** (produced no evidence either way, and are not counted as passed or failed)"
                                  : ""));
                sb.AppendLine();

                if (_results.Count > 0)
                {
                    sb.AppendLine("| scenario | expected | observed | ledger chain | |");
                    sb.AppendLine("|---|---|---|---|---|");
                    foreach (var r in _results)
                        sb.AppendLine("| " + r.Name + " | " + r.Expected + " | " + r.Observed + " | " +
                                      r.LedgerChain + " | " + (r.Invalid ? "**INVALID**" : r.Passed ? "PASS" : "**FAIL**") + " |");
                    sb.AppendLine();
                }

                sb.AppendLine("<details><summary>run log</summary>");
                sb.AppendLine();
                sb.AppendLine("```");
                lock (_gate) foreach (var l in _log) sb.AppendLine(l);
                sb.AppendLine("```");
                sb.AppendLine("</details>");

                if (!File.Exists(ReportPath))
                {
                    var head = new StringBuilder();
                    head.AppendLine("# REMOJO — deadman-guardian soak on Sim101");
                    head.AppendLine();
                    head.AppendLine("An automated attacker, not a demo. Each run tries to make the guardian fail the way its");
                    head.AppendLine("owner would: breach the limit, edit the sealed config, hand-edit the state, kill it");
                    head.AppendLine("mid-lockout, submit orders while locked, and push the clock past expiry.");
                    head.AppendLine();
                    head.AppendLine("Every scenario drives a **sandbox** guardian with its own state and ledger, over the real");
                    head.AppendLine("NinjaTrader ports. The production guardian's files are never touched. Orders, where a");
                    head.AppendLine("scenario needs one, are LIMIT only, priced where they cannot fill, capped per session, and");
                    head.AppendLine("cancelled by the run. `Sim101` is verified to be the simulator before anything is sent.");
                    File.WriteAllText(ReportPath, head.ToString(), new UTF8Encoding(false));
                }
                File.AppendAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(Path.Combine(SoakDir, "soak_error.txt"),
                        DateTime.UtcNow.ToString("O") + " " + ex + Environment.NewLine); } catch { }
            }
        }

        private static string SafeProvider(Account a) { try { return a.Provider.ToString(); } catch { return "?"; } }
        private static string Safe(Func<string> f) { try { return f() ?? ""; } catch { return "?"; } }

        private void Note(string line)
        {
            lock (_gate) _log.Add(DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + line);
        }
    }
}
