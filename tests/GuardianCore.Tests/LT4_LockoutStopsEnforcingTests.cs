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
            var flattensAtVerification = h.Events().Count(e => e == Ev.FlattenRequested);

            // The trader is locked out and sends a market order anyway. It fills, because nothing in
            // NT8 can stop it - that is the detect-and-cancel window this product is honest about.
            var after = broker.PlaceTraderOrder(Harness.Account, Harness.Instrument, "Buy", 1);
            broker.Fill(after);
            Assert.Equal(1, broker.PositionOf(Harness.Account, Harness.Instrument));

            h.Guardian.OnOrderObserved(broker.SnapshotOf(after));
            h.Guardian.Tick();

            // FIXED 2026-08-31. It used to assert the position was STILL THERE - the defect - and
            // when the fix landed it stayed green, which is why it is rewritten rather than deleted:
            // it went on passing because the test never FILLS the re-flatten order, and
            // OrderLifecycleBroker does not move a position until it is filled. Three flattens were
            // being requested and none filled. A GREEN THAT MEANT THE OPPOSITE OF WHAT IT SAID -
            // exactly the double-simplifies-reality family this file's header warns about.
            //
            // So it asserts what the GUARDIAN controls: the latch reopened and a flatten was asked
            // for. Whether it fills is the venue's half, and LT4e carries that end to end.
            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);
            Assert.True(h.Events().Count(e => e == Ev.FlattenRequested) > flattensAtVerification,
                        "exposure returned and the guardian did not even ask for a flatten - LT-4");
        }

        /// <summary>REWRITTEN 2026-08-31, and it is now THE CONTAINMENT ON THE FIX rather than a
        /// record of the defect.
        ///
        /// It used to assert that no further flatten is ever attempted after the first verified one -
        /// LT-4 itself - and it went red the moment the fix landed, which is the convention working.
        ///
        /// What it guards now is the fix's own failure mode. Option A was chosen partly BECAUSE it
        /// fails noisily - repeated flattens - rather than destructively. "Noisy" is only acceptable
        /// while it stays proportionate: re-flattening an account that is already flat, every tick,
        /// until the seal expires, would be a flatten storm against the venue and a ledger nobody can
        /// read. The reopen must be driven by EXPOSURE, not by being Locked.</summary>
        [Fact]
        public void LT4b_A_flat_account_is_not_re_flattened_however_many_ticks_pass()
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

            var flattensWhenVerified = h.Events().Count(e => e == Ev.FlattenRequested);
            Assert.Contains(Ev.FlattenVerified, h.Events());
            Assert.Equal(0, broker.PositionOf(Harness.Account, Harness.Instrument));

            for (int i = 0; i < 10; i++) h.Guardian.Tick();     // ten ticks, still flat

            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);
            Assert.Equal(flattensWhenVerified, h.Events().Count(e => e == Ev.FlattenRequested));
            Assert.Equal(1, broker.CancelAllCalls);             // and still exactly one blind sweep
        }

        /// <summary>THE FIX. Written red, before the code, and it is LT4a's reciprocal.
        ///
        /// Exposure that appears AFTER the flatten verified is closed by the next cycle - which is
        /// what the LT-1 fix's own comment always claimed and what LT4a proved was not true.
        ///
        /// THE THIRD ASSERT IS THE POINT AND IT SHIPS WITH THE FIRST COMMIT, not after: the naive
        /// repair for LT-4 - stop returning early while Locked, sweep on every tick - IS LT-1. A
        /// repeated blind CancelAllOrders destroys exactly what M1's comment named, "a protective
        /// stop, on an account that may hold real money". Continuous enforcement and blind
        /// cancellation are not the same thing, and this test is where they stay separated.
        ///
        /// What makes the separation structural rather than remembered: the sweep lives in
        /// EnterLockout, not in RunLockoutSteps (LT1c pins that). Re-entering the steps therefore
        /// re-flattens and CANNOT re-sweep - so the fix reaches for the mechanism that reduces
        /// exposure and never for the one that destroys orders.</summary>
        [Fact]
        public void LT4e_Exposure_opened_after_the_verified_flatten_is_closed_by_the_next_cycle()
        {
            var h = new Harness();
            var broker = new OrderLifecycleBroker();
            h.BrokerOverride = broker;
            h.Armed("600.00");
            broker.SetPosition(Harness.Account, Harness.Instrument, 1);
            h.LoseExactly(600.00m);

            h.Guardian.Tick();                                   // breach -> sweep once -> flatten
            broker.Fill(broker.LastFlattenOrder().OrderId);
            h.Guardian.Tick();                                   // verified
            Assert.Contains(Ev.FlattenVerified, h.Events());

            var flattensBefore = h.Events().Count(e => e == Ev.FlattenRequested);
            var sweepsAtLockout = broker.CancelAllCalls;

            // A market order the guardian could not prevent - NT8 has no pre-submit veto - fills and
            // opens exposure while LOCKED.
            var after = broker.PlaceTraderOrder(Harness.Account, Harness.Instrument, "Buy", 1);
            broker.Fill(after);
            Assert.Equal(1, broker.PositionOf(Harness.Account, Harness.Instrument));

            h.Guardian.OnOrderObserved(broker.SnapshotOf(after));
            h.Guardian.Tick();                                   // the next cycle notices and flattens
            var reflatten = broker.LastFlattenOrder();
            Assert.NotNull(reflatten);
            broker.Fill(reflatten.OrderId);
            h.Guardian.Tick();                                   // and verifies again

            Assert.True(h.Events().Count(e => e == Ev.FlattenRequested) > flattensBefore,
                        "no further flatten was even attempted - LT-4");
            Assert.Equal(0, broker.PositionOf(Harness.Account, Harness.Instrument));

            // THE CONTAINMENT. Not one blind sweep more than the single one at lockout.
            Assert.Equal(sweepsAtLockout, broker.CancelAllCalls);
        }

        /// <summary>THE ONE THAT MAKES LT-4 A BROKEN PROMISE RATHER THAN A GAP.
        ///
        /// On 2026-08-31 at 09:10:30 the product told a real person, in NinjaTrader's log, verbatim:
        ///
        ///     "LOCKED. 0 orders cancelled and positions closed on Sim101, at $40.00 against a
        ///      $40.00 limit. ANY NEW ORDER WILL BE CANCELLED until 17:00 (America/Chicago).
        ///      This is what you asked for."
        ///
        /// It was written one second after FLATTEN_VERIFIED, which is precisely the moment
        /// LockoutVerified latches true. This test asks the sentence's own question - would a new
        /// order have been cancelled? - and the answer is no.
        ///
        /// The enumeration behind it, small enough to be exhaustive: CancelAllOrders has ONE call
        /// site in the library (Guardian.cs:916, inside SweepRestingOrders), SweepRestingOrders has
        /// one caller (EnterLockout), and EnterLockout's breach path (:747) is unreachable while
        /// Locked because the tick returns at :633-638. Nothing else cancels.
        ///
        /// So this is the house's first defect class - TEXT THAT ASSERTS MORE THAN ITS OWN CODE
        /// CHECKED - reached through the second one, and in the worst possible place: the single
        /// artefact a human actually reads, at the single moment the product exists for.</summary>
        [Fact]
        public void LT4d_The_message_promises_new_orders_will_be_cancelled_and_they_are_not()
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
            Assert.Contains(Ev.FlattenVerified, h.Events());     // the message goes out HERE
            var cancelsWhenTheMessageWentOut = broker.CancelAllCalls;   // the sweep's one, at lockout

            // "Any new order will be cancelled." One arrives.
            var newOrder = broker.PlaceTraderOrder(Harness.Account, Harness.Instrument, "Buy", 1);
            h.Guardian.OnOrderObserved(broker.SnapshotOf(newOrder));
            for (int i = 0; i < 5; i++) h.Guardian.Tick();

            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);   // still locked, still promising
            Assert.Equal(cancelsWhenTheMessageWentOut, broker.CancelAllCalls);   // not one more
            Assert.True(broker.IsWorking(newOrder),
                        "the message said any new order would be cancelled; this one is still working");
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
