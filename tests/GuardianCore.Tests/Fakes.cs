using System;
using System.Collections.Generic;
using System.Linq;
using GuardianCore;

namespace GuardianCore.Tests
{
    /// <summary>Wall clock and monotonic counter move independently, because that is exactly the
    /// attack of SPEC 6.4 / 7.5.</summary>
    public sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; }
        public long MonotonicMs { get; set; }

        public FakeClock(DateTime startUtc, long monotonicMs = 1_000_000)
        {
            UtcNow = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
            MonotonicMs = monotonicMs;
        }

        /// <summary>Time passes honestly: both clocks advance together.</summary>
        public void Advance(TimeSpan by)
        {
            UtcNow = UtcNow.Add(by);
            MonotonicMs += (long)by.TotalMilliseconds;
        }

        /// <summary>The trader edits the system clock: wall moves, monotonic does not.</summary>
        public void SetWallClockOnly(DateTime utc) => UtcNow = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        /// <summary>Only real time passes (used to model sleep/hibernate the other way round).</summary>
        public void AdvanceMonotonicOnly(TimeSpan by) => MonotonicMs += (long)by.TotalMilliseconds;
    }

    /// <summary>In-memory store that can be made to fail, and that records the order of operations
    /// so G6 can assert "persist before acting".</summary>
    public sealed class FakeFileStore : IFileStore
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<string> Operations { get; } = new List<string>();
        public bool FailOnAppend { get; set; }
        public bool FailOnWrite { get; set; }

        public bool Exists(string path) => _files.ContainsKey(path);

        public string ReadAllText(string path) =>
            _files.TryGetValue(path, out var v) ? v : throw new InvalidOperationException("no such file: " + path);

        public void WriteAtomic(string path, string contents)
        {
            if (FailOnWrite) throw new InvalidOperationException("disk full (simulated)");
            Operations.Add("write:" + path);
            _files[path] = contents;
        }

        public void AppendLine(string path, string line)
        {
            if (FailOnAppend) throw new InvalidOperationException("disk full (simulated)");
            Operations.Add("append:" + path);
            _files[path] = _files.TryGetValue(path, out var existing) ? existing + line + "\n" : line + "\n";
        }

        public IEnumerable<string> ReadLines(string path) =>
            _files.TryGetValue(path, out var v)
                ? v.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l))
                : Enumerable.Empty<string>();

        // Test helpers - a hand editing the files is the whole point of G9, G10, G17, G19.
        public void PutRaw(string path, string contents) => _files[path] = contents;
        public string GetRaw(string path) => _files.TryGetValue(path, out var v) ? v : null;
        public void Delete(string path) => _files.Remove(path);
    }

    public sealed class FakeBroker : IBrokerActions
    {
        private readonly Dictionary<string, List<PositionSnapshot>> _positions =
            new Dictionary<string, List<PositionSnapshot>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<OrderSnapshot>> _orders =
            new Dictionary<string, List<OrderSnapshot>>(StringComparer.Ordinal);

        public List<string> Calls { get; } = new List<string>();
        /// <summary>Invoked on every broker call, so G6 can look at what was already on disk at the
        /// moment the first order left the building.</summary>
        public Action<string> Observer { get; set; }
        /// <summary>Number of Flatten calls that will throw before one succeeds (models a crash or a
        /// rejected flatten mid-sequence).</summary>
        public int FlattenFailures { get; set; }
        /// <summary>When true, Flatten is accepted but the position never goes flat (G: LOCKOUT_INCOMPLETE).</summary>
        public bool FlattenSilentlyDoesNothing { get; set; }

        public void SetPosition(string account, string instrument, int qty)
        {
            if (!_positions.TryGetValue(account, out var list)) _positions[account] = list = new List<PositionSnapshot>();
            list.RemoveAll(p => p.Instrument == instrument);
            if (qty != 0) list.Add(new PositionSnapshot(account, instrument, qty));
        }

        public void SetWorkingOrder(string account, string orderId, string instrument, string action)
        {
            if (!_orders.TryGetValue(account, out var list)) _orders[account] = list = new List<OrderSnapshot>();
            list.Add(new OrderSnapshot(account, orderId, instrument, action));
        }

        public void CancelAllOrders(string account)
        {
            Calls.Add("cancel:" + account);
            Observer?.Invoke("cancel:" + account);
            if (_orders.TryGetValue(account, out var list)) list.Clear();
        }

        public void Flatten(string account)
        {
            Calls.Add("flatten:" + account);
            Observer?.Invoke("flatten:" + account);
            if (FlattenFailures > 0) { FlattenFailures--; throw new InvalidOperationException("flatten failed (simulated)"); }
            if (FlattenSilentlyDoesNothing) return;
            if (_positions.TryGetValue(account, out var list)) list.Clear();
        }

        public IReadOnlyList<PositionSnapshot> GetPositions(string account) =>
            _positions.TryGetValue(account, out var list) ? list.ToList() : (IReadOnlyList<PositionSnapshot>)new List<PositionSnapshot>();

        public IReadOnlyList<OrderSnapshot> GetWorkingOrders(string account) =>
            _orders.TryGetValue(account, out var list) ? list.ToList() : (IReadOnlyList<OrderSnapshot>)new List<OrderSnapshot>();
    }

    public sealed class FakeAccountFeed : IAccountFeed
    {
        private readonly Dictionary<string, AccountState> _states = new Dictionary<string, AccountState>(StringComparer.Ordinal);
        private readonly Dictionary<string, PlatformPnl> _pnl = new Dictionary<string, PlatformPnl>(StringComparer.Ordinal);

        public FakeAccountFeed(params string[] accounts)
        {
            foreach (var a in accounts)
            {
                _states[a] = new AccountState(true, ConnectionState.Connected, "UsDollar");
                _pnl[a] = new PlatformPnl(0m, 0m);
            }
        }

        public IReadOnlyList<string> KnownAccounts => _states.Keys.ToList();

        public AccountState GetState(string account) =>
            _states.TryGetValue(account, out var s) ? s : AccountState.Missing();

        public PlatformPnl GetPlatformPnl(string account) =>
            _pnl.TryGetValue(account, out var p) ? p : PlatformPnl.Unknown();

        public void SetState(string account, AccountState state) => _states[account] = state;
        public void Remove(string account) { _states.Remove(account); _pnl.Remove(account); }
        public void SetPnl(string account, decimal? grossRealized, decimal? unrealized) =>
            _pnl[account] = new PlatformPnl(grossRealized, unrealized);
    }
}
