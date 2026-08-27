// LT-1: the guardian cancelled its own flatten orders, and the trader's exits.
//
// Found by the live test of 2026-08-26, not by any test here - because every test here ran against a
// broker double where flattening is atomic. In production the flatten order was submitted at
// 18:44:27.407, accepted at .516, and cancel-requested at .517: ONE MILLISECOND after acceptance,
// by the guardian itself, which had observed its own order and called CancelAllOrders. 167 loops,
// FLATTEN_VERIFIED zero, and twelve ORDER_REJECTED_LOCKED that were Sell, SellShort and BuyToCover -
// the trader's own exits, cancelled.
//
// THE DOCTRINE THESE TESTS ENFORCE, and the second half is the one whose absence caused LT-1:
//
//     on WORDS   every message asserts exactly what its own code established
//     on ACTS    the guardian never acts on the account on a premise it could not verify
//
// Cancelling is ACTING on the account, not refusing to act, so the fail-closed instinct does not
// apply to it. The worst cases are not symmetric and that is the whole argument:
//
//     cancelling wrongly      the trader cannot exit a sinking position. Loss UNBOUNDED, and caused
//                             by the guardian.
//     not cancelling wrongly  one order opens exposure during the lockout; the next cycle's flatten
//                             closes it. Loss BOUNDED by one cycle.
//
// These run against OrderLifecycleBroker, where flattening emits an order that the test feeds back
// through OnOrderObserved exactly as NinjaTrader's OrderUpdate does. Against FakeBroker they would
// all pass today, which is the point.

using System;
using System.Linq;
using GuardianCore;

namespace GuardianCore.Tests
{
    public class LT1_CancelScopeTests
    {
        private static Harness Breached(out OrderLifecycleBroker broker, int position = 1)
        {
            var h = new Harness();
            broker = new OrderLifecycleBroker();
            h.BrokerOverride = broker;
            h.Armed("600.00");
            broker.SetPosition(Harness.Account, Harness.Instrument, position);
            h.LoseExactly(600.00m);
            h.Guardian.Tick();                       // breach -> EnterLockout -> sweep, then flatten
            return h;
        }

        /// <summary>THE ONE THAT MATTERS. The platform echoes the guardian's own flatten order back to
        /// it, which is all NinjaTrader did on 2026-08-26. The order must survive that, fill, and the
        /// lockout must COMPLETE - one attempt, verified. In production it died in 110ms and the
        /// guardian span 167 times.</summary>
        [Fact]
        public void LT1a_The_guardians_own_flatten_order_survives_being_observed_and_the_lockout_completes()
        {
            OrderLifecycleBroker broker;
            var h = Breached(out broker);

            var flattenOrder = broker.LastFlattenOrder();
            Assert.NotNull(flattenOrder);            // the flatten did submit something

            // NinjaTrader reports the order back through OrderUpdate. This is the exact moment.
            h.Guardian.OnOrderObserved(flattenOrder);

            Assert.True(broker.IsWorking(flattenOrder.OrderId),
                        "the guardian cancelled its own flatten order - LT-1");

            broker.Fill(flattenOrder.OrderId);       // it reaches the venue and fills
            h.Guardian.Tick();

            Assert.Equal(0, broker.PositionOf(Harness.Account, Harness.Instrument));
            Assert.Contains(Ev.FlattenVerified, h.Events());
        }

        /// <summary>The trader is long and sends a Sell to get out. That order REDUCES exposure, and
        /// the guardian must not touch it. Cancelling it is the mirror error in its most expensive
        /// form: the guardian trapping someone in a position it exists to protect them from.</summary>
        [Fact]
        public void LT1b_A_traders_exit_order_is_not_cancelled_while_locked()
        {
            OrderLifecycleBroker broker;
            var h = Breached(out broker);

            var traderExit = broker.PlaceTraderOrder(Harness.Account, Harness.Instrument, "Sell", -1);
            h.Guardian.OnOrderObserved(broker.SnapshotOf(traderExit));

            Assert.True(broker.IsWorking(traderExit),
                        "the guardian cancelled the trader's own exit - LT-1");
        }

        /// <summary>The slow killer, and the one the design note missed: RunLockoutSteps re-enters
        /// whole on every tick, so its blind account-wide sweep runs again and again - and would kill
        /// a flatten order still in flight one second later. The sweep belongs to the MOMENT the
        /// lockout begins, not to every attempt at completing it.</summary>
        [Fact]
        public void LT1c_The_blind_cancel_sweep_happens_once_however_often_the_steps_re_enter()
        {
            OrderLifecycleBroker broker;
            var h = Breached(out broker);

            Assert.Equal(1, broker.CancelAllCalls);

            h.Guardian.Tick();
            h.Guardian.Tick();
            h.Guardian.Tick();

            Assert.True(h.Guardian.Status.Kind == StateKind.Locked);
            Assert.Equal(1, broker.CancelAllCalls);
        }

        /// <summary>The containment. Removing the over-reach must not remove the sweep itself: the
        /// resting orders that exist AT the lockout are still cleared, once.</summary>
        [Fact]
        public void LT1d_Resting_orders_present_at_the_lockout_are_still_cleared()
        {
            var h = new Harness();
            var broker = new OrderLifecycleBroker();
            h.BrokerOverride = broker;
            h.Armed("600.00");
            broker.SetPosition(Harness.Account, Harness.Instrument, 1);
            var resting = broker.PlaceTraderOrder(Harness.Account, Harness.Instrument, "Buy", 1);

            h.LoseExactly(600.00m);
            h.Guardian.Tick();

            Assert.False(broker.IsWorking(resting));
            Assert.Contains(Ev.OrdersCancelled, h.Events());
        }
    }
}
