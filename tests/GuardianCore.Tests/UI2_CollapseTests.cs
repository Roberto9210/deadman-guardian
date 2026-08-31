// Step 2: the panel can be reduced to a strip that stays on screen - except in the two states where
// being reduced to a strip is the failure.
//
// WHY A COLLAPSE AT ALL, and it is not a comfort feature. Candidate 9: the panel can be closed with
// one click and never comes back until an F5, and it is the ONLY channel that reaches this trader -
// Announce writes to a Log tab he does not read. A user who needs their screen back and has no
// legitimate way to get it will take the illegitimate one. Giving them a real way to reclaim the
// screen is what makes the later refusal-to-close defensible instead of a fight.
//
// THE TWO STATES THAT MAY NOT COLLAPSE, and the second was nearly missed:
//
//   LockoutNeedsHuman - the one state where the product depends on a person.
//   FailClosed        - the guardian is BLIND, and this panel is the only sign that the trader is
//                       not protected. It is arguably worse than NeedsHuman: there he knows his day
//                       is over; here he believes he has a brake and does not.
//
// FailClosed was missed on the first pass for a reason worth writing down: the review looked at the
// state that SHOUTS and not at the one that is QUIET. "THE GUARDIAN NEEDS YOU" is an imperative in
// capitals; "CANNOT SEE YOUR ACCOUNT" describes a condition and therefore reads as informational.
// Urgency of tone is not the same as what is at stake.

using System;
using GuardianCore;
using NinjaTrader.NinjaScript.AddOns.DeadmanGuardian;

namespace GuardianCore.Tests
{
    public class UI2_CollapseTests
    {
        private const string Acct = "Sim101";
        private static readonly string Until = Messages.Until("17:00", "America/Chicago");

        // ------------------------------------------------------------------ who may collapse

        [Fact]
        public void UI2a_The_ordinary_states_may_collapse()
        {
            Assert.True(PanelCollapse.MayCollapse(StateKind.Armed, false, null));
            Assert.True(PanelCollapse.MayCollapse(StateKind.Locked, false, null));
            Assert.True(PanelCollapse.MayCollapse(StateKind.Disarmed, false, null));
        }

        /// <summary>The one where the product depends on a person cannot be a strip in a corner.</summary>
        [Fact]
        public void UI2b_The_state_that_needs_a_human_may_not_collapse()
        {
            Assert.False(PanelCollapse.MayCollapse(StateKind.Locked, true, null));
        }

        /// <summary>THE ONE THAT WAS NEARLY MISSED. Blind is worse than stopped: a trader with this
        /// collapsed in a corner is trading in the belief that he has a brake on.</summary>
        [Fact]
        public void UI2c_A_blind_guardian_may_not_collapse_either()
        {
            Assert.False(PanelCollapse.MayCollapse(StateKind.FailClosed, false, "account is Disconnected"));

            // and M22 - limit reached, nothing closed - which arrives by a different route with the
            // same thing at stake: a position may be standing open AT the limit, all day.
            Assert.False(PanelCollapse.MayCollapse(StateKind.FailClosed, false,
                                                   Messages.ReasonLimitNotFlattened + ": ..."));
        }

        // ------------------------------------------------------------------ what is remembered

        /// <summary>Collapsed is remembered - except into DISARMED, and that exception is the whole
        /// product. Collapse on Tuesday, the session close leaves it DISARMED, and on Wednesday NT8
        /// opens to a strip saying NOT ARMED that asks for nothing. The guardian becomes furniture.
        /// This product depends on one voluntary act per day, and hiding its only button behind a
        /// click nobody remembers is how a commitment device stops being used - with nobody
        /// uninstalling it and nobody deciding to abandon it.</summary>
        [Fact]
        public void UI2d_Collapsed_is_remembered_everywhere_except_into_not_armed()
        {
            Assert.True(PanelCollapse.RemembersCollapsed(StateKind.Armed));
            Assert.True(PanelCollapse.RemembersCollapsed(StateKind.Locked));
            Assert.False(PanelCollapse.RemembersCollapsed(StateKind.Disarmed));
        }

        // ------------------------------------------------------------------ the strip's words

        [Fact]
        public void UI2e_Armed_carries_the_number_committed_to_and_until_when()
        {
            var s = Messages.Strip(StateKind.Armed, false, null, 600.00m, Until);

            Assert.Contains("ARMED", s, StringComparison.Ordinal);
            Assert.Contains("600.00", s, StringComparison.Ordinal);
            Assert.Contains("17:00", s, StringComparison.Ordinal);
            // THE ZONE TRAVELS. It is short and tempting to cut, and cutting it is what LT-2 fixed:
            // the guardian knows which zone it was configured with, so omitting it is a choice.
            Assert.Contains("America/Chicago", s, StringComparison.Ordinal);
        }

