// deadman-guardian — soak sandbox.
//
// Each scenario gets a Guardian of its own, with its own state file and its own ledger under
// deadman-guardian-soak\<scenario>\. The production guardian's files are never opened.
//
// Honesty about what is real in here, because the report has to say it:
//   * the STATE MACHINE, the SEAL, the LEDGER and the CLOCK RULES are the real GuardianCore.
//   * the FILE STORE is the real NtFileStore, so atomic writes and the append path are exercised.
//   * P&L is SYNTHETIC. Making a simulated account lose exactly $600 needs fillable orders, and
//     fillable orders are what this suite refuses to send. So the feed reports what the scenario
//     tells it and Core does the arithmetic - which tests the rules, not NinjaTrader's accounting.
//   * POSITIONS and CANCELS in the sandbox broker are synthetic too, except in the one scenario that
//     places a real resting order on Sim101 and hands it to the guardian.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using GuardianCore;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.DeadmanGuardian
{
    /// <summary>Real time, plus an offset the scenario can push. Pushing the WALL clock only is the
    /// attack of SPEC §6.4: the monotonic counter must not move with it.</summary>
    public sealed class SoakClock : IClock
    {
        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;
        private TimeSpan _wallPush = TimeSpan.Zero;

        public DateTime UtcNow => DateTime.UtcNow + _wallPush;
        public long MonotonicMs => (long)(Stopwatch.GetTimestamp() * MsPerTick);

        public void PushWallClockOnly(TimeSpan by) { _wallPush += by; }
    }

    /// <summary>Synthetic broker. Records what was asked of it, can refuse a flatten once, and can be
    /// given an open position to flatten.</summary>
    public sealed class SoakBroker : IBrokerActions
    {
        private readonly Dictionary<string, List<PositionSnapshot>> _positions =
            new Dictionary<string, List<PositionSnapshot>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<OrderSnapshot>> _orders =
            new Dictionary<string, List<OrderSnapshot>>(StringComparer.Ordinal);

        public List<string> Calls { get; } = new List<string>();
        public bool FailFlattenOnce { get; set; }

        public void OpenPosition(string account, string instrument, int qty)
        {
            if (!_positions.TryGetValue(account, out var l)) _positions[account] = l = new List<PositionSnapshot>();
            l.Add(new PositionSnapshot(account, instrument, qty));
        }

        public void AddWorkingOrder(string account, string id, string instrument, string action)
        {
            if (!_orders.TryGetValue(account, out var l)) _orders[account] = l = new List<OrderSnapshot>();
            l.Add(new OrderSnapshot(account, id, instrument, action));
        }

        public void CancelAllOrders(string account)
        {
            Calls.Add("cancel:" + account);
            if (_orders.TryGetValue(account, out var l)) l.Clear();
        }

        public void Flatten(string account)
        {
            Calls.Add("flatten:" + account);
            if (FailFlattenOnce) { FailFlattenOnce = false; throw new InvalidOperationException("flatten refused (soak)"); }
            if (_positions.TryGetValue(account, out var l)) l.Clear();
        }

        public IReadOnlyList<PositionSnapshot> GetPositions(string account) =>
            _positions.TryGetValue(account, out var l) ? l.ToList() : (IReadOnlyList<PositionSnapshot>)new List<PositionSnapshot>();

        public IReadOnlyList<OrderSnapshot> GetWorkingOrders(string account) =>
            _orders.TryGetValue(account, out var l) ? l.ToList() : (IReadOnlyList<OrderSnapshot>)new List<OrderSnapshot>();
    }

    /// <summary>Synthetic feed. The account is reported known, connected and in the configured currency;
    /// the P&amp;L is whatever the scenario set, and it agrees with what the scenario fed Core, so the
    /// cross-check of SPEC §5.4 passes and the scenario tests the rule it means to test.</summary>
    public sealed class SoakFeed : IAccountFeed
    {
        private readonly string _account;
        public decimal GrossRealized { get; set; }
        public decimal? Unrealized { get; set; } = 0m;

        public SoakFeed(string account) { _account = account; }

        public IReadOnlyList<string> KnownAccounts => new List<string> { _account };
        public AccountState GetState(string account) =>
            string.Equals(account, _account, StringComparison.Ordinal)
                ? new AccountState(true, ConnectionState.Connected, "UsDollar")
                : AccountState.Missing();
        public PlatformPnl GetPlatformPnl(string account) => new PlatformPnl(GrossRealized, Unrealized);
    }

    /// <summary>The REAL NinjaTrader broker, scoped to what this suite placed and nothing else.
    ///
    /// The first soak run failed the "order while locked is cancelled" scenario with
    /// logged=True, stillWorking=True: the sandbox had handed the guardian the synthetic broker, so
    /// the cancel never left the process. That made the scenario unable to prove the one thing it
    /// existed for. This class is the fix, and it is deliberately narrow:
    ///   * it cancels ONLY orders whose Name is the suite's own tag;
    ///   * it NEVER flattens. Positions on that account belong to the trader, not to a test.
    /// </summary>
    public sealed class ScopedNtBroker : IBrokerActions
    {
        public const string Tag = "deadman-soak";
        private readonly Action<string> _log;

        public ScopedNtBroker(Action<string> log) { _log = log ?? (_ => { }); }

        private static IEnumerable<Order> Ours(Account a) =>
            a.Orders.Where(o => string.Equals(o.Name, Tag, StringComparison.Ordinal) && Accounts.IsWorking(o.OrderState));

        public void CancelAllOrders(string account)
        {
            var a = Accounts.Find(account);
            if (a == null) return;
            var ours = Ours(a).ToList();
            if (ours.Count == 0) { _log("scoped cancel: nothing of ours working on " + account); return; }
            _log("scoped cancel: " + ours.Count + " order(s) tagged '" + Tag + "' on " + account);
            a.Cancel(ours);
        }

        public void Flatten(string account)
        {
            // Refused on purpose. A soak that can flatten a real account is a soak that can cost money.
            _log("scoped flatten: REFUSED on " + account + " - the soak never flattens a real account");
        }

        public IReadOnlyList<PositionSnapshot> GetPositions(string account) => new List<PositionSnapshot>();

        public IReadOnlyList<OrderSnapshot> GetWorkingOrders(string account)
        {
            var a = Accounts.Find(account);
            if (a == null) return new List<OrderSnapshot>();
            return Ours(a).Select(o => new OrderSnapshot(account, o.OrderId ?? o.Id.ToString(CultureInfo.InvariantCulture),
                                                         o.Instrument == null ? "?" : o.Instrument.FullName,
                                                         o.OrderAction.ToString())).ToList();
        }
    }

    /// <summary>One scenario's world: its own directory, its own Guardian, its own ledger.</summary>
    public sealed class Sandbox
    {
        private const string Account = "Sim101";
        private const decimal PointValue = 5.00m;

        private readonly string _dir;
        private readonly Action<string> _note;
        private readonly NtFileStore _store = new NtFileStore();
        private int _fill;

        public string Name { get; }
        public string StatePath { get; }
        public string LedgerPath { get; }
        public SoakClock Clock { get; } = new SoakClock();
        public SoakBroker Broker { get; } = new SoakBroker();
        private readonly IBrokerActions _brokerForGuardian;
        public SoakFeed Feed { get; } = new SoakFeed(Account);
        public Guardian Guardian { get; private set; }

        /// <summary>When <paramref name="brokerOverride"/> is supplied the guardian talks to it instead
        /// of the synthetic broker. The locked-order scenario passes the scoped REAL broker, because a
        /// cancel that never leaves the process proves nothing.</summary>
        public Sandbox(string name, string root, Action<string> note, IBrokerActions brokerOverride = null)
        {
            Name = name;
            _note = note ?? (_ => { });
            _brokerForGuardian = brokerOverride;
            _dir = Path.Combine(root, "sandbox", name + "-" + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(_dir);
            StatePath = Path.Combine(_dir, "state.json");
            LedgerPath = Path.Combine(_dir, "ledger.jsonl");
            Guardian = Build("run-1");
        }

        private Guardian Build(string runId)
        {
            var g = new Guardian(new GuardianOptions
            {
                Clock = Clock,
                Store = _store,
                Broker = _brokerForGuardian ?? Broker,
                Feed = Feed,
                StatePath = StatePath,
                LedgerPath = LedgerPath,
                RunId = runId
            });
            g.Start();
            return g;
        }

        public string ConfigText(string personal, decimal firm)
        {
            return "{" +
                   "\"schemaVersion\":1," +
                   "\"accounts\":[\"" + Account + "\"]," +
                   "\"currency\":\"UsDollar\"," +
                   "\"firmDailyLossLimit\":\"" + Money.Format(firm) + "\"," +
                   "\"personalDailyLossLimit\":\"" + personal + "\"," +
                   "\"sessionResetTimeZone\":\"America/Chicago\"," +
                   "\"sessionResetLocalTime\":\"17:00\"," +
                   "\"ledgerPath\":\"" + LedgerPath.Replace("\\", "\\\\") + "\"," +
                   "\"statePath\":\"" + StatePath.Replace("\\", "\\\\") + "\"," +
                   "\"pnlToleranceUsd\":\"5.00\"}";
        }

        public void Arm()
        {
            var result = Guardian.Arm(ConfigText("600.00", 1000.00m));
            _note("[" + Name + "] arm -> " + (result.Ok ? "ARMED" : result.ToString()));
        }

        /// <summary>Feeds a synthetic round trip that loses exactly <paramref name="dollars"/>, with the
        /// feed agreeing so the cross-check passes.</summary>
        public void Lose(decimal dollars)
        {
            _fill++;
            var points = dollars / PointValue;
            var entry = 5000.00m;
            var exit = entry - points;
            var now = Clock.UtcNow;

            Feed.GrossRealized = 0m; Feed.Unrealized = 0m;
            Guardian.OnExecution(new ExecutionRecord(Account, "SOAK 09-26", now, entry, 1, Side.Long, 0m, PointValue, "in" + _fill));
            Feed.GrossRealized = -dollars;
            Guardian.OnExecution(new ExecutionRecord(Account, "SOAK 09-26", now, exit, 1, Side.Short, 0m, PointValue, "out" + _fill));
        }

        /// <summary>A brand-new process, over the same files, with no monotonic continuity.</summary>
        public Guardian Restart()
        {
            Guardian = Build("run-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            _note("[" + Name + "] restarted -> " + Guardian.Status.Kind);
            return Guardian;
        }

        public IEnumerable<string> Events()
        {
            foreach (var o in new Ledger(_store, LedgerPath).ReadAll())
            {
                var ev = o.GetString("event");
                if (ev != null) yield return ev;
            }
        }

        public bool HasEvent(string name) => Events().Contains(name);

        public string EventsSummary()
        {
            var all = Events().ToList();
            return all.Count + " (" + string.Join(", ", all.Distinct().Take(8)) + ")";
        }

        public string VerifyChain()
        {
            var r = new Ledger(_store, LedgerPath).Verify();
            return r.Ok ? "OK" : ("BROKEN@" + r.BrokenSeq);
        }
    }
}
