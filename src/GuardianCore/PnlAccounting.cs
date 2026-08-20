using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GuardianCore
{
    /// <summary>Why a P&amp;L figure could not be produced. Never a number: SPEC 5.5 forbids treating
    /// "unknown" as zero.</summary>
    public enum PnlStatus
    {
        Ok,
        NoPriceForOpenPosition,   // SPEC 5.5
        SourcesDisagree,          // SPEC 5.4
        AccountUnknown,           // SPEC 10
        InvalidPointValue         // amendment A3: Core cannot turn ticks into money without it
    }

    public sealed class AccountPnl
    {
        public string Account { get; }
        public PnlStatus Status { get; }
        public decimal GrossRealized { get; }
        public decimal Commissions { get; }
        public decimal Unrealized { get; }
        public decimal? PlatformGrossRealized { get; }
        public string Detail { get; }

        public decimal DayPnl => GrossRealized + Unrealized - Commissions;
        public decimal DayLoss => DayPnl < 0m ? -DayPnl : 0m;
        public bool Ok => Status == PnlStatus.Ok;

        public AccountPnl(string account, PnlStatus status, decimal grossRealized, decimal commissions,
                          decimal unrealized, decimal? platformGrossRealized, string detail)
        {
            Account = account; Status = status; GrossRealized = grossRealized; Commissions = commissions;
            Unrealized = unrealized; PlatformGrossRealized = platformGrossRealized; Detail = detail;
        }
    }

    public sealed class DayPnlSnapshot
    {
        public IReadOnlyList<AccountPnl> Accounts { get; }
        public bool Ok { get; }
        public AccountPnl FirstProblem { get; }

        /// <summary>SPEC 5.2: losses are summed across accounts, never netted against another
        /// account's profit - a firm fails each account on its own number.</summary>
        public decimal TotalDayLoss { get; }

        public DayPnlSnapshot(IReadOnlyList<AccountPnl> accounts)
        {
            Accounts = accounts;
            FirstProblem = accounts.FirstOrDefault(a => !a.Ok);
            Ok = FirstProblem == null;
            TotalDayLoss = accounts.Where(a => a.Ok).Sum(a => a.DayLoss);
        }
    }

    /// <summary>
    /// SPEC section 5. Executions are the primary source; the platform's own figures are a
    /// cross-check, never a tie-break (5.4).
    ///
    /// Realized P&amp;L uses average-cost position tracking per (account, instrument): a fill that
    /// reduces an open position realizes (exit - averageEntry) * closedQty * pointValue, signed by the
    /// direction of the position being closed. Commissions accumulate separately and are always
    /// subtracted, so a strategy can never look profitable because its costs were forgotten.
    /// </summary>
    public sealed class PnlBook
    {
        private sealed class Pos
        {
            public int Qty;              // signed: + long, - short
            public decimal AvgPrice;
            public decimal PointValue;
        }

        private readonly Dictionary<string, Dictionary<string, Pos>> _positions =
            new Dictionary<string, Dictionary<string, Pos>>(StringComparer.Ordinal);
        private readonly Dictionary<string, decimal> _grossRealized = new Dictionary<string, decimal>(StringComparer.Ordinal);
        private readonly Dictionary<string, decimal> _commissions = new Dictionary<string, decimal>(StringComparer.Ordinal);
        private readonly HashSet<string> _badPointValue = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _seenExecutionIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Start of a new trading day: everything resets (SPEC 5.1).</summary>
        public void ResetDay()
        {
            _positions.Clear();
            _grossRealized.Clear();
            _commissions.Clear();
            _badPointValue.Clear();
            _seenExecutionIds.Clear();
        }

        public decimal GrossRealized(string account) => _grossRealized.TryGetValue(account, out var v) ? v : 0m;
        public decimal Commissions(string account) => _commissions.TryGetValue(account, out var v) ? v : 0m;

        public bool HasOpenPosition(string account) =>
            _positions.TryGetValue(account, out var byInstrument) && byInstrument.Values.Any(p => p.Qty != 0);

        public int NetQuantity(string account, string instrument) =>
            _positions.TryGetValue(account, out var byInstrument) && byInstrument.TryGetValue(instrument, out var p) ? p.Qty : 0;

        /// <summary>Applies one fill. Returns false when the record cannot be accounted for, which is an
        /// unknown and never a zero.</summary>
        public bool Apply(ExecutionRecord ex, out string problem)
        {
            problem = null;
            if (ex == null) { problem = "null execution"; return false; }
            if (ex.Quantity <= 0) { problem = "execution quantity must be positive"; return false; }
            if (ex.PointValue <= 0m)
            {
                _badPointValue.Add(ex.Account);
                problem = "instrument '" + ex.Instrument + "' has no usable point value";
                return false;
            }
            // Duplicate fills would double-count a loss; NT8 can raise the same execution more than once.
            if (ex.ExecutionId != null && !_seenExecutionIds.Add(ex.Account + "|" + ex.ExecutionId)) return true;

            if (!_positions.TryGetValue(ex.Account, out var byInstrument))
                _positions[ex.Account] = byInstrument = new Dictionary<string, Pos>(StringComparer.Ordinal);
            if (!byInstrument.TryGetValue(ex.Instrument, out var pos))
                byInstrument[ex.Instrument] = pos = new Pos { Qty = 0, AvgPrice = 0m, PointValue = ex.PointValue };
            pos.PointValue = ex.PointValue;

            _commissions[ex.Account] = Commissions(ex.Account) + ex.Commission;

            int signed = ex.Side == Side.Long ? ex.Quantity : -ex.Quantity;

            if (pos.Qty == 0)
            {
                pos.Qty = signed;
                pos.AvgPrice = ex.Price;
                return true;
            }

            bool sameDirection = Math.Sign(pos.Qty) == Math.Sign(signed);
            if (sameDirection)
            {
                // Adding: weighted average entry.
                var total = pos.AvgPrice * Math.Abs(pos.Qty) + ex.Price * Math.Abs(signed);
                pos.Qty += signed;
                pos.AvgPrice = total / Math.Abs(pos.Qty);
                return true;
            }

            // Reducing or reversing.
            int closing = Math.Min(Math.Abs(pos.Qty), Math.Abs(signed));
            int directionOfClosedPosition = Math.Sign(pos.Qty);
            var realized = (ex.Price - pos.AvgPrice) * closing * ex.PointValue * directionOfClosedPosition;
            _grossRealized[ex.Account] = GrossRealized(ex.Account) + realized;

            int remainder = Math.Abs(signed) - closing;
            pos.Qty += signed;
            if (pos.Qty == 0) pos.AvgPrice = 0m;
            else if (remainder > 0) pos.AvgPrice = ex.Price;   // reversed into a new position
            return true;
        }

        /// <summary>Builds the day's picture, applying the cross-check and the unknowns of SPEC 5.4/5.5.</summary>
        public DayPnlSnapshot Snapshot(IEnumerable<string> accounts, IAccountFeed feed, decimal tolerance)
        {
            var list = new List<AccountPnl>();
            foreach (var account in accounts)
            {
                var state = feed.GetState(account);
                if (state == null || !state.Known || state.Connection != ConnectionState.Connected)
                {
                    list.Add(new AccountPnl(account, PnlStatus.AccountUnknown, 0m, 0m, 0m, null,
                        state == null || !state.Known ? "account is not known to the platform" : "account is " + state.Connection));
                    continue;
                }
                if (_badPointValue.Contains(account))
                {
                    list.Add(new AccountPnl(account, PnlStatus.InvalidPointValue, 0m, 0m, 0m, null,
                        "an execution arrived without a usable point value"));
                    continue;
                }

                var gross = GrossRealized(account);
                var commissions = Commissions(account);
                var platform = feed.GetPlatformPnl(account) ?? PlatformPnl.Unknown();

                // SPEC 5.5: an open position with no price has no computable unrealized P&L.
                decimal unrealized;
                if (HasOpenPosition(account))
                {
                    if (!platform.Unrealized.HasValue)
                    {
                        list.Add(new AccountPnl(account, PnlStatus.NoPriceForOpenPosition, gross, commissions, 0m,
                            platform.GrossRealized, "open position with no current price"));
                        continue;
                    }
                    unrealized = platform.Unrealized.Value;
                }
                else unrealized = 0m;

                // SPEC 5.4: disagreement is an unknown, not a tie-break. No averaging, no friendlier number.
                if (platform.GrossRealized.HasValue)
                {
                    var delta = Math.Abs(gross - platform.GrossRealized.Value);
                    if (delta > tolerance)
                    {
                        list.Add(new AccountPnl(account, PnlStatus.SourcesDisagree, gross, commissions, unrealized,
                            platform.GrossRealized,
                            "core " + Money.Format(gross) + " vs platform " + Money.Format(platform.GrossRealized.Value) +
                            " differ by " + Money.Format(delta) + ", tolerance " + Money.Format(tolerance)));
                        continue;
                    }
                }
                else if (gross != 0m)
                {
                    list.Add(new AccountPnl(account, PnlStatus.SourcesDisagree, gross, commissions, unrealized, null,
                        "core has realized P&L but the platform reports none"));
                    continue;
                }

                list.Add(new AccountPnl(account, PnlStatus.Ok, gross, commissions, unrealized, platform.GrossRealized, null));
            }
            return new DayPnlSnapshot(list);
        }
    }
}
