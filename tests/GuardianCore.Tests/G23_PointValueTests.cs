using System;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    /// <summary>
    /// G23 (SPEC §5.7): the contract's point value comes from platform metadata, and a missing, zero or
    /// negative one is an unknown that blocks entries. It is never defaulted to 1.
    ///
    /// The guarantee exists because the failure it prevents is silent. A guardian that substituted 1 for a
    /// missing $5 point value would report a fifth of every loss, stay `ARMED`, and let the real loss run
    /// far past the limit while showing a comfortable number on screen. Blocking is the loud failure; a
    /// plausible substitute is the quiet one.
    /// </summary>
    public class G23_PointValueTests : Harness
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 19, 14, 30, 0, DateTimeKind.Utc);

        private static ExecutionRecord Fill(decimal pointValue, string id, Side side = Side.Long, decimal price = 5000m)
            => new ExecutionRecord("A", "MES 09-26", T0, price, 1, side, 1m, pointValue, id);

        [Theory]
        [InlineData(0.0)]      // metadata absent: NT8 handed us nothing usable
        [InlineData(-5.0)]     // metadata corrupt
        [InlineData(-0.01)]
        public void G23_a_non_positive_point_value_is_an_unknown_not_a_number(double pointValue)
        {
            var book = new PnlBook();
            Assert.False(book.Apply(Fill((decimal)pointValue, "e1"), out var problem));
            Assert.Contains("point value", problem);

            var snap = book.Snapshot(new[] { "A" }, new FakeAccountFeed("A"), 5.00m);
            Assert.False(snap.Ok);
            Assert.Equal(PnlStatus.InvalidPointValue, snap.FirstProblem.Status);
        }

        [Fact]
        public void G23_a_broken_point_value_contributes_no_number_at_all_to_the_day_loss()
        {
            var book = new PnlBook();
            book.Apply(Fill(0m, "e1"), out _);
            book.Apply(Fill(0m, "e2", Side.Short, 4880m), out _);   // would be -120 points

            var snap = book.Snapshot(new[] { "A" }, new FakeAccountFeed("A"), 5.00m);

            Assert.Equal(0m, snap.TotalDayLoss);          // not $120, not $600: no number is produced
            Assert.Equal(PnlStatus.InvalidPointValue, snap.FirstProblem.Status);
        }

        [Fact]
        public void G23_the_guardian_blocks_entries_when_the_point_value_is_unusable()
        {
            Armed("600.00");
            Assert.True(Guardian.Status.EntriesAllowed);

            Guardian.OnExecution(new ExecutionRecord(Account, Instrument, Clock.UtcNow, 5000m, 1, Side.Long, 0m, 0m, "bad1"));

            Assert.Equal(StateKind.FailClosed, Guardian.Status.Kind);
            Assert.False(Guardian.Status.EntriesAllowed);
            Assert.True(HasEvent(Ev.PnlUncomputable));
            Assert.Contains("point value", ((JsonObject)LastEvent(Ev.PnlUncomputable)["payload"]).GetString("problem")
                                           ?? ((JsonObject)LastEvent(Ev.PnlUncomputable)["payload"]).GetString("detail"));
        }

        [Fact]
        public void G23_a_bad_point_value_never_falls_back_to_one()
        {
            // The fixture is built so the two readings diverge across the limit:
            //   120 points on 1 MES contract at $5.00 = $600.00 -> a breach of the $600 limit
            //   the same 120 points at a substituted 1.0        = $120.00 -> comfortably inside it
            // If Core ever defaulted to 1, this test would see an ARMED guardian reporting $120.
            Armed("600.00");

            Feed.SetPnl(Account, 0m, 0m);
            Guardian.OnExecution(new ExecutionRecord(Account, Instrument, Clock.UtcNow, 5000m, 1, Side.Long, 0m, 0m, "in"));
            Feed.SetPnl(Account, -600m, 0m);
            Guardian.OnExecution(new ExecutionRecord(Account, Instrument, Clock.UtcNow, 4880m, 1, Side.Short, 0m, 0m, "out"));

            Assert.Equal(StateKind.FailClosed, Guardian.Status.Kind);
            Assert.False(Guardian.Status.EntriesAllowed);

            // No checkpoint ever carried the substituted figure, and no breach was decided on it either.
            var checkpoints = LedgerEntries()
                .Where(e => e.GetString("event") == Ev.PnlCheckpoint)
                .Select(e => ((JsonObject)e["payload"]).GetString("dayLoss"))
                .ToList();
            Assert.DoesNotContain("120.00", checkpoints);
            Assert.False(HasEvent(Ev.LimitBreached));
        }

        [Fact]
        public void G23_the_unknown_clears_once_a_usable_point_value_arrives()
        {
            // The guarantee must not be satisfied by blocking forever: a real point value resolves it.
            Armed("600.00");
            Guardian.OnExecution(new ExecutionRecord(Account, Instrument, Clock.UtcNow, 5000m, 1, Side.Long, 0m, 0m, "bad"));
            Assert.Equal(StateKind.FailClosed, Guardian.Status.Kind);

            // A fresh session: the adapter reconnects and the metadata is there this time.
            Clock.Advance(TimeSpan.FromHours(2));       // seal expires, day rolls
            Guardian.Tick();
            Assert.Equal(StateKind.Disarmed, Guardian.Status.Kind);

            Assert.True(Guardian.Arm(Config("600.00")).Ok);
            Feed.SetPnl(Account, 0m, 0m);
            Guardian.OnExecution(new ExecutionRecord(Account, Instrument, Clock.UtcNow, 5000m, 1, Side.Long, 0m, PointValue, "good"));
            Feed.SetPnl(Account, 0m, -50m);
            Guardian.Tick();

            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
            Assert.True(Guardian.Status.EntriesAllowed);
        }

        [Fact]
        public void G23_a_valid_point_value_produces_the_real_money_figure()
        {
            // Control for the whole guarantee: with the platform's $5.00, 120 points is $600.00 and trips.
            Armed("600.00");
            LoseExactly(600.00m);

            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);
            Assert.Equal("600.00", ((JsonObject)LastEvent(Ev.LimitBreached)["payload"]).GetString("dayLoss"));
        }
    }
}
