// deadman-guardian — NtAdapter: the four ports of SPEC §14, and nothing else.
//
// THE RULE (SPEC §3.2): no decision may live in this file. No thresholds, no comparison against a
// limit, no "if" about money or state. Every conditional here is about translation or about a value
// being absent - and an absent value is reported as absent, never substituted.
//
// Sim101 only in v1: the guarded account comes from config, and config.json ships with Sim101.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using GuardianCore;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.DeadmanGuardian
{
    /// <summary>SPEC §6.4. Wall clock for timestamps, monotonic for anything the trader must not be
    /// able to set.
    ///
    /// Environment.TickCount64 does NOT exist on .NET Framework 4.8 - verified twice, once on the
    /// bench and once inside the NinjaTrader process itself (see nt/STEP3_FINDINGS.md §2). Stopwatch
    /// is high-resolution here (frequency 10,000,000) and is the source used.</summary>
    public sealed class NtClock : IClock
    {
        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;
        public DateTime UtcNow => DateTime.UtcNow;
        public long MonotonicMs => (long)(Stopwatch.GetTimestamp() * MsPerTick);
    }

    /// <summary>SPEC §6.1: atomic writes, so a torn state file is impossible. An unreadable one is
    /// handled upstream by failing closed.</summary>
    public sealed class NtFileStore : IFileStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public bool Exists(string path) => File.Exists(path);

        public string ReadAllText(string path) => File.ReadAllText(path, Utf8NoBom);

        public IEnumerable<string> ReadLines(string path)
        {
            if (!File.Exists(path)) yield break;
            using (var reader = new StreamReader(path, Utf8NoBom))
            {
                string line;
                while ((line = reader.ReadLine()) != null) yield return line;
            }
        }

        public void WriteAtomic(string path, string contents)
        {
            EnsureDirectory(path);
            var temp = path + ".tmp";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, Utf8NoBom))
            {
                writer.Write(contents);
                writer.Flush();
                fs.Flush(true);            // to disk, not just to the OS cache
            }

            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public void AppendLine(string path, string line)
        {
            EnsureDirectory(path);
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(fs, Utf8NoBom))
            {
                writer.Write(line);
                writer.Write(Environment.NewLine);
                writer.Flush();
                fs.Flush(true);            // SPEC §6: the ledger line is on disk before the act it describes
            }
        }

        private static void EnsureDirectory(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }

    /// <summary>Shared account lookup. NT8 keeps the same Account instance across a connection being
    /// established - verified in-process: the instance obtained at Configure went on receiving
    /// AccountItemUpdate after Cbi.Connection.CreateAccount fired - but the lookup is done by name on
    /// every call anyway, so a replaced instance cannot leave the guardian talking to a corpse.</summary>
    internal static class Accounts
    {
        public static Account Find(string name)
        {
            try
            {
                return Account.All.FirstOrDefault(a =>
                    string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        /// <summary>The order states in which an order is still live at the venue and can still fill.
        /// `AcceptedByRisk` is included: SPEC §3.4 established that risk acceptance happens at the
        /// venue, so an order in that state is on its way, not parked.</summary>
        public static bool IsWorking(OrderState state)
        {
            switch (state)
            {
                case OrderState.Accepted:
                case OrderState.AcceptedByRisk:
                case OrderState.ChangePending:
                case OrderState.ChangeSubmitted:
                case OrderState.PartFilled:
                case OrderState.Submitted:
                case OrderState.TriggerPending:
                case OrderState.Working:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>SPEC §14. The only orders this add-on ever sends are cancels and flattens (§13).</summary>
    public sealed class NtBrokerActions : IBrokerActions
    {
        private readonly Action<string> _log;
        public NtBrokerActions(Action<string> log) { _log = log ?? (_ => { }); }

        public void CancelAllOrders(string account)
        {
            var a = Accounts.Find(account);
            if (a == null) throw new InvalidOperationException("account '" + account + "' not found");

            var working = a.Orders.Where(o => Accounts.IsWorking(o.OrderState)).ToList();
            if (working.Count == 0) return;
            _log("cancel " + working.Count + " order(s) on " + account);
            a.Cancel(working);
        }

        public void Flatten(string account)
        {
            var a = Accounts.Find(account);
            if (a == null) throw new InvalidOperationException("account '" + account + "' not found");

            var instruments = a.Positions
                .Where(p => p.Quantity != 0 && p.MarketPosition != MarketPosition.Flat)
                .Select(p => p.Instrument)
                .Distinct()
                .ToList();
            if (instruments.Count == 0) return;
            _log("flatten " + instruments.Count + " instrument(s) on " + account);
            a.Flatten(instruments);
        }

        public IReadOnlyList<PositionSnapshot> GetPositions(string account)
        {
            var a = Accounts.Find(account);
            if (a == null) return new List<PositionSnapshot>();

            return a.Positions
                .Where(p => p.MarketPosition != MarketPosition.Flat && p.Quantity != 0)
                .Select(p => new PositionSnapshot(
                    account,
                    p.Instrument == null ? "?" : p.Instrument.FullName,
                    p.MarketPosition == MarketPosition.Short ? -p.Quantity : p.Quantity))
                .ToList();
        }

        public IReadOnlyList<OrderSnapshot> GetWorkingOrders(string account)
        {
            var a = Accounts.Find(account);
            if (a == null) return new List<OrderSnapshot>();

            return a.Orders
                .Where(o => Accounts.IsWorking(o.OrderState))
                .Select(o => new OrderSnapshot(
                    account,
                    o.OrderId ?? o.Id.ToString(),
                    o.Instrument == null ? "?" : o.Instrument.FullName,
                    o.OrderAction.ToString()))
                .ToList();
        }
    }

    /// <summary>SPEC §14 and §5.3. Every number crosses the boundary as decimal, and an unavailable
    /// number crosses as null - never as zero (§5.5, G15, G23).</summary>
    public sealed class NtAccountFeed : IAccountFeed
    {
        public IReadOnlyList<string> KnownAccounts
        {
            get
            {
                try { return Account.All.Select(a => a.Name).ToList(); }
                catch { return new List<string>(); }
            }
        }

        public AccountState GetState(string account)
        {
            var a = Accounts.Find(account);
            if (a == null) return AccountState.Missing();

            // Verified in-process: at AddOn Configure the account exists and is denominated, but
            // Connection is still null - the connection arrives seconds later. That is "present, not
            // yet connected", which is Disconnected, not Unknown (nt/STEP3_FINDINGS.md §3).
            var connection = ConnectionState.Unknown;
            try
            {
                if (a.Connection == null) connection = ConnectionState.Disconnected;
                else if (a.ConnectionStatus == ConnectionStatus.Connected) connection = ConnectionState.Connected;
                else connection = ConnectionState.Disconnected;
            }
            catch { connection = ConnectionState.Unknown; }

            string denomination;
            try { denomination = a.Denomination.ToString(); } catch { denomination = null; }

            return new AccountState(true, connection, denomination);
        }

        public PlatformPnl GetPlatformPnl(string account)
        {
            var a = Accounts.Find(account);
            if (a == null) return PlatformPnl.Unknown();

            var gross = TryGet(a, AccountItem.GrossRealizedProfitLoss);
            var unrealized = TryGet(a, AccountItem.UnrealizedProfitLoss);
            return new PlatformPnl(gross, unrealized);
        }

        /// <summary>NT8 hands these back as double. The conversion to decimal happens here, at the
        /// boundary, so that no double ever enters Core (SPEC §4 rule 7, G21). A value the platform
        /// cannot produce comes back null.</summary>
        private static decimal? TryGet(Account a, AccountItem item)
        {
            try
            {
                var value = a.Get(item, a.Denomination);
                if (double.IsNaN(value) || double.IsInfinity(value)) return null;
                return Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
            }
            catch { return null; }
        }
    }

    /// <summary>Translates one NT8 Execution into the record Core accounts with. The point value comes
    /// from platform metadata and is never typed by anyone (SPEC §5.7, G23); when the metadata is not
    /// usable it is passed through as zero, which Core turns into INVALID_POINT_VALUE and a blocked
    /// entry rather than a plausible substitute.</summary>
    public static class ExecutionTranslator
    {
        public static ExecutionRecord Translate(Execution e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            decimal pointValue = 0m;
            try
            {
                var master = e.Instrument?.MasterInstrument;
                if (master != null) pointValue = (decimal)master.PointValue;
            }
            catch { pointValue = 0m; }

            decimal commission = 0m;
            try { commission = Math.Round((decimal)e.Commission, 2, MidpointRounding.AwayFromZero); }
            catch { commission = 0m; }

            var side = e.MarketPosition == MarketPosition.Short ? Side.Short : Side.Long;

            return new ExecutionRecord(
                account: e.Account == null ? "?" : e.Account.Name,
                instrument: e.Instrument == null ? "?" : e.Instrument.FullName,
                timeUtc: e.Time.ToUniversalTime(),
                price: (decimal)e.Price,
                quantity: e.Quantity,
                side: side,
                commission: commission,
                pointValue: pointValue,
                executionId: e.ExecutionId);
        }
    }
}