        /// <summary>LT-2 in the newest text. An absent figure is suppressed, never printed as 0.00 -
        /// which is where "your limit is $0.00" came from in the first place.</summary>
        [Fact]
        public void UI2f_An_absent_figure_is_suppressed_and_never_shown_as_zero()
        {
            var noLimit = Messages.Strip(StateKind.Armed, false, null, null, Until);
            Assert.DoesNotContain("0.00", noLimit, StringComparison.Ordinal);
            Assert.Contains("17:00", noLimit, StringComparison.Ordinal);

            var noUntil = Messages.Strip(StateKind.Armed, false, null, 600.00m, null);
            Assert.Contains("600.00", noUntil, StringComparison.Ordinal);
            Assert.DoesNotContain("until", noUntil, StringComparison.OrdinalIgnoreCase);

            var neither = Messages.Strip(StateKind.Armed, false, null, null, null);
            Assert.Equal("ARMED", neither);        // still grammatical with nothing to say
        }

        /// <summary>No loss figure once locked - a decision with TWO INDEPENDENT REASONS, and both
        /// are recorded because a "no" with one reason written is a "no" that falls over as soon as
        /// that reason stops applying.
        ///
        /// ONE: a figure that can be UNKNOWN, printed in a one-line strip, is the exact ground $0.00
        /// grew from. Once locked the actionable fact is when it lifts, and the guardian always knows
        /// that one.
        ///
        /// TWO, and it was only noticed after the power cut of 2026-08-31: a restart into LOCKED
        /// adopts no baseline (Guardian.cs:273 excludes Locked by name), so the book is EMPTY while
        /// OnExecution keeps applying fills into it - and an empty book books a closing fill as an
        /// opening one. That stale book is harmless ONLY because nothing reads it while Locked. The
        /// natural source for a live loss figure here is _book.Snapshot(), which is precisely the
        /// read that is unreachable. Adding the figure would make the stale book legible and put a
        /// wrong number in front of a locked-out trader.
        ///
        /// So this test guards two things at once, and the second is not visible from the assertion.
        /// If it ever goes red, fix the book before touching the strip.</summary>
        [Fact]
        public void UI2g_Locked_says_when_it_lifts_and_carries_no_figure_that_could_be_unknown()
        {
            var s = Messages.Strip(StateKind.Locked, false, null, 600.00m, Until);

            Assert.StartsWith("LOCKED", s, StringComparison.Ordinal);
            Assert.Contains("17:00 (America/Chicago)", s, StringComparison.Ordinal);
            Assert.DoesNotContain("600.00", s, StringComparison.Ordinal);
        }

        [Fact]
        public void UI2h_Not_armed_is_just_that()
        {
            Assert.Equal(Messages.HeadlineNotArmed, Messages.Strip(StateKind.Disarmed, false, null, null, null));
        }

        /// <summary>The strip inherits every ban the panel and the title carry. It is the text that
        /// will be read most often of anything this product writes, and inspected least.</summary>
        [Fact]
        public void UI2i_The_strip_may_not_promise_what_no_version_of_this_product_can_do()
        {
            var forbidden = new[]
            {
                "will be cancelled", "are being cancelled", "will be blocked", "will be prevented",
                "cannot place", "will not let", "won't be able", "unable to", "any new order will",
                "cannot trade", "blocked from trading", "prevented",
            };

            foreach (var s in new[]
            {
                Messages.Strip(StateKind.Armed, false, null, 600.00m, Until),
                Messages.Strip(StateKind.Armed, false, null, null, null),
                Messages.Strip(StateKind.Locked, false, null, 600.00m, Until),
                Messages.Strip(StateKind.Locked, true, null, 600.00m, Until),
                Messages.Strip(StateKind.FailClosed, false, "account is Disconnected", null, Until),
                Messages.Strip(StateKind.Disarmed, false, null, null, null),
            })
                foreach (var phrase in forbidden)
                    Assert.False(s.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0,
                                 "'" + phrase + "' in strip: " + s);
        }

        /// <summary>ASCII only, like every other string in this file. Messages.cs has been through
        /// patch scripts more than once and this project has the scars: heredocs mangle non-ASCII,
        /// and a separator that arrives as a control character in the one line a trader reads at a
        /// glance is not worth the typography.</summary>
        [Fact]
        public void UI2j_The_strip_is_ascii_like_every_other_message()
        {
            foreach (var s in new[]
            {
                Messages.Strip(StateKind.Armed, false, null, 600.00m, Until),
                Messages.Strip(StateKind.Locked, false, null, null, Until),
                Messages.Strip(StateKind.Disarmed, false, null, null, null),
            })
                foreach (var c in s)
                    Assert.True(c >= 32 && c <= 126, "non-ASCII in strip: " + s);
        }
    }
}
