using System;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    /// <summary>G5: the breach is at &gt;=, not &gt;. G6: state is persisted before the first broker call.
    /// G7: a process killed mid-flatten resumes LOCKED. G8: orders after lockout are cancelled and logged.</summary>
    public class G5_G6_G7_G8_LockoutTests : Harness
    {
        [Fact]
        public void G5_landing_exactly_on_the_limit_is_a_breach()
        {
            Armed("600.00");
            LoseExactly(600.00m);

            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);
            Assert.False(Guardian.Status.EntriesAllowed);
            var breach = LastEvent(Ev.LimitBreached);
            Assert.NotNull(breach);
            Assert.Equal("600.00", ((JsonObject)breach["payload"]).GetString("dayLoss"));
            Assert.Equal("600.00", ((JsonObject)breach["payload"]).GetString("limit"));
        }

        [Fact]
        public void G5_one_cent_short_of_the_limit_is_not_a_breach()
        {
            Armed("600.00");
            LoseExactly(599.95m);

            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
            Assert.True(Guardian.Status.EntriesAllowed);
            Assert.False(HasEvent(Ev.LimitBreached));
        }

        [Fact]
        public void G5_a_profit_never_trips_the_limit()
        {
            Armed("600.00");
            Feed.SetPnl(Account, 0m, 0m);
            Guardian.OnExecution(new ExecutionRecord(Account, Instrument, Clock.UtcNow, 5000m, 1, Side.Long, 0m, PointValue, "in"));
            Feed.SetPnl(Account, 750m, 0m);
            Guardian.OnExecution(new ExecutionRecord(Account, Instrument, Clock.UtcNow, 5150m, 1, Side.Short, 0m, PointValue, "out"));

            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
        }

        [Fact]
        public void G6_the_state_file_says_LOCKED_before_the_first_order_reaches_the_broker()
        {
            Armed("600.00");
            Broker.SetPosition(Account, Instrument, 1);

            string diskAtFirstCall = null;
            Broker.Observer = _ => { diskAtFirstCall = diskAtFirstCall ?? StateOnDisk(); };

            LoseExactly(600.00m);

            Assert.NotNull(diskAtFirstCall);
            Assert.Contains("\"state\":\"LOCKED\"", diskAtFirstCall);
            // and the breach was in the ledger before the broker was touched, too
            Assert.Contains(Ev.LimitBreached, Events());
        }

        [Fact]
        public void G6_the_lockout_cancels_then_flattens_and_verifies()
        {
            Armed("600.00");
            Broker.SetPosition(Account, Instrument, 2);
            Broker.SetWorkingOrder(Account, "o1", Instrument, "Buy");

            LoseExactly(600.00m);

            Assert.Equal(new[] { "cancel:" + Account, "flatten:" + Account }, Broker.Calls.Take(2).ToArray());
            Assert.Contains(Ev.OrdersCancelled, Events());
            Assert.Contains(Ev.FlattenRequested, Events());
            Assert.Contains(Ev.FlattenVerified, Events());
            Assert.Empty(Broker.GetPositions(Account));
        }

        [Fact]
        public void G6_a_flatten_that_silently_does_nothing_is_reported_incomplete_never_verified()
        {
            Armed("600.00");
            Broker.SetPosition(Account, Instrument, 1);
            Broker.FlattenSilentlyDoesNothing = true;

            LoseExactly(600.00m);

            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);
            Assert.Contains(Ev.LockoutIncomplete, Events());
            Assert.DoesNotContain(Ev.FlattenVerified, Events());
        }

        [Fact]
        public void G7_a_process_killed_mid_flatten_comes_back_LOCKED_and_finishes_the_job()
        {
            Armed("600.00");
            Broker.SetPosition(Account, Instrument, 1);
            Broker.FlattenFailures = 1;         // the first flatten throws: the "crash" mid-sequence

            LoseExactly(600.00m);

            // The state on disk already says LOCKED even though the flatten never completed.
            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);
            Assert.Contains("\"state\":\"LOCKED\"", StateOnDisk());
            Assert.Contains("\"lockoutVerified\":false", StateOnDisk());
            Assert.Single(Broker.GetPositions(Account));   // still exposed

            // A brand-new process over the same files: no monotonic continuity, fresh Core.
            var resumed = NewGuardian("run-2");

            Assert.Equal(StateKind.Locked, resumed.Status.Kind);
            Assert.False(resumed.Status.EntriesAllowed);
            Assert.Empty(Broker.GetPositions(Account));    // the retry finished the flatten
            Assert.Contains(Ev.FlattenVerified, Events());
        }

        [Fact]
        public void G7_the_restart_never_comes_back_armed_when_the_state_said_locked()
        {
            Armed("600.00");
            LoseExactly(600.00m);
            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);

            var resumed = NewGuardian("run-2");
            Assert.Equal(StateKind.Locked, resumed.Status.Kind);
            Assert.NotEqual(StateKind.Armed, resumed.Status.Kind);
            Assert.NotEqual(StateKind.Disarmed, resumed.Status.Kind);
        }

        [Fact]
        public void G8_an_order_placed_after_the_lockout_is_cancelled_and_recorded()
        {
            Armed("600.00");
            LoseExactly(600.00m);
            Broker.Calls.Clear();

            Guardian.OnOrderObserved(new OrderSnapshot(Account, "o-99", Instrument, "Buy"));

            Assert.Contains("cancel:" + Account, Broker.Calls);
            var rejected = LastEvent(Ev.OrderRejectedLocked);
            Assert.NotNull(rejected);
            var payload = (JsonObject)rejected["payload"];
            Assert.Equal("o-99", payload.GetString("orderId"));
            Assert.Equal(Account, payload.GetString("account"));
        }

        [Fact]
        public void G8_enforcement_keeps_working_for_every_later_order_not_just_the_first()
        {
            Armed("600.00");
            LoseExactly(600.00m);

            for (int i = 0; i < 5; i++)
                Guardian.OnOrderObserved(new OrderSnapshot(Account, "o" + i, Instrument, "Buy"));

            Assert.Equal(5, Events().Count(e => e == Ev.OrderRejectedLocked));
        }

        [Fact]
        public void G8_orders_are_left_alone_while_armed()
        {
            Armed("600.00");
            Broker.Calls.Clear();

            Guardian.OnOrderObserved(new OrderSnapshot(Account, "o-1", Instrument, "Buy"));

            Assert.Empty(Broker.Calls);
            Assert.False(HasEvent(Ev.OrderRejectedLocked));
        }
    }
}
