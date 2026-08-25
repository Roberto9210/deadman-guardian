using System;
using System.Collections.Generic;

namespace GuardianCore
{
    /// <summary>SPEC section 14. Wall clock for timestamps and the session boundary; monotonic counter
    /// for anything the trader must not be able to move (SPEC 6.4, 7.5).
    ///
    /// Adapter note, verified rather than assumed: Environment.TickCount64 does NOT exist on
    /// .NET Framework 4.8 (the NT8 runtime) - only TickCount:Int32, which wraps every 24.9 days.
    /// The adapter backs MonotonicMs with Stopwatch.GetTimestamp() scaled by Frequency.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
        long MonotonicMs { get; }
    }

    /// <summary>SPEC section 14 plus ReadLines, which the ledger verification of SPEC 11.3 needs and
    /// the spec did not list. Noted for amendment (AMENDMENTS.md A1).</summary>
    public interface IFileStore
    {
        bool Exists(string path);
        string ReadAllText(string path);
        void WriteAtomic(string path, string contents);
        void AppendLine(string path, string line);
        IEnumerable<string> ReadLines(string path);
    }

    public enum Side { Long, Short }

    public sealed class PositionSnapshot
    {
        public string Account { get; }
        public string Instrument { get; }
        public int Quantity { get; }      // signed: positive long, negative short, 0 flat

        /// <summary>The position's average entry price, when the platform can report one. Added for
        /// the restart baseline (Option A): an adopted position whose entry price is unknown makes
        /// every later closing fill's realised P&amp;L uncomputable, so adoption REFUSES rather than
        /// guessing. Null means "the platform could not say", never zero.</summary>
        public decimal? AveragePrice { get; }

        // TWO constructors, not one with an optional parameter, and the difference is binary
        // compatibility: optional parameters are compile-time sugar, so replacing the 3-argument
        // constructor would break every already-compiled caller (NinjaTrader.Custom.dll compiled
        // against the previous GuardianCore) with MissingMethodException in the window between
        // deploying the new DLL and the F5 that recompiles the adapter.
        public PositionSnapshot(string account, string instrument, int quantity)
            : this(account, instrument, quantity, null) { }

        public PositionSnapshot(string account, string instrument, int quantity, decimal? averagePrice)
        {
            Account = account; Instrument = instrument; Quantity = quantity; AveragePrice = averagePrice;
        }
    }

    public sealed class OrderSnapshot
    {
        public string Account { get; }
        public string OrderId { get; }
        public string Instrument { get; }
        public string Action { get; }
        public OrderSnapshot(string account, string orderId, string instrument, string action)
        {
            Account = account; OrderId = orderId; Instrument = instrument; Action = action;
        }
    }

    /// <summary>SPEC section 14. Only ever asked to cancel and flatten - never to open (SPEC 13).</summary>
    public interface IBrokerActions
    {
        void CancelAllOrders(string account);
        void Flatten(string account);
        IReadOnlyList<PositionSnapshot> GetPositions(string account);
        IReadOnlyList<OrderSnapshot> GetWorkingOrders(string account);
    }

    public enum ConnectionState { Connected, Disconnected, Unknown }

    public sealed class AccountState
    {
        public bool Known { get; }
        public ConnectionState Connection { get; }
        public string Denomination { get; }
        public AccountState(bool known, ConnectionState connection, string denomination)
        {
            Known = known; Connection = connection; Denomination = denomination;
        }
        public static AccountState Missing() => new AccountState(false, ConnectionState.Unknown, null);
    }

    /// <summary>The platform's own P&amp;L figures, used as the cross-check of SPEC 5.4.
    /// Null means "not computable" - never zero (SPEC 5.5).
    ///
    /// GrossRealized is realized excluding commissions: the adapter maps it from
    /// AccountItem.GrossRealizedProfitLoss so that it compares like-for-like with Core's own
    /// execution accounting, which tracks commissions separately. Noted for amendment (A2).</summary>
    public sealed class PlatformPnl
    {
        public decimal? GrossRealized { get; }
        public decimal? Unrealized { get; }
        public PlatformPnl(decimal? grossRealized, decimal? unrealized)
        {
            GrossRealized = grossRealized; Unrealized = unrealized;
        }
        public static PlatformPnl Unknown() => new PlatformPnl(null, null);
    }

    public interface IAccountFeed
    {
        IReadOnlyList<string> KnownAccounts { get; }
        AccountState GetState(string account);
        PlatformPnl GetPlatformPnl(string account);
    }

    /// <summary>One fill. Money arrives as decimal: the adapter converts NT8's double Commission at the
    /// boundary so that no double ever enters Core (SPEC 4 rule 7, G21).
    ///
    /// PointValue is supplied by the adapter (NT8: Instrument.MasterInstrument.PointValue) because Core
    /// cannot turn a price difference into money without it, and the spec did not say where it comes
    /// from. Missing or non-positive PointValue is an unknown and fails closed. Noted for amendment (A3).</summary>
    public sealed class ExecutionRecord
    {
        public string Account { get; }
        public string Instrument { get; }
        public DateTime TimeUtc { get; }
        public decimal Price { get; }
        public int Quantity { get; }
        public Side Side { get; }
        public decimal Commission { get; }
        public decimal PointValue { get; }
        public string ExecutionId { get; }

        public ExecutionRecord(string account, string instrument, DateTime timeUtc, decimal price,
                               int quantity, Side side, decimal commission, decimal pointValue, string executionId)
        {
            Account = account; Instrument = instrument; TimeUtc = timeUtc; Price = price;
            Quantity = quantity; Side = side; Commission = commission; PointValue = pointValue;
            ExecutionId = executionId;
        }
    }
}
