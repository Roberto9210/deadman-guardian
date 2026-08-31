// The sentences a human reads, under test - because they are product CLAIMS.
//
// One of them was already false: "you cannot trade until 17:00" promised something SPEC section 17
// explicitly denies, in the one message a trader is guaranteed to read. Another would have fired at
// the wrong moment and sent every user to hand-close a position that was closing itself. Prose is not
// compiled by anything, so it needs the tests more than code does, not less.

using System;
using GuardianCore;

namespace GuardianCore.Tests
{
    public class C_MessagesTests
    {
        private const string Acct = "Sim101";
        private static readonly string Until = Messages.Until("17:00", "America/Chicago");

        // ---------------------------------------------------------------- headlines

        /// <summary>The two states that used to share a headline are opposites: fail-closed is armed
        /// and blocking, disarmed is nothing at all. A real person went looking for the Arm button
        /// because one sentence served both (2026-08-22).</summary>
        [Fact]
        public void Fail_closed_and_disarmed_no_longer_say_the_same_thing()
        {
            Assert.Equal("CANNOT SEE YOUR ACCOUNT", Messages.Headline(StateKind.FailClosed));
            Assert.Equal("NOT ARMED", Messages.Headline(StateKind.Disarmed));
            Assert.NotEqual(Messages.Headline(StateKind.FailClosed), Messages.Headline(StateKind.Disarmed));
        }

