// deadman-guardian — shared guardrails for the two test bots (A: the disaster, B: the prudent one).
//
// READ THIS FIRST, because it is the one thing that separates these files from everything else in nt\:
//
//   THE PROBE AND THE SOAK REFUSE TO SEND A FILLABLE ORDER. THESE BOTS EXIST TO SEND THEM.
//
// That refusal was correct for them and it left a hole that their own source admits, verbatim, in
// nt\soak\SoakSandbox.cs:
//
//     "P&L is SYNTHETIC. Making a simulated account lose exactly $600 needs fillable orders, and
//      fillable orders are what this suite refuses to send."
//
// So the 6/6 soak proves the RULES of GuardianCore over injected ExecutionRecords. It has never proved
// that the guardian fires on NinjaTrader's own accounting of real fills, nor that the §5.4 cross-check
// between Core's arithmetic and AccountItem.GrossRealizedProfitLoss holds when both are real. Bot A is
// the first thing in this repository allowed to close that hole, and Bot B is the first thing allowed
// to show that a full session of real fills produces no intervention at all.
//
// Sending fillable orders is a bigger blast radius than the soak's, so the rails are wider and harder:
//
//   * account "Sim101" ONLY. Exactly one ordinal-name match, Provider PROVEN == Simulator, Connected.
//     Any failed check aborts before a single order is constructed. Same shape as
//     DeadmanGuardianSoak.VerifyAccount, kept identical on purpose - a second dialect of the same
//     check is a second thing to get wrong.
//   * a per-session ORDER budget and a per-order and NET CONTRACT cap, refused in code, not intended.
//   * a gate file per bot, burned at start, so a run can never repeat itself by restart.
//   * MUTUAL EXCLUSION: A and B never run together. See BotGate.OtherGateBlocks for why this is a
//     correctness rule and not tidiness.
//   * automatic shutdown at the session boundary: cancel own orders, flatten own position, stop.
//
// Deployed to: Documents\NinjaTrader 8\bin\Custom\AddOns\BotGuardrails.cs
// Source:      <repo>/nt/bots/BotGuardrails.cs

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GuardianCore;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.DeadmanGuardian
{
    /// <summary>Where the bots keep their gates, their sandbox guardians and their reports. Separate
    /// from deadman-guardian-soak\ so a bot run can never be mistaken for a soak run in the record.</summary>
    public static class BotPaths
    {
        public static readonly string Root =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         "NinjaTrader 8", "deadman-guardian-bots");

        public static string Gate(string bot) { return Path.Combine(Root, "bot" + bot + ".GO"); }
        public static string Report(string bot) { return Path.Combine(Root, "BOT" + bot + "_REPORT.md"); }
        public static string RunDir(string bot, DateTime startedUtc)
        {
            return Path.Combine(Root, "runs", "bot" + bot + "-" +
                                startedUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The checks that run before anything is constructed. Every one of them aborts; none of
    /// them warns and continues.</summary>
    public static class BotSafety
    {
        public const string TargetAccount = "Sim101";

        /// <summary>Nothing runs until the account is PROVEN to be the simulator. Copied in shape from
        /// DeadmanGuardianSoak.VerifyAccount - the bots send orders that can fill, so if anything the
        /// bar is higher here, never lower.</summary>
        public static Account VerifyAccount(Action<string> note)
        {
            var all = new List<Account>();
            try { all = Account.All.ToList(); }
            catch (Exception ex) { note("ABORT: Account.All threw: " + ex.Message); return null; }

            note("Account.All = [" + string.Join(", ", all.Select(a => a.Name + "/" + SafeProvider(a))) + "]");

            var matches = all.Where(a => string.Equals(a.Name, TargetAccount, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1)
            {
                note("ABORT: expected exactly one '" + TargetAccount + "', found " + matches.Count);
                return null;
            }

            var account = matches[0];

            var provider = SafeProvider(account);
            if (provider != Provider.Simulator.ToString())
            {
                note("ABORT: Provider=" + provider + ", not Simulator");
                return null;
            }

            try
            {
                if (account.Connection == null || account.ConnectionStatus != ConnectionStatus.Connected)
                {
                    note("ABORT: account not connected (" + SafeConnection(account) + ")");
                    return null;
                }
            }
            catch (Exception ex) { note("ABORT: connection check threw: " + ex.Message); return null; }

            note("verified " + TargetAccount + " Provider=Simulator, Connected");
            return account;
        }

        /// <summary>An instrument the feed actually serves. Nothing is assumed to exist by name.</summary>
        public static Instrument ResolveInstrument(Action<string> note, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                Instrument found = null;
                try { found = Instrument.GetInstrument(candidate, false); } catch { found = null; }
                if (found == null) { note("instrument not resolved: " + candidate); continue; }

                decimal pointValue = 0m;
                try { pointValue = (decimal)found.MasterInstrument.PointValue; } catch { pointValue = 0m; }
                if (pointValue <= 0m)
                {
                    // Core turns a non-positive point value into INVALID_POINT_VALUE and blocks (G23).
                    // A bot that trades an instrument whose money value is unknown would be testing the
                    // unknown path by accident instead of the path it means to test.
                    note("instrument rejected (no usable point value): " + candidate);
                    continue;
                }

                note("instrument resolved: " + candidate + "  pointValue=" +
                     pointValue.ToString(CultureInfo.InvariantCulture) + "  tickSize=" +
                     SafeTickSize(found));
                return found;
            }
            note("ABORT: none of the candidate instruments resolved with a usable point value");
            return null;
        }

        public static string SafeProvider(Account a)
        {
            try { return a.Provider.ToString(); } catch { return "?"; }
        }

        private static string SafeConnection(Account a)
        {
            try { return a.ConnectionStatus.ToString(); } catch { return "?"; }
        }

        private static string SafeTickSize(Instrument i)
        {
            try { return i.MasterInstrument.TickSize.ToString(CultureInfo.InvariantCulture); } catch { return "?"; }
        }
    }

    /// <summary>Order and contract budget. Every cap refuses in code; none of them is a comment asking
    /// the bot to behave. The counters only ever go up - a cancelled order still spent its budget,
    /// because the budget exists to bound how much this process can ask of a venue, not how much it
    /// managed to keep.</summary>
    public sealed class SessionBudget
    {
        private readonly object _gate = new object();

        public int MaxOrdersPerSession { get; private set; }
        public int MaxContractsPerOrder { get; private set; }
        public int MaxNetContracts { get; private set; }

        public int OrdersPlaced { get; private set; }
        public int ContractsRequested { get; private set; }

        public SessionBudget(int maxOrdersPerSession, int maxContractsPerOrder, int maxNetContracts)
        {
            if (maxOrdersPerSession <= 0) throw new ArgumentOutOfRangeException("maxOrdersPerSession");
            if (maxContractsPerOrder <= 0) throw new ArgumentOutOfRangeException("maxContractsPerOrder");
            if (maxNetContracts <= 0) throw new ArgumentOutOfRangeException("maxNetContracts");

            MaxOrdersPerSession = maxOrdersPerSession;
            MaxContractsPerOrder = maxContractsPerOrder;
            MaxNetContracts = maxNetContracts;
        }

        /// <summary>Reserves one order. <paramref name="netAfter"/> is the absolute net position the
        /// bot would hold if this order filled in full; it is the caller's arithmetic because only the
        /// caller knows whether the order opens or closes.</summary>
        public bool TryReserveOrder(int quantity, int netAfter, out string denial)
        {
            lock (_gate)
            {
                if (quantity <= 0)
                { denial = "QTY_NOT_POSITIVE(" + quantity + ")"; return false; }

                if (quantity > MaxContractsPerOrder)
                { denial = "OVER_PER_ORDER_CAP(" + quantity + ">" + MaxContractsPerOrder + ")"; return false; }

                if (Math.Abs(netAfter) > MaxNetContracts)
                { denial = "OVER_NET_CAP(|" + netAfter + "|>" + MaxNetContracts + ")"; return false; }

                if (OrdersPlaced >= MaxOrdersPerSession)
                { denial = "SESSION_ORDER_BUDGET_SPENT(" + OrdersPlaced + "/" + MaxOrdersPerSession + ")"; return false; }

                OrdersPlaced++;
                ContractsRequested += quantity;
                denial = null;
                return true;
            }
        }

        public string Summary()
        {
            lock (_gate)
            {
                return OrdersPlaced + "/" + MaxOrdersPerSession + " orders, " +
                       ContractsRequested + " contracts requested, caps " +
                       MaxContractsPerOrder + "/order and " + MaxNetContracts + " net";
            }
        }
    }

    /// <summary>The gate files, and the rule that A and B never run at once.</summary>
    public static class BotGate
    {
        /// <summary>Why this is correctness and not tidiness: when Bot A's guardian locks out it calls
        /// NtBrokerActions.Flatten and CancelAllOrders, and BOTH of those are account-wide - they do
        /// not know which bot placed what (see nt\addon\GuardianPorts.cs). If B were trading at that
        /// moment, A's lockout would cancel B's orders and flatten B's position, and B's report would
        /// record an intervention that its own guardian never made. That is a manufactured false
        /// positive in the exact number B exists to measure. So: never together.</summary>
        public static bool OtherGateBlocks(string thisBot, Action<string> note)
        {
            var other = string.Equals(thisBot, "A", StringComparison.Ordinal) ? "B" : "A";
            var otherGate = BotPaths.Gate(other);
            if (!File.Exists(otherGate)) return false;

            note("ABORT: bot " + other + "'s gate is present (" + otherGate + "). A and B never run " +
                 "together - a lockout flatten is account-wide and would forge an intervention in the " +
                 "other bot's record.");
            return true;
        }

        /// <summary>Burned BEFORE anything is sent, so a crash or a restart cannot replay the run.
        /// Failure to delete is fatal: a gate we cannot burn is a run we cannot bound.</summary>
        public static bool Burn(string bot, Action<string> note)
        {
            var path = BotPaths.Gate(bot);
            try
            {
                File.Delete(path);
                note("gate burned before sending anything: " + path);
                return true;
            }
            catch (Exception ex)
            {
                note("ABORT: could not delete the gate file, refusing to send: " + ex.Message);
                return false;
            }
        }
    }

    /// <summary>A run log that is also the report. Lines are timestamped when they happen, not when
    /// they are written out, because a report assembled at the end from memory is a story.</summary>
    public sealed class BotLog
    {
        private readonly object _gate = new object();
        private readonly List<string> _lines = new List<string>();

        public void Note(string message)
        {
            lock (_gate)
            {
                _lines.Add(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + message);
            }
        }

        public IReadOnlyList<string> Lines
        {
            get { lock (_gate) { return _lines.ToList(); } }
        }

        /// <summary>Appends a dated section. Never rewrites what is already in the file: earlier runs,
        /// including the ones that failed, stay above the later ones (the soak report's rule).</summary>
        public void AppendSection(string reportPath, string title, IEnumerable<string> body)
        {
            var sb = new StringBuilder();
            if (!File.Exists(reportPath))
            {
                sb.AppendLine("# " + Path.GetFileNameWithoutExtension(reportPath));
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## " + title);
            sb.AppendLine();
            foreach (var line in body) sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("<details><summary>run log</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var line in Lines) sb.AppendLine(line);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                File.AppendAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { /* a report we cannot write is not a reason to leave orders alive */ }
        }
    }

    /// <summary>A guardian of the bot's own, over its own state file and its own ledger, with a small
    /// limit — and with the REAL ports underneath.
    ///
    /// This is the difference from nt\soak\SoakSandbox.cs and the reason these bots exist. The soak
    /// hands its guardian a synthetic feed and injects the fills, so what it proves is that Core's
    /// rules are right. Here the broker is NtBrokerActions, the feed is NtAccountFeed and the
    /// executions are NinjaTrader's own, translated by the same ExecutionTranslator the production
    /// adapter uses. Nothing about the money is synthetic, which means the §5.4 cross-check between
    /// Core's arithmetic and AccountItem.GrossRealizedProfitLoss is under test here for the first
    /// time — if the two disagree by more than the tolerance, the run will say PNL_DISAGREEMENT and
    /// that is a finding, not a nuisance.
    ///
    /// Production's state.json and ledger.jsonl are never opened for writing. The one thing this
    /// guardian DOES share with production is the account: a lockout here calls the real
    /// CancelAllOrders and the real Flatten, and both are account-wide. That is intended — it is what
    /// makes the evidence real — and it is exactly why BotGate.OtherGateBlocks exists.</summary>
    public sealed class BotSandboxGuardian
    {
        private const string Account = "Sim101";

        private readonly NtFileStore _store = new NtFileStore();
        private readonly Action<string> _note;
        private readonly Guardian _guardian;

        public string Dir { get; private set; }
        public string StatePath { get; private set; }
        public string LedgerPath { get; private set; }

        public BotSandboxGuardian(string bot, DateTime startedUtc, Action<string> note)
        {
            _note = note ?? (_ => { });
            Dir = BotPaths.RunDir(bot, startedUtc);
            Directory.CreateDirectory(Dir);
            StatePath = Path.Combine(Dir, "state.json");
            LedgerPath = Path.Combine(Dir, "ledger.jsonl");

            _guardian = new Guardian(new GuardianOptions
            {
                Clock = new NtClock(),
                Store = _store,
                Broker = new NtBrokerActions(_note),
                Feed = new NtAccountFeed(),
                StatePath = StatePath,
                LedgerPath = LedgerPath,
                RunId = "bot" + bot + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)
            });
            _guardian.Start();
            _note("sandbox guardian started at " + Dir + "; state=" + _guardian.Status.Kind);
        }

        public bool Arm(string personalLimit, decimal firmLimit)
        {
            var result = _guardian.Arm(ConfigText(personalLimit, firmLimit));
            _note("sandbox arm -> " + (result.Ok ? "ARMED" : result.ToString()));
            return result.Ok && _guardian.Status.Kind == StateKind.Armed;
        }

        private string ConfigText(string personalLimit, decimal firmLimit)
        {
            return "{" +
                   "\"schemaVersion\":1," +
                   "\"accounts\":[\"" + Account + "\"]," +
                   "\"currency\":\"UsDollar\"," +
                   "\"firmDailyLossLimit\":\"" + Money.Format(firmLimit) + "\"," +
                   "\"personalDailyLossLimit\":\"" + personalLimit + "\"," +
                   "\"sessionResetTimeZone\":\"America/Chicago\"," +
                   "\"sessionResetLocalTime\":\"17:00\"," +
                   "\"ledgerPath\":\"" + LedgerPath.Replace("\\", "\\\\") + "\"," +
                   "\"statePath\":\"" + StatePath.Replace("\\", "\\\\") + "\"," +
                   "\"pnlToleranceUsd\":\"5.00\"}";
        }

        public GuardianStatus Status { get { return _guardian.Status; } }
        public void OnExecution(ExecutionRecord record) { _guardian.OnExecution(record); }
        public void OnOrderObserved(OrderSnapshot order) { _guardian.OnOrderObserved(order); }
        public void Tick() { _guardian.Tick(); }
        public void Stop() { _guardian.Stop(); }

        /// <summary>The day loss AS THE GUARDIAN RECORDED IT — read back out of its own ledger, not
        /// recomputed here. A second opinion computed by the test is a second thing that can be
        /// wrong, and it would be the one nobody audits.</summary>
        public decimal DayLoss()
        {
            try
            {
                decimal last = 0m;
                foreach (var entry in new Ledger(_store, LedgerPath).ReadAll())
                {
                    var payload = entry["payload"] as JsonObject;
                    if (payload == null) continue;
                    var raw = payload.GetString("dayLoss");
                    decimal parsed;
                    if (raw != null && Money.TryParse(raw, out parsed)) last = parsed;
                }
                return last;
            }
            catch { return 0m; }
        }

        public IEnumerable<string> Events()
        {
            foreach (var entry in new Ledger(_store, LedgerPath).ReadAll())
            {
                var ev = entry.GetString("event");
                if (ev != null) yield return ev;
            }
        }

        public bool HasEvent(string name) { return Events().Contains(name); }

        public int CountEvent(string name) { return Events().Count(e => string.Equals(e, name, StringComparison.Ordinal)); }

        public string EventsSummary()
        {
            var all = Events().ToList();
            return all.Count + " (" + string.Join(", ", all.Distinct().Take(10)) + ")";
        }

        public string VerifyChain()
        {
            var r = new Ledger(_store, LedgerPath).Verify();
            return r.Ok ? "OK" : ("BROKEN@" + r.BrokenSeq);
        }
    }
}
