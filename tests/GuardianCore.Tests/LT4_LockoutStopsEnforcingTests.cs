// LT-4: once the flatten verifies, the lockout STOPS FLATTENING - and that is the exact premise the
// LT-1 fix rests on.
//
// Found on 2026-08-29 while working out how many flatten rounds to expect from the LT-1 behavioural
// test. Not by intuition: by enumerating every call site of RunLockoutSteps and every assignment of
// LockoutVerified, which is a small enough set to be exhaustive.
//
//     Guardian.cs:240   if (Kind == Locked && !LockoutVerified) RunLockoutSteps();   <- tick
//     Guardian.cs:635   if (!LockoutVerified) RunLockoutSteps();                     <- tick
//     Guardian.cs:890   RunLockoutSteps();                                           <- EnterLockout
//
//     Guardian.cs:478   = false    Arm
//     Guardian.cs:851   = false    CheckExpiry, on the way to Disarmed
//     Guardian.cs:886   = false    EnterLockout
//     Guardian.cs:965   = TRUE     RunLockoutSteps, flatten verified
//     Guardian.cs:972   = false    RunLockoutSteps, something still open
//
// NOTHING sets it back to false while the guardian STAYS Locked. So after the first verified flatten
// both tick guards are permanently closed, and RunLockoutSteps is never called again for that
// lockout. New exposure opened afterwards is never closed.
//
// WHY IT MATTERS MORE THAN IT LOOKS, and why it is filed as its own defect rather than a footnote:
// this is the load-bearing sentence of the LT-1 fix, in LT-1's own comment and in this suite's
// header -
//
//     "not cancelling wrongly - one order opens exposure during the lockout; the NEXT CYCLE'S
//      FLATTEN closes it. Loss BOUNDED by one cycle."
//
// There is no next cycle's flatten. The bound is asserted, not implemented.
//
// AND IT IS OLDER THAN THE LT-1 FIX. Before 2026-08-26, OnOrderObserved called
// CancelAllOrders(order.Account) on every observed order, which is what actually covered this hole -
// badly, blindly, and at the cost of killing the guardian's own flatten. Removing the over-reach was
// right; it also removed the only thing standing here. THE HOUSE PATTERN, one more time: a check
// that exists is not a check that runs, and here the check that ran was the defect.
//
// THE ARITHMETIC OF THE WORST CASE, so nobody has to guess: the lockout verifies with the account
// flat, the trader (or a strategy NinjaTrader has not switched off yet) sends one market order, it
// fills, and the position stands until the human closes it or the session ends. Unbounded, on an
// account the guardian has already declared it is protecting.
//
// NOT FIXED HERE. The fix is a design decision - re-verify on every tick, or re-arm the steps when a
// position appears - and it must not be made in the same breath as the finding. These tests are
// GREEN BECAUSE THEY DOCUMENT WHAT THE CODE DOES TODAY, the same convention as M4-M7. Each MUST go
// red when the fix lands; one that stays green means the fix touched nothing.

using System;
using System.Linq;
using GuardianCore;

namespace GuardianCore.Tests
{
    public class LT4_LockoutStopsEnforcingTests
    {
        /// <summary>The whole finding in one test. Lockout, flatten completes, THEN a market order
        /// fills - exactly what Bot A's even-numbered probes do on purpose - and the guardian never
        /// flattens again. The position stands.</summary>
        [Fact]
        public void LT4a_Exposure_opened_after_the_flatten_verifies_is_never_closed()
        {
            var h = new Harness();
            var broker = new OrderLifecycleBroker();
            h.BrokerOverride = broker;
            h.Armed("600.00");
            broker.SetPosition(Harness.Account, Harness.Instrument, 1);
            h.LoseExactly(600.00m);

            h.Guardian.Tick();                                   // breach -> sweep -> flatten requested
            var flatten = broker.LastFlattenOrder();
            broker.Fill(flatten.OrderId);
            h.Guardian.Tick();                                   // and now it verifies

            Assert.Contains(Ev.FlattenVerified, h.Events());
            Assert.Equal(0, broker.PositionOf(Harness.Account, Harness.Instrument));

            // The trader is locked out and sends a market order anyway. It fills, because nothing in
            // NT8 can stop it - that is the detect-and-cancel window this product is honest about.
            var after = broker.PlaceTraderOrder(Harness.Account, Harness.Instrument, "Buy", 1);
            broker.Fill(after);
            Assert.Equal(1, broker.PositionOf(Harness.Account, Harness.Instrument));

            h.Guardian.OnOrderObserved(broker.SnapshotOf(after));
            h.Guardian.Tick();
            h.Guardian.Tick();
            h.Guardian.Tick();

            // Still locked, still says so - and the position is still there. THE DEFECT.
            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);
            Assert.Equal(1, broker.PositionOf(Harness.Account, Harness.Instrument));
        }

        /// <summary>The mechanism, isolated from the position: after the verified flatten, no further
        /// FLATTEN_REQUESTED is ever written however many ticks pass. This is the one that says WHY
        /// LT4a fails rather than just that it does.</summary>
        [Fact]
        public void LT4b_No_further_flatten_is_even_attempted_after_the_first_verified_one()
        {
            var h = new Harness();
            var broker = new OrderLifecycleBroker();
            h.BrokerOverride = broker;
            h.Armed("600.00");
            broker.SetPosition(Harness.Account, Harness.Instrument, 1);
            h.LoseExactly(600.00m);

            h.Guardian.Tick();
            broker.Fill(broker.LastFlattenOrder().OrderId);
            h.Guardian.Tick();

            var flattensAtVerification = h.Events().Count(e => e == Ev.FlattenRequested);
            Assert.Contains(Ev.FlattenVerified, h.Events());

            broker.SetPosition(Harness.Account, Harness.Instrument, 2);   // exposure, by any route
            for (int i = 0; i < 10; i++) h.Guardian.Tick();

            Assert.Equal(flattensAtVerification, h.Events().Count(e => e == Ev.FlattenRequested));
        }

        /// <summary>The containment that keeps LT4a honest: BEFORE the flatten verifies, re-entry
        /// works exactly as designed. The defect is the latch, not the loop - LT-1's own fix relies
        /// on this half continuing to work, and it does.</summary>
        [Fact]
        public void LT4c_Before_verification_the_steps_do_re_enter_every_tick()
        {
            var h = new Harness();
            var broker = new OrderLifecycleBroker();
            h.BrokerOverride = broker;
            h.Armed("600.00");
            broker.SetPosition(Harness.Account, Harness.Instrument, 1);
            h.LoseExactly(600.00m);

            h.Guardian.Tick();                                   // flatten requested, not filled
            var first = h.Events().Count(e => e == Ev.FlattenRequested);

            h.Guardian.Tick();
            h.Guardian.Tick();

            Assert.True(h.Events().Count(e => e == Ev.FlattenRequested) > first,
                        "re-entry before verification is what closes a position the first attempt missed");
        }
    }
}
