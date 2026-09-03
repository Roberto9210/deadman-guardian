// THE RECORD THAT G8'S REMOVAL TOOK WITH IT.
//
// IT IS ASKED FOR BY AN EXPERIMENT THAT ALREADY FAILED FOR ITS ABSENCE. On 2026-09-03 a live
// breach test on Sim101 set out to observe whether a new order gets through while LOCKED - the
// falsifier the sealed prediction cared about most. When the moment came, the ledger could not tell
// "we stopped it silently" from "we did not stop it", because ORDER_REJECTED_LOCKED has had no
// writer since 2026-08-27 (A12). We had to go to NinjaTrader's own Orders tab for an answer this
// file should have held. Removing the cancel also removed the RECORD, and nobody noticed until an
// experiment leaned on it.
//
// WHY THIS IS NOT G8 REOPENED: G8 is about CANCELLING. This is about RECORDING. They are different
// properties, and confusing them is exactly what made ORDER_REJECTED_LOCKED a false name - it
// asserted an action in order to report an observation.
//
// THE NAME CARRIES THE WHOLE CLAIM: OBSERVED, WHILE LOCKED. No outcome, no decision, no action.
// Rejected, blocked, refused and ignored were all considered and dropped: each asserts something
// that does not happen.
//
// THE RULE APPLIED BEFORE WRITING IT - is there a change cheaper than the real fix that turns the
// red green? The first test demands the event; the third and fourth demand it be in the right
// place; and the second makes the DANGEROUS shortcut impossible - anyone who "improves" this into a
// cancellation turns it red, and the failure message says why. A record that could quietly become a
// brake is how this repository got G8 in the first place.

using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    public class G8b_OrderObservationTests : Harness
    {
        /// <summary>The finding in one test: an order seen while LOCKED leaves a row that names it.</summary>
        [Fact]
        public void G8b_An_order_seen_while_locked_is_written_down()
        {
            Armed("600.00");
            LoseExactly(600.00m);
            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);

            Guardian.OnOrderObserved(new OrderSnapshot(Account, "o-1", Instrument, "Buy"));

            var row = LastEvent(Ev.OrderObservedWhileLocked);
            Assert.NotNull(row);
            var payload = (JsonObject)row["payload"];
            Assert.Equal(Account, payload.GetString("account"));
            Assert.Equal("o-1", payload.GetString("orderId"));
            Assert.Equal(Instrument, payload.GetString("instrument"));
            Assert.Equal("Buy", payload.GetString("action"));
        }

        /// <summary>THE CONTROL THAT KEEPS IT HONEST. A record does not touch the broker, and it does
        /// not write the name that asserts an action. If this ever goes red, somebody turned an
        /// observation into an enforcement - which is LT-1, which cost the guardian its own flatten
        /// orders and four of the trader's on 2026-08-26.</summary>
        [Fact]
        public void G8b_It_is_a_record_and_not_a_brake()
        {
            Armed("600.00");
            LoseExactly(600.00m);
            var callsBefore = Broker.Calls.Count;

            Guardian.OnOrderObserved(new OrderSnapshot(Account, "o-1", Instrument, "Buy"));

            Assert.Equal(callsBefore, Broker.Calls.Count);
            Assert.False(HasEvent(Ev.OrderRejectedLocked));
        }

        /// <summary>Not locked, nothing written. Without this the event would degrade into "we saw an
        /// order", which is noise on every ordinary trading day and would bury the case it exists for.</summary>
        [Fact]
        public void G8b_An_order_seen_while_not_locked_writes_nothing()
        {
            Armed("600.00");
            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);

            Guardian.OnOrderObserved(new OrderSnapshot(Account, "o-1", Instrument, "Buy"));

            Assert.False(HasEvent(Ev.OrderObservedWhileLocked));
        }

        /// <summary>The foreign-account branch keeps its own event and still returns before this one.
        /// M1 exists because acting on an account we were merely TOLD about is the inversion of how
        /// everything here is built; recording it under the guarded-account name would repeat that
        /// mistake in the record instead of in the broker.</summary>
        [Fact]
        public void G8b_A_foreign_account_takes_the_foreign_branch_and_only_that_one()
        {
            Armed("600.00");
            LoseExactly(600.00m);

            Guardian.OnOrderObserved(new OrderSnapshot("NotMine", "o-9", Instrument, "Buy"));

            Assert.True(HasEvent(Ev.ForeignAccountOrderObserved));
            Assert.False(Events().Any(e => e == Ev.OrderObservedWhileLocked));
        }
    }
}
