// The panel's position: the regression that was caught by one question, and the comfort file that
// must never be able to hurt anyone.
//
// HOW THE REGRESSION APPEARED, because the method matters more than the bug. On 2026-08-31 the panel
// was taught to grow in the state that needs a human, so Width moved into Render - and Left with it,
// recomputed from the corner of the work area on EVERY refresh. A trader who dragged the panel would
// have watched it walk back to the corner about once a second.
//
// Nobody saw it while it was being written, and the reason is a gap in method rather than attention:
// the whole day had been spent reviewing what the panel SAYS, word by word, and not one question had
// been asked about what a person can DO with it. A message review is not a manipulation review.
//
// The second half came out of Roberto's own tense. "Me sale en primer plano y lo muevo para una
// esquina" - habitual present. He does it EVERY session, because the constructor fixes the position
// and every F5 rebuilds the window. Fixing the snap alone would only have stretched the interval
// between re-tidyings from one second to one F5.

using System;
using NinjaTrader.NinjaScript.AddOns.DeadmanGuardian;

namespace GuardianCore.Tests
{
    public class UI1_PanelPlacementTests
    {
        // A 1920x1040 work area, origin at 0,0 - the shape of an ordinary primary monitor.
        private const double L = 0, T = 0, R = 1920, B = 1040;

        /// <summary>THE REGRESSION, in one line. The panel widens and the right edge does not move, so
        /// it grows leftward from wherever it was put instead of jumping to a corner.</summary>
        [Fact]
        public void UI1a_Widening_keeps_the_right_edge_where_the_trader_left_it()
        {
            // dragged to the left of the screen, then widened 330 -> 430
            var left = PanelPlacement.LeftAfterWidthChange(200, 330, 430);

            Assert.Equal(100, left);                 // grew leftward
            Assert.Equal(530, left + 430);           // and the right edge did not move
        }

        /// <summary>And back: narrowing returns the right edge to the same place, so collapsing and
        /// expanding repeatedly cannot walk the panel across the screen.</summary>
        [Fact]
        public void UI1b_Narrowing_is_the_exact_inverse_so_the_panel_does_not_drift()
        {
            var wide = PanelPlacement.LeftAfterWidthChange(200, 330, 430);
            var back = PanelPlacement.LeftAfterWidthChange(wide, 430, 330);

            Assert.Equal(200, back);
        }

        /// <summary>A position the trader chose is not second-guessed for being unusual - only for
        /// being unreachable. An odd but visible spot survives untouched.</summary>
        [Fact]
        public void UI1c_A_reachable_position_is_left_alone()
        {
            var p = PanelPlacement.Clamp(37, 611, 330, 190, L, T, R, B);

            Assert.Equal(37, p.Left);
            Assert.Equal(611, p.Top);
        }

        /// <summary>THE ONE THAT MAKES A SAVED POSITION SAFE TO RESTORE. A laptop undocked, or a second
        /// monitor gone, leaves coordinates that no longer exist. Without this, restoring "remembers"
        /// the panel onto a screen nobody has and the trader cannot find the only surface that
        /// works.</summary>
        [Fact]
        public void UI1d_A_position_on_a_monitor_that_is_gone_comes_back_on_screen()
        {
            var p = PanelPlacement.Clamp(3200, 1400, 330, 190, L, T, R, B);

            Assert.Equal(R - 330, p.Left);
            Assert.Equal(B - 190, p.Top);
            Assert.True(p.Left >= L && p.Left + 330 <= R);
            Assert.True(p.Top >= T && p.Top + 190 <= B);
        }

        [Fact]
        public void UI1e_Negative_coordinates_come_back_too()
        {
            var p = PanelPlacement.Clamp(-500, -80, 330, 190, L, T, R, B);

            Assert.Equal(L, p.Left);
            Assert.Equal(T, p.Top);
        }

        /// <summary>A panel bigger than the work area pins top-left rather than centring: the headline
        /// is at the top, so that is the half worth keeping when something has to be clipped.</summary>
        [Fact]
        public void UI1f_A_panel_larger_than_the_screen_keeps_its_headline_visible()
        {
            var p = PanelPlacement.Clamp(50, 50, 4000, 3000, L, T, R, B);

            Assert.Equal(L, p.Left);
            Assert.Equal(T, p.Top);
        }

        // ------------------------------------------------------------------ the comfort file

        /// <summary>Every way this file can be broken produces defaults and nothing else. It is the one
        /// place in this codebase where a plausible default is right, and it is right precisely
        /// because NOTHING DEPENDS ON IT: a forgotten window position is a fresh panel in the corner,
        /// not an unknown the guardian has to defend against.</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{")]
        [InlineData("not json at all")]
        [InlineData("{\"left\": }")]
        [InlineData("{\"left\": \"NaN\", \"top\": 20}")]
        [InlineData("{\"top\": 20}")]
        [InlineData("{\"leftish\": 10, \"top\": 20}")]
        [InlineData("{\"left\": 10, \"top\": 20, \"somethingFromTheFuture\": 7}")]
        public void UI1g_A_broken_or_unknown_comfort_file_never_throws_and_never_blocks(string text)
        {
            var prefs = UiPrefs.Parse(text);          // must not throw, whatever it is

            Assert.NotNull(prefs);
            // the last case is the only one that legitimately has a position
            if (text != null && text.Contains("somethingFromTheFuture"))
            {
                Assert.True(prefs.HasPosition);
                Assert.Equal(10, prefs.Left.Value);
            }
            else
            {
                Assert.False(prefs.HasPosition, "a file this broken must not yield a position");
            }
        }

        /// <summary>What was written comes back. Round-trip, including the collapsed flag that step 2
        /// will use.</summary>
        [Fact]
        public void UI1h_A_saved_position_survives_the_round_trip()
        {
            var written = new UiPrefs { Left = 412.5, Top = 96, Collapsed = true }.Format();
            var read = UiPrefs.Parse(written);

            Assert.True(read.HasPosition);
            Assert.Equal(412.5, read.Left.Value);
            Assert.Equal(96, read.Top.Value);
            Assert.True(read.Collapsed);
        }

        /// <summary>Written on a machine with a comma decimal separator - which Roberto's NinjaTrader
        /// uses, its own log prints "precio promedio = 7714,25" - and read anywhere. The format is
        /// invariant on both sides, so a position does not become a different number abroad.</summary>
        [Fact]
        public void UI1i_The_format_does_not_depend_on_the_machines_decimal_separator()
        {
            var text = new UiPrefs { Left = 412.5, Top = 96 }.Format();

            Assert.Contains("412.5", text, StringComparison.Ordinal);
            Assert.DoesNotContain("412,5", text, StringComparison.Ordinal);
        }
    }
}