        /// <summary>Retired wording is gone everywhere. The one this list starts with misled toward
        /// the dangerous side in fail-closed: it suggested nothing was operating when something was.</summary>
        [Theory]
        [InlineData(StateKind.Armed)]
        [InlineData(StateKind.Locked)]
        [InlineData(StateKind.FailClosed)]
        [InlineData(StateKind.Disarmed)]
        public void No_headline_uses_retired_wording_any_more(StateKind kind)
        {
            // Read from Messages.Retired rather than spelled here: a test that hard-codes the phrase
            // contains the string it forbids, and then has to exempt itself from the repository scan.
            foreach (var retired in Messages.Retired)
                Assert.DoesNotContain(retired, Messages.Headline(kind), StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------- the actionable detail

        [Fact]
        public void Cannot_see_tells_the_reader_what_to_do_not_only_what_happened()
        {
            var d = Messages.DetailCannotSee("account is Disconnected", hasSeal: true, until: Until);

            Assert.Contains("Connect the feed", d, StringComparison.Ordinal);
            Assert.Contains("nothing needs restarting", d, StringComparison.Ordinal);
            Assert.Contains("not letting new positions open", d, StringComparison.Ordinal);
        }

        /// <summary>FailClosed does not always have a seal - StartCorrupt enters it with none - so the
        /// "you are still armed" sentence cannot be asserted without looking. Each variant has to be
        /// true in the moment it is written.</summary>
        [Fact]
        public void With_a_seal_it_explains_the_missing_Arm_button_by_saying_you_are_already_armed()
        {
            var d = Messages.DetailCannotSee("account is Disconnected", hasSeal: true, until: Until);

            Assert.Contains("still armed", d, StringComparison.Ordinal);
            Assert.Contains("no Arm button", d, StringComparison.Ordinal);
            Assert.Contains("nothing to arm", d, StringComparison.Ordinal);
        }

        [Fact]
        public void Without_a_seal_it_promises_the_button_instead_of_claiming_you_are_armed()
        {
            var d = Messages.DetailCannotSee("state file unreadable", hasSeal: false, until: null);

            Assert.Contains("Nothing is armed yet", d, StringComparison.Ordinal);
            Assert.Contains("Arm button will appear", d, StringComparison.Ordinal);
            Assert.DoesNotContain("still armed", d, StringComparison.Ordinal);
        }

        /// <summary>"until 17:00" means nothing to a reader in another zone, and the guardian knows
        /// which zone it was configured with, so dropping it is a choice rather than a limitation.</summary>
        [Fact]
        public void Every_time_carries_its_zone()
        {
            Assert.Equal("17:00 (America/Chicago)", Messages.Until("17:00", "America/Chicago"));
            Assert.Contains("America/Chicago", Messages.DetailCannotSee("x", true, Until), StringComparison.Ordinal);
            Assert.Contains("America/Chicago", Messages.DetailLocked(Acct, Until), StringComparison.Ordinal);
        }

        /// <summary>A time we do not have is omitted, never invented.</summary>
        [Fact]
        public void A_missing_time_is_left_out_rather_than_guessed()
        {
            Assert.Null(Messages.Until(null, "America/Chicago"));
            Assert.DoesNotContain("until", Messages.DetailLocked(Acct, null), StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------- the lockout, in two parts

        /// <summary>Message one is written BEFORE the broker is touched, so it may not claim anything
        /// was done. If the cancel later fails in part, a past-tense sentence here would leave the
        /// record asserting something false in a file sold as evidence.</summary>
        [Fact]
        public void The_first_lockout_message_promises_and_never_reports()
        {
            var m = Messages.LockoutImminent(Acct, 612.40m, 600.00m);

            Assert.Contains("am about to cancel", m, StringComparison.Ordinal);
            Assert.DoesNotContain("I cancelled", m, StringComparison.Ordinal);
            Assert.DoesNotContain("closed your positions.", m, StringComparison.Ordinal);
            Assert.Contains("612.40", m, StringComparison.Ordinal);
            Assert.Contains("600.00", m, StringComparison.Ordinal);
        }

        /// <summary>It also warns about NinjaTrader's own message before NinjaTrader writes it - the
        /// Log is read downwards, and an explanation arriving after "Disabling NinjaScript strategy"
        /// corrects nothing, because nobody keeps reading past the point where they think they
        /// understand.</summary>
        [Fact]
        public void The_first_message_pre_empts_the_platform_switching_strategies_off()
        {
            var m = Messages.LockoutImminent(Acct, 612.40m, 600.00m);

            Assert.Contains("switch off any strategy", m, StringComparison.Ordinal);
            Assert.Contains("not an error", m, StringComparison.Ordinal);
            Assert.Contains("nothing is broken", m, StringComparison.Ordinal);
        }

        [Fact]
        public void The_second_lockout_message_reports_with_the_real_figures()
        {
            var m = Messages.LockoutComplete(Acct, 612.40m, 600.00m, 3, Until);

            Assert.Contains("3 orders cancelled", m, StringComparison.Ordinal);
            Assert.Contains("positions closed", m, StringComparison.Ordinal);
            Assert.Contains("612.40", m, StringComparison.Ordinal);
            Assert.Contains("America/Chicago", m, StringComparison.Ordinal);
        }

        [Fact]
        public void One_cancelled_order_is_not_pluralised()
        {
            Assert.Contains("1 order cancelled", Messages.LockoutComplete(Acct, 1m, 2m, 1, Until), StringComparison.Ordinal);
            Assert.Contains("0 orders cancelled", Messages.LockoutComplete(Acct, 1m, 2m, 0, Until), StringComparison.Ordinal);
        }

        /// <summary>SPEC section 17: hitting the limit does not bound the loss. No message may promise
        /// that trading is prevented - the guardian detects and cancels, and saying otherwise in the
        /// one text a trader is guaranteed to read would be the product contradicting its own threat
        /// model two documents away.</summary>
        [Theory]
        [InlineData("cannot trade")]
        [InlineData("will not be able to trade")]
        [InlineData("prevented")]
        [InlineData("blocked from trading")]
        public void No_message_promises_that_trading_is_prevented(string forbidden)
        {
            foreach (var m in new[]
            {
                Messages.DetailLocked(Acct, Until),
                Messages.LockoutImminent(Acct, 1m, 2m),
                Messages.LockoutComplete(Acct, 1m, 2m, 1, Until),
                Messages.LockoutStillOpen(Acct, 3)
            })
            {
                Assert.DoesNotContain(forbidden, m, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>REWRITTEN 2026-08-31, and HOW it went red is worth more than the test.
        ///
        /// It used to assert the messages CONTAIN "will be cancelled" - the positive half of the pair
        /// above, pinning what the product promises instead of over-promising. That was TRUE when it
        /// was written: OnOrderObserved cancelled every order seen while locked.
        ///
        /// LT-1 removed that on 2026-08-26 and THIS TEST STAYED GREEN FOR FIVE DAYS, because it pins
        /// the message's wording against ITSELF. Nothing pinned the wording to the behaviour, so the
        /// suite went on certifying a promise the code had stopped keeping - until a real person read
        /// it on 2026-08-31.
        ///
        /// That is the fifth candidate's shape appearing inside the test suite: WE AUDIT ACTS, NOT
        /// WORDS. A test asserting that a message says X is worth nothing unless something else
        /// asserts that X is what happens.
        ///
        /// So the promise moved to where the product can keep it - the POSITION, not the order - which
        /// is what SPEC T7 always said: "the order fills and the next cycle closes it". The code moved
        /// to match the spec, not the other way round.</summary>
        [Fact]
        public void What_is_promised_instead_is_what_actually_happens_no_position_stays_open()
        {
            foreach (var m in new[] { Messages.DetailLocked(Acct, Until),
                                      Messages.LockoutComplete(Acct, 1m, 2m, 1, Until) })
            {
                Assert.Contains("no position will stay open", m, StringComparison.Ordinal);
                // and it says out loud what it cannot do, rather than leaving that to be assumed
                Assert.Contains("does not block new orders", m, StringComparison.Ordinal);
            }
        }

        /// <summary>The line that works when its reader is looking for someone to blame.</summary>
        [Fact]
        public void The_completion_message_reminds_the_reader_this_was_their_own_decision()
        {
            Assert.Contains("This is what you asked for", Messages.LockoutComplete(Acct, 1m, 2m, 1, Until), StringComparison.Ordinal);
        }

        /// <summary>Reserved for a TERMINAL failure only. In a normal successful lockout the transient
        /// LOCKOUT_INCOMPLETE appears about half a second before FLATTEN_VERIFIED (measured, first real
        /// run, 2026-08-22), so firing this text there would send every user to hand-close a position
        /// that is closing itself. Gating on exhausted:true is the caller's duty; this test pins that
        /// the text is unmistakably an alarm, so nobody reuses it for the transient case by accident.</summary>
        [Fact]
        public void The_failure_message_is_an_alarm_and_asks_the_human_to_act()
        {
            var m = Messages.LockoutStillOpen(Acct, 3);

            Assert.Contains("COULD NOT CLOSE EVERYTHING", m, StringComparison.Ordinal);
            Assert.Contains("CLOSE IT YOURSELF NOW", m, StringComparison.Ordinal);
            Assert.Contains("3 attempts", m, StringComparison.Ordinal);
            Assert.DoesNotContain("This is what you asked for", m, StringComparison.Ordinal);
        }
    }
}
