// LT-2: five adapter fields that only exist if you were present at a particular instant, and the
// three whose TYPE forced them to lie about it.
//
// Observed in production on 2026-08-26. The lockout message a real person read said:
//
//     "You are down $40.00 and your limit is $0.00."
//
// The limit was $40. _personalLimit is assigned only inside the arm path; the F5 came after arming,
// the addon was rebuilt, the guardian restored ARMED from the seal WITHOUT re-arming, and the field
// kept its default. Same shape as M15, which was fixed for _guardedAccount without sweeping the
// family - my miss.
//
// THE MECHANISM, and it is the whole lesson:
//
//     string   -> null -> Messages.Until returns null -> the clause DISAPPEARS      (truth)
//     decimal  -> 0.00 -> printed as money                                          (lie)
//     int      -> 0    -> printed as a count                                        (lie)
//
// A PLAUSIBLE DEFAULT LIES; AN ABSENCE TELLS THE TRUTH BY SAYING NOTHING - and the field's TYPE
// decides which of the two it can do. The reference types could be absent; the value types had no
// such option. Worse, the plausible default is more dangerous than an absurd one: -999999 would have
// been caught on day one, and $0.00 is the most believable figure there is.
//
// The worst reachable case, and it is the one the product is judged on: the addon restarts DURING a
// lockout, nobody re-arms, and the observer never saw LIMIT_BREACHED or ORDERS_CANCELLED because they
// were already written. When FLATTEN_VERIFIED arrives, message two goes out with dayLoss $0.00, limit
// $0.00, 0 orders cancelled. Every figure false, on the most important event this product produces.

using System;
using GuardianCore;

namespace GuardianCore.Tests
{
    public class LT2_AbsentFiguresTests
    {
        private const string Acct = "Sim101";
        private static readonly string Until = Messages.Until("17:00", "America/Chicago");

        // ---------------------------------------------------------------- the restore, with nothing known

        /// <summary>Restart during a lockout: nothing was observed, so no figure is known. The message
        /// must not invent any of them, and must say where the real ones are.</summary>
        [Fact]
        public void LT2a_A_lockout_report_with_no_observed_figures_states_none_of_them()
        {
            var m = Messages.LockoutComplete(Acct, null, null, null, Until);

            Assert.DoesNotContain("$0.00", m, StringComparison.Ordinal);
            Assert.DoesNotContain("0 orders", m, StringComparison.Ordinal);
            Assert.Contains("your record", m, StringComparison.Ordinal);
        }

        [Fact]
        public void LT2b_The_imminent_warning_with_no_figures_still_says_what_is_about_to_happen()
        {
            var m = Messages.LockoutImminent(Acct, null, null);

            Assert.DoesNotContain("$0.00", m, StringComparison.Ordinal);
            // The ACTION is known even when the numbers are not - it is about to cancel and close.
            Assert.Contains("am about to cancel", m, StringComparison.Ordinal);
            Assert.Contains("switch off any strategy", m, StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------- the limit alone survives

        /// <summary>After LT-2's first layer the limit comes from the SEALED CONFIG, which a restore
        /// does have - so it is present even when the observed figures are not. The message says the
        /// limit and stays silent about the loss, which is exactly the state of knowledge.</summary>
        [Fact]
        public void LT2c_A_limit_recovered_from_the_seal_is_stated_while_the_unobserved_loss_is_not()
        {
            var m = Messages.LockoutComplete(Acct, null, 600.00m, null, Until);

            Assert.Contains("600.00", m, StringComparison.Ordinal);
            Assert.DoesNotContain("$0.00", m, StringComparison.Ordinal);
            Assert.Contains("your record", m, StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------- zero is a REAL value

        /// <summary>The containment that makes the rest meaningful: 0 is a legitimate figure. On
        /// 2026-08-26 ORDERS_CANCELLED carried count 0 for real - there were no resting orders. An
        /// absent count and a count of zero are different facts and must read differently.</summary>
        [Fact]
        public void LT2d_A_genuine_zero_is_still_reported_because_it_is_a_fact()
        {
            var m = Messages.LockoutComplete(Acct, 612.40m, 600.00m, 0, Until);

            Assert.Contains("0 orders cancelled", m, StringComparison.Ordinal);
            Assert.DoesNotContain("your record", m, StringComparison.Ordinal);
        }

        /// <summary>And the control: everything known, everything said, exactly as before LT-2.</summary>
        [Fact]
        public void LT2e_With_every_figure_known_the_message_is_unchanged()
        {
            var m = Messages.LockoutComplete(Acct, 612.40m, 600.00m, 3, Until);

            Assert.Contains("3 orders cancelled", m, StringComparison.Ordinal);
            Assert.Contains("612.40", m, StringComparison.Ordinal);
            Assert.Contains("600.00", m, StringComparison.Ordinal);
            Assert.Contains("America/Chicago", m, StringComparison.Ordinal);
            Assert.DoesNotContain("your record", m, StringComparison.Ordinal);
        }
    }
}
