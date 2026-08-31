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
// FIXED 2026-08-31, option A: ReopenLockoutIfExposureReturned un-latches when the account is not
// flat, so RunLockoutSteps re-enters and re-FLATTENS. It cannot re-sweep - the blind cancel lives in
// EnterLockout and only there - so the naive repair, which IS LT-1, is unreachable by construction.
// Option B (targeted cancellation through an optional port) is deferred with its trigger written
// down: reconsider when the LEDGER shows repeated reopening under lockout.
//
// The tests that documented the defect were rewritten when they flipped, which is the M4-M7
// convention working. One of them, LT4a, STAYED GREEN after its own fix and had to be caught by
// reading it: it passed because it never filled the re-flatten order, and the double does not move a
// position until filled. A green that meant the opposite of what it said.
//
// WHAT THE FIX DOES NOT DO, and the reason LT4d exists: it does not cancel anything. NT8 has no
// pre-submit veto, so no version of this product can stop an order from reaching the market. The
// message that promised otherwise was replaced the same day (C), and LT4d holds the replacement to a
// vocabulary of impossibility rather than to a single phrase.

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

        /// <summary>Candidate 8's half that the product can fix on its own: the ONE surface a trader
        /// sees without looking for it must be able to say a human is needed.
        ///
        /// On 2026-08-26 the guardian asked for help 165 times and Roberto's answer on 2026-08-31 was
        /// "no me di cuenta". Announce writes to NinjaTrader's Log tab, which he does not read - so
        /// message 3 was never delivered to the person it names. Meanwhile the status panel, which IS
        /// Topmost and IS on screen, said the ordinary locked text: that no position would stay open,
        /// while one was open and stuck, for five days.
        ///
        /// Derived, never stored: LockoutVerified and FlattenAttempts are already persisted, so this
        /// survives a restart. An adapter-side flag would be the LT-2 / M15 family all over again - a
        /// field that only exists if the process was present at a particular instant.
        ///
        /// And it retracts itself: FLATTEN_VERIFIED sets LockoutVerified, the derivation goes false,
        /// the panel returns to the ordinary text. If the panel promises, the panel must be able to
        /// take it back - by construction, not by remembering to.</summary>
        [Fact]
        public void LT4g_The_panel_can_say_a_human_is_needed_and_only_while_one_is()
        {
            var h = new Harness();
            var broker = new OrderLifecycleBroker();
            h.BrokerOverride = broker;
            h.Armed("600.00");
            Assert.False(h.Guardian.LockoutNeedsHuman, "armed and fine");

            broker.SetPosition(Harness.Account, Harness.Instrument, 1);
            h.LoseExactly(600.00m);
            h.Guardian.Tick();                                  // attempt 1, flatten in flight
            Assert.False(h.Guardian.LockoutNeedsHuman, "one unfilled attempt is an ordinary lockout");

            h.Guardian.Tick();                                  // 2
            h.Guardian.Tick();                                  // 3 - now it is genuinely stuck
            Assert.True(h.Guardian.LockoutNeedsHuman);

            broker.Fill(broker.LastFlattenOrder().OrderId);     // it finally closes
            h.Guardian.Tick();
            Assert.Contains(Ev.FlattenVerified, h.Events());
            Assert.False(h.Guardian.LockoutNeedsHuman, "the panel must take it back on its own");
        }

        /// <summary>The text, and the trap it has to avoid.
        ///
        /// The derivation is computed off a flag the ledger calls `exhausted`, and MaxFlattenAttempts
        /// is a constant named "maximum attempts" - but this morning's enumeration proved it gates no
        /// loop at all (Guardian.cs:1044 is its only use; the guardian retries forever). If the panel
        /// inherits that vocabulary and says "gave up" or "stopped", it reintroduces exactly the lie
        /// we removed from message 3 an hour earlier - candidate 7 biting through a name into the text
        /// derived from it.
        ///
        /// Three things are true at once and the panel has to hold all three: something is still open,
        /// the guardian has NOT stopped, and it needs the person.</summary>
        [Fact]
        public void LT4h_The_needs_you_text_never_says_the_guardian_stopped()
        {
            var m = Messages.DetailNeedsYou(Harness.Account);

            foreach (var lie in new[] { "gave up", "gives up", "stopped trying", "no longer trying",
                                        "could not close", "cannot close", "has stopped" })
                Assert.False(m.IndexOf(lie, StringComparison.OrdinalIgnoreCase) >= 0,
                             "the panel says the guardian stopped, and it has not: '" + lie + "' in: " + m);

            Assert.Contains("still open", m, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("keeps trying", m, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CLOSE IT YOURSELF", m, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The taskbar/Alt-Tab title, which is the one piece of text a person reads WITHOUT
        /// switching to the window. It was the constant "deadman-guardian" - the product's name, which
        /// the reader already knows - so the free channel said nothing.
        ///
        /// The state goes FIRST, because a taskbar entry is truncated from the right.</summary>
        [Fact]
        public void LT4i_The_window_title_carries_the_state_and_leads_with_it()
        {
            Assert.StartsWith("CLOSE IT YOURSELF", Messages.WindowTitle(StateKind.Locked, true),
                              StringComparison.Ordinal);
            Assert.StartsWith("LOCKED", Messages.WindowTitle(StateKind.Locked, false),
                              StringComparison.Ordinal);

            // every state still identifies the product, after the state
            foreach (var k in new[] { StateKind.Armed, StateKind.Locked, StateKind.FailClosed, StateKind.Disarmed })
                Assert.Contains("deadman-guardian", Messages.WindowTitle(k, false), StringComparison.Ordinal);

            // and the needs-you title may not inherit the giving-up vocabulary either (LT4h's rule,
            // applied to the channel that is read most often and inspected least)
            foreach (var lie in new[] { "gave up", "stopped", "could not" })
                Assert.False(Messages.WindowTitle(StateKind.Locked, true)
                                     .IndexOf(lie, StringComparison.OrdinalIgnoreCase) >= 0, lie);
        }

        /// <summary>THE PERMANENT CONTAINMENT ON WHAT THE LOCKOUT IS ALLOWED TO PROMISE.
        ///
        /// NT8 has no pre-submit veto - 2,912 types scanned in and out of process, STEP3_FINDINGS
        /// section 4 - so NO version of this product can stop an order from reaching the market. Any
        /// message that says otherwise is false on the day it is written, not merely made false later
        /// by a defect. "Any new order will be cancelled" survived until a real person read it on
        /// 2026-08-31 at 09:10:30.
        ///
        /// SO THIS BANS A WAY OF SPEAKING, NOT A SENTENCE. The next person to write a message here
        /// will not copy that phrase; they will invent their own version of the same promise. The
        /// list below is what makes this test outlive the phrase it was born from.
        ///
        /// AND IT BANS CONSTRUCTIONS, NEVER BARE WORDS - the distinction is the whole design and it
        /// is not a detail:
        ///
        ///     "0 orders cancelled"        TRUE. A past report of a sweep that really happened.
        ///     "will be cancelled"         FALSE. A promise about orders not yet sent.
        ///
        /// "cancelled" is therefore NOT on the list. A ban that cannot tell tense from tense forbids
        /// the truth along with the lie - and the repair for that is not an exceptions list. An
        /// exceptions list grows, and in six months someone adds the exception that covers a real lie.
        /// That is how containments die. Every entry below is a string that can only occur in a
        /// forward-looking claim.</summary>
        [Fact]
        public void LT4d_No_lockout_message_may_promise_that_an_order_will_be_stopped()
        {
            var forbidden = new[]
            {
                "will be cancelled", "are being cancelled", "will be blocked", "will be prevented",
                "cannot place", "will not let", "won't be able", "unable to", "any new order will",
            };

            var withUntil = Messages.Until("17:00", "America/Chicago");
            var messages = new[]
            {
                Messages.LockoutComplete(Harness.Account, 612.40m, 600.00m, 3, withUntil),
                Messages.LockoutComplete(Harness.Account, (decimal?)null, (decimal?)null, (int?)null, withUntil),
                Messages.LockoutComplete(Harness.Account, (decimal?)null, (decimal?)null, (int?)null, null),
                Messages.LockoutImminent(Harness.Account, 612.40m, 600.00m),
                Messages.LockoutStillOpen(Harness.Account, 3),
                Messages.DetailLocked(Harness.Account, withUntil),
                Messages.DetailLocked(Harness.Account, null),          // absent until: still grammatical, still honest
            };

            foreach (var m in messages)
                foreach (var phrase in forbidden)
                    Assert.False(m.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0,
                                 "a lockout message promises what no version of this product can do - '" +
                                 phrase + "' in: " + m);

            // And the containment on the containment: the TRUE past report survives the ban.
            Assert.Contains("3 orders cancelled",
                            Messages.LockoutComplete(Harness.Account, 612.40m, 600.00m, 3, withUntil),
                            StringComparison.Ordinal);
        }

        /// <summary>Kept from when it recorded the defect: LT-4's fix closes exposure, it does not
        /// cancel, so the message may not claim cancellation. This asserts the ACT still matches the
        /// words - nothing is cancelled on observation - which is the other half of LT4d.
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
        public void LT4f_Nothing_is_cancelled_on_observation_while_locked()
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
