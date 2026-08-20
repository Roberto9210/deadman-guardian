using System;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    /// <summary>G3: day P&amp;L includes commissions and matches a hand-computed fixture.
    /// G4: losses are summed across accounts, never netted.
    /// G14: sources disagreeing is an unknown, with no tie-break.
    /// G15: a missing price for an open position is an unknown, never zero.</summary>
    public class G3_G4_G14_G15_PnlTests
    {
        private const decimal MesPointValue = 5.00m;   // MES: $5 per point (SPEC 3.1 contract note)
        private static readonly DateTime T0 = new DateTime(2026, 8, 19, 14, 30, 0, DateTimeKind.Utc);

        private static ExecutionRecord Fill(string account, Side side, decimal price, int qty,
                                            decimal commission, string id, string instrument = "MES 09-26")
            => new ExecutionRecord(account, instrument, T0, price, qty, side, commission, MesPointValue, id);

        [Fact]
        public void G3_a_hand_computed_round_trip_matches_to_the_cent()
        {
            // Buy 2 @ 5000.00, sell 2 @ 5003.25 => +3.25 points * 2 contracts * $5 = +$32.50 gross.
            // Commission $1.40 per side per contract as the harness of SPEC 4: 2 * 2 * 0.70 = $2.80.
            var book = new PnlBook();
            Assert.True(book.Apply(Fill("A", Side.Long, 5000.00m, 2, 1.40m, "e1"), out _));
            Assert.True(book.Apply(Fill("A", Side.Short, 5003.25m, 2, 1.40m, "e2"), out _));

            Assert.Equal(32.50m, book.GrossRealized("A"));
            Assert.Equal(2.80m, book.Commissions("A"));

            var feed = new FakeAccountFeed("A");
            feed.SetPnl("A", 32.50m, 0m);
            var snap = book.Snapshot(new[] { "A" }, feed, 5.00m);

            Assert.True(snap.Ok);
            Assert.Equal(29.70m, snap.Accounts[0].DayPnl);    // 32.50 - 2.80
            Assert.Equal(0m, snap.TotalDayLoss);
        }

        [Fact]
        public void G3_commissions_can_turn_a_gross_win_into_a_net_loss()
        {
            // +0.25 points on 1 contract = $1.25 gross; $3.90 friction => -$2.65 net.
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000.00m, 1, 1.95m, "e1"), out _);
            book.Apply(Fill("A", Side.Short, 5000.25m, 1, 1.95m, "e2"), out _);

            var feed = new FakeAccountFeed("A");
            feed.SetPnl("A", 1.25m, 0m);
            var snap = book.Snapshot(new[] { "A" }, feed, 5.00m);

            Assert.Equal(1.25m, book.GrossRealized("A"));
            Assert.Equal(3.90m, book.Commissions("A"));
            Assert.Equal(-2.65m, snap.Accounts[0].DayPnl);
            Assert.Equal(2.65m, snap.TotalDayLoss);
        }

        [Fact]
        public void G3_short_side_realizes_the_right_sign()
        {
            // Sell 1 @ 5000, buy back @ 4990 => +10 points * $5 = +$50.
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Short, 5000m, 1, 0m, "e1"), out _);
            book.Apply(Fill("A", Side.Long, 4990m, 1, 0m, "e2"), out _);
            Assert.Equal(50m, book.GrossRealized("A"));

            // And a losing short: sell @ 5000, buy back @ 5010 => -$50.
            var book2 = new PnlBook();
            book2.Apply(Fill("B", Side.Short, 5000m, 1, 0m, "e1"), out _);
            book2.Apply(Fill("B", Side.Long, 5010m, 1, 0m, "e2"), out _);
            Assert.Equal(-50m, book2.GrossRealized("B"));
        }

        [Fact]
        public void G3_partial_exits_and_average_entry_are_accounted_for()
        {
            // Buy 1 @ 5000, buy 1 @ 5010 (avg 5005), sell 1 @ 5015 => +10 pts * $5 = +$50.
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 0m, "e1"), out _);
            book.Apply(Fill("A", Side.Long, 5010m, 1, 0m, "e2"), out _);
            book.Apply(Fill("A", Side.Short, 5015m, 1, 0m, "e3"), out _);

            Assert.Equal(50m, book.GrossRealized("A"));
            Assert.Equal(1, book.NetQuantity("A", "MES 09-26"));   // still long one
            Assert.True(book.HasOpenPosition("A"));
        }

        [Fact]
        public void G3_a_reversal_closes_and_opens_in_one_fill()
        {
            // Long 1 @ 5000, then sell 2 @ 5010: closes +$50 and leaves a short of 1 @ 5010.
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 0m, "e1"), out _);
            book.Apply(Fill("A", Side.Short, 5010m, 2, 0m, "e2"), out _);

            Assert.Equal(50m, book.GrossRealized("A"));
            Assert.Equal(-1, book.NetQuantity("A", "MES 09-26"));
        }

        [Fact]
        public void G3_a_duplicate_execution_id_is_not_counted_twice()
        {
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 1.00m, "e1"), out _);
            book.Apply(Fill("A", Side.Long, 5000m, 1, 1.00m, "e1"), out _);   // same id replayed
            Assert.Equal(1.00m, book.Commissions("A"));
            Assert.Equal(1, book.NetQuantity("A", "MES 09-26"));
        }

        [Fact]
        public void G3_an_execution_without_a_point_value_is_an_unknown_not_a_zero()
        {
            var book = new PnlBook();
            var bad = new ExecutionRecord("A", "MES 09-26", T0, 5000m, 1, Side.Long, 1m, 0m, "e1");
            Assert.False(book.Apply(bad, out var problem));
            Assert.Contains("point value", problem);

            var feed = new FakeAccountFeed("A");
            var snap = book.Snapshot(new[] { "A" }, feed, 5.00m);
            Assert.False(snap.Ok);
            Assert.Equal(PnlStatus.InvalidPointValue, snap.FirstProblem.Status);
        }

        [Fact]
        public void G4_losses_are_summed_across_accounts_and_never_netted()
        {
            // A wins $500, B loses $700. Netting would report $200 of profit and let B breach unnoticed.
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 0m, "a1"), out _);
            book.Apply(Fill("A", Side.Short, 5100m, 1, 0m, "a2"), out _);      // +100 pts = +$500
            book.Apply(Fill("B", Side.Long, 5000m, 1, 0m, "b1"), out _);
            book.Apply(Fill("B", Side.Short, 4860m, 1, 0m, "b2"), out _);      // -140 pts = -$700

            var feed = new FakeAccountFeed("A", "B");
            feed.SetPnl("A", 500m, 0m);
            feed.SetPnl("B", -700m, 0m);
            var snap = book.Snapshot(new[] { "A", "B" }, feed, 5.00m);

            Assert.True(snap.Ok);
            Assert.Equal(500m, snap.Accounts.Single(a => a.Account == "A").DayPnl);
            Assert.Equal(-700m, snap.Accounts.Single(a => a.Account == "B").DayPnl);
            Assert.Equal(700m, snap.TotalDayLoss);   // not 200
        }

        [Fact]
        public void G14_sources_disagreeing_beyond_tolerance_is_an_unknown_with_no_tie_break()
        {
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 0m, "e1"), out _);
            book.Apply(Fill("A", Side.Short, 4900m, 1, 0m, "e2"), out _);      // core: -$500

            var feed = new FakeAccountFeed("A");
            feed.SetPnl("A", -100m, 0m);                                        // platform: -$100
            var snap = book.Snapshot(new[] { "A" }, feed, 5.00m);

            Assert.False(snap.Ok);
            Assert.Equal(PnlStatus.SourcesDisagree, snap.FirstProblem.Status);
            // Neither number was adopted, and nothing was averaged.
            Assert.Contains("core -500.00 vs platform -100.00", snap.FirstProblem.Detail);
            Assert.Equal(0m, snap.TotalDayLoss);   // a broken account contributes no number at all
        }

        [Fact]
        public void G14_a_disagreement_within_tolerance_is_accepted()
        {
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 0m, "e1"), out _);
            book.Apply(Fill("A", Side.Short, 4999m, 1, 0m, "e2"), out _);      // core: -$5
            var feed = new FakeAccountFeed("A");
            feed.SetPnl("A", -7m, 0m);                                          // platform: -$7, delta $2
            var snap = book.Snapshot(new[] { "A" }, feed, 5.00m);
            Assert.True(snap.Ok);
            Assert.Equal(5m, snap.TotalDayLoss);   // core's number is used, not the platform's
        }

        [Fact]
        public void G14_platform_reporting_nothing_while_core_has_realized_pnl_is_an_unknown()
        {
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 0m, "e1"), out _);
            book.Apply(Fill("A", Side.Short, 4900m, 1, 0m, "e2"), out _);
            var feed = new FakeAccountFeed("A");
            feed.SetPnl("A", null, 0m);
            var snap = book.Snapshot(new[] { "A" }, feed, 5.00m);
            Assert.False(snap.Ok);
            Assert.Equal(PnlStatus.SourcesDisagree, snap.FirstProblem.Status);
        }

        [Fact]
        public void G15_an_open_position_with_no_price_is_an_unknown_never_zero()
        {
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 0m, "e1"), out _);   // still open

            var feed = new FakeAccountFeed("A");
            feed.SetPnl("A", 0m, null);                                     // no quote
            var snap = book.Snapshot(new[] { "A" }, feed, 5.00m);

            Assert.False(snap.Ok);
            Assert.Equal(PnlStatus.NoPriceForOpenPosition, snap.FirstProblem.Status);
        }

        [Fact]
        public void G15_a_flat_account_needs_no_price()
        {
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 0m, "e1"), out _);
            book.Apply(Fill("A", Side.Short, 5000m, 1, 0m, "e2"), out _);   // flat again

            var feed = new FakeAccountFeed("A");
            feed.SetPnl("A", 0m, null);
            var snap = book.Snapshot(new[] { "A" }, feed, 5.00m);
            Assert.True(snap.Ok);
        }

        [Fact]
        public void G15_unrealized_loss_counts_towards_the_day_loss()
        {
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 1.95m, "e1"), out _);
            var feed = new FakeAccountFeed("A");
            feed.SetPnl("A", 0m, -250m);
            var snap = book.Snapshot(new[] { "A" }, feed, 5.00m);

            Assert.True(snap.Ok);
            Assert.Equal(-251.95m, snap.Accounts[0].DayPnl);
            Assert.Equal(251.95m, snap.TotalDayLoss);
        }

        [Fact]
        public void G3_resetting_the_day_clears_everything()
        {
            var book = new PnlBook();
            book.Apply(Fill("A", Side.Long, 5000m, 1, 1.00m, "e1"), out _);
            book.Apply(Fill("A", Side.Short, 4900m, 1, 1.00m, "e2"), out _);
            Assert.NotEqual(0m, book.GrossRealized("A"));

            book.ResetDay();
            Assert.Equal(0m, book.GrossRealized("A"));
            Assert.Equal(0m, book.Commissions("A"));
            Assert.False(book.HasOpenPosition("A"));
        }
    }
}
