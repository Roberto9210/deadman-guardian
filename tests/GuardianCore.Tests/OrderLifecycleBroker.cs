// A broker double that MODELS THE ORDER LIFECYCLE, because the one that does not hid LT-1.
//
// FakeBroker.Flatten clears the position from a list. No order, no submission, no acceptance,
// nothing to cancel - flattening is ATOMIC in that world. The design assumed that atomicity without
// anyone writing the assumption down, and in production the guardian cancelled its own flatten order
// 110ms after submitting it, looped 167 times, and never once wrote FLATTEN_VERIFIED (2026-08-26).
//
// The 16 soak runs and 256 tests were green and STILL ARE. They test a world where flattening is
// instantaneous, and that world does not exist. A TEST DOUBLE THAT SIMPLIFIES REALITY DOES NOT TEST
// LESS - IT TESTS SOMETHING ELSE, AND THE GREEN SAYS YOU TESTED THE WRONG ONE.
//
// So here flattening EMITS AN ORDER, with an id, that the test can feed back through
// OnOrderObserved exactly as NinjaTrader's OrderUpdate does - and the position does not move until
// that order is filled. It is deliberately a separate type: FakeBroker stays as it is for the tests
// that are not about flatten mechanics, and this one is opted into by the tests that are.
//
// AND THIS DOUBLE WAS ITSELF CORRECTED BY PRODUCTION, NOT BY INTUITION. Its first version emitted one
// flatten order per Flatten() call, which stacked - and made LT1a fail for a reason that had nothing
// to do with the defect. The production log settled it: 167 FLATTEN_REQUESTED against 6 orders named
// "Cerrar", so NinjaTrader does not stack a second close while one is in flight. Without that
// correction the test was lying in the other direction.
//
// The rule that leaves: A DOUBLE IS CORRECTED AGAINST PRODUCTION, NOT AGAINST INTUITION. Every place
// it differs from the real thing is a place a defect can hide, so each difference is either evidence-
// backed or written down as a known simplification.

using System;
using System.Collections.Generic;
using System.Linq;
using GuardianCore;

namespace GuardianCore.Tests
{
    public sealed class OrderLifecycleBroker : IBrokerActions
    {
        private sealed class Working
        {
            public string Account, OrderId, Instrument, Action;
            public int SignedQty;          // what filling it does to the position
            public bool Ours;              // emitted by Flatten rather than by a trader
        }

        // account -> instrument -> signed quantity (positive long, negative short)
        private readonly Dictionary<string, Dictionary<string, int>> _positions =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        private readonly List<Working> _working = new List<Working>();
        private int _nextId;

        public List<string> Calls { get; } = new List<string>();

        /// <summary>How many times the guardian asked for a blind account-wide cancel. The whole
        /// point of LT-1c: it must be ONE however many times the lockout steps re-enter.</summary>
        public int CancelAllCalls { get; private set; }

        public void SetPosition(string account, string instrument, int signedQty)
        {
            if (!_positions.TryGetValue(account, out var byInstr))
                _positions[account] = byInstr = new Dictionary<string, int>(StringComparer.Ordinal);
            byInstr[instrument] = signedQty;
        }

        public int PositionOf(string account, string instrument)
        {
            return _positions.TryGetValue(account, out var byInstr) && byInstr.TryGetValue(instrument, out var q)
                ? q : 0;
        }

        /// <summary>An order the TRADER placed - a resting exit, say - so a test can prove the
        /// guardian does not kill it.</summary>
        public string PlaceTraderOrder(string account, string instrument, string action, int signedQty)
        {
            var id = "T" + (++_nextId);
            _working.Add(new Working
            {
                Account = account, OrderId = id, Instrument = instrument,
                Action = action, SignedQty = signedQty, Ours = false
            });
            return id;
        }

        public bool IsWorking(string orderId)
        {
            return _working.Any(w => string.Equals(w.OrderId, orderId, StringComparison.Ordinal));
        }

        /// <summary>The last order Flatten emitted, shaped exactly as the adapter shapes what it sees
        /// on OrderUpdate - which is how the guardian came to cancel its own.</summary>
        public OrderSnapshot LastFlattenOrder()
        {
            var w = _working.LastOrDefault(x => x.Ours);
            return w == null ? null : new OrderSnapshot(w.Account, w.OrderId, w.Instrument, w.Action);
        }

        public OrderSnapshot SnapshotOf(string orderId)
        {
            var w = _working.FirstOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.Ordinal));
            return w == null ? null : new OrderSnapshot(w.Account, w.OrderId, w.Instrument, w.Action);
        }

        /// <summary>The order reaches the venue and fills. Only now does the position move.</summary>
        public void Fill(string orderId)
        {
            var w = _working.FirstOrDefault(x => string.Equals(x.OrderId, orderId, StringComparison.Ordinal));
            if (w == null) throw new InvalidOperationException(
                "cannot fill '" + orderId + "': it is not working. Cancelled, perhaps - which is the defect.");
            _working.Remove(w);
            SetPosition(w.Account, w.Instrument, PositionOf(w.Account, w.Instrument) + w.SignedQty);
        }

        // ------------------------------------------------------------------ IBrokerActions

        public void CancelAllOrders(string account)
        {
            CancelAllCalls++;
            Calls.Add("cancelAll:" + account);
            _working.RemoveAll(w => string.Equals(w.Account, account, StringComparison.Ordinal));
        }

        /// <summary>Submits a closing order per non-flat instrument and returns. The position is
        /// UNCHANGED until that order fills - which is the entire difference from FakeBroker, and the
        /// entire reason LT-1 was invisible.</summary>
        public void Flatten(string account)
        {
            Calls.Add("flatten:" + account);
            if (!_positions.TryGetValue(account, out var byInstr)) return;
            foreach (var kv in byInstr.Where(k => k.Value != 0).ToList())
            {
                // No stacking: a closing order already in flight for this instrument is not doubled.
                // Modelled on the production evidence rather than guessed - on 2026-08-26 the guardian
                // logged 167 FLATTEN_REQUESTED and NinjaTrader emitted only 6 orders named "Cerrar".
                if (_working.Any(w => w.Ours
                                      && string.Equals(w.Account, account, StringComparison.Ordinal)
                                      && string.Equals(w.Instrument, kv.Key, StringComparison.Ordinal)))
                    continue;
                var id = "F" + (++_nextId);
                _working.Add(new Working
                {
                    Account = account, OrderId = id, Instrument = kv.Key,
                    Action = kv.Value > 0 ? "Sell" : "BuyToCover",
                    SignedQty = -kv.Value, Ours = true
                });
            }
        }

        public IReadOnlyList<PositionSnapshot> GetPositions(string account)
        {
            if (!_positions.TryGetValue(account, out var byInstr)) return new List<PositionSnapshot>();
            return byInstr.Where(k => k.Value != 0)
                          .Select(k => new PositionSnapshot(account, k.Key, k.Value))
                          .ToList();
        }

        public IReadOnlyList<OrderSnapshot> GetWorkingOrders(string account)
        {
            return _working.Where(w => string.Equals(w.Account, account, StringComparison.Ordinal))
                           .Select(w => new OrderSnapshot(w.Account, w.OrderId, w.Instrument, w.Action))
                           .ToList();
        }
    }
}
