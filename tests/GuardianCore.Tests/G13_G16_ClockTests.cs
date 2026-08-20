using System;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    /// <summary>G13a: a forward jump in session is detected, fails closed, and the seal is MAINTAINED.
    /// G13b: a backward jump is logged in both continuity cases and never releases the seal.
    /// G13c: expiry in session is measured on the monotonic clock, not the wall clock.
    /// G13d: a sleep-like divergence must not pin FAIL_CLOSED forever.
    /// G16: an unknown or disconnected account fails closed.</summary>
    public class G13_G16_ClockTests : Harness
    {
        [Fact]
        public void G13a_winding_the_clock_forward_past_expiry_blocks_trading_instead_of_releasing_the_seal()
        {
            Armed("600.00");
            var sealHash = Guardian.Status.SealHash;

            Clock.Advance(TimeSpan.FromMinutes(30));      // half an hour of honest time
            Guardian.Tick();
            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);

            // The bypass: set the system clock past 17:00 CT so the seal "expires". Monotonic does not move.
            Clock.SetWallClockOnly(SessionEnd.AddMinutes(1));
            Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, Guardian.Status.Kind);
            Assert.False(Guardian.Status.EntriesAllowed);
            Assert.Equal(sealHash, Guardian.Status.SealHash);          // the seal is still in force
            Assert.False(HasEvent(Ev.SealExpired));
            Assert.False(HasEvent(Ev.Disarmed));

            var anomaly = LastEvent(Ev.ClockAnomaly);
            Assert.NotNull(anomaly);
            var payload = (JsonObject)anomaly["payload"];
            Assert.Equal("forward", payload.GetString("direction"));
            Assert.Equal(true, ((JsonBool)payload["sealMaintained"]).Value);
        }

        [Fact]
        public void G13a_a_locked_session_cannot_be_unlocked_by_moving_the_clock_forward()
        {
            Armed("600.00");
            LoseExactly(600.00m);
            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);

            Clock.SetWallClockOnly(SessionEnd.AddHours(1));
            Guardian.Tick();

            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);
            Assert.False(Guardian.Status.EntriesAllowed);
            Assert.False(HasEvent(Ev.LockoutCleared));
        }

        [Fact]
        public void G13b_a_backward_jump_in_session_is_an_anomaly_and_keeps_the_seal()
        {
            Armed("600.00");
            var sealHash = Guardian.Status.SealHash;
            Clock.Advance(TimeSpan.FromMinutes(20));
            Guardian.Tick();

            Clock.SetWallClockOnly(Start.AddMinutes(-45));   // wind back
            Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, Guardian.Status.Kind);
            Assert.Equal(sealHash, Guardian.Status.SealHash);
            var anomaly = LastEvent(Ev.ClockAnomaly);
            Assert.NotNull(anomaly);
            Assert.Equal("backward", ((JsonObject)anomaly["payload"]).GetString("direction"));
        }

        [Fact]
        public void G13b_a_backward_jump_across_a_restart_is_recorded_as_suspect_because_it_cannot_be_proved()
        {
            Armed("600.00");
            Clock.Advance(TimeSpan.FromMinutes(30));
            Guardian.Tick();

            // New process (no monotonic continuity) and the wall clock is now earlier than last seen.
            Clock.SetWallClockOnly(Start.AddMinutes(-30));
            var restarted = NewGuardian("run-2");

            Assert.Equal(StateKind.FailClosed, restarted.Status.Kind);
            Assert.True(HasEvent(Ev.ClockSuspect));
            var suspect = LastEvent(Ev.ClockSuspect);
            Assert.Equal(true, ((JsonBool)((JsonObject)suspect["payload"])["sealMaintained"]).Value);
            // The trace of SPEC 17.2: the ledger's own timestamps are no longer monotonic.
            var timestamps = LedgerEntries().Select(e => e.GetString("tsUtc")).ToList();
            Assert.True(timestamps.Zip(timestamps.Skip(1), (a, b) => string.CompareOrdinal(a, b) > 0).Any(),
                        "a backward step should be visible in the ledger timestamps");
        }

        [Fact]
        public void G13c_expiry_is_measured_on_the_monotonic_clock_when_continuity_exists()
        {
            Armed("600.00");

            // Real time passes past the sealed duration while the wall clock stays where it was.
            Clock.AdvanceMonotonicOnly(TimeSpan.FromHours(2));
            Guardian.Tick();

            Assert.Equal(StateKind.Disarmed, Guardian.Status.Kind);
            var expired = LastEvent(Ev.SealExpired);
            Assert.NotNull(expired);
            Assert.Equal("monotonic", ((JsonObject)expired["payload"]).GetString("basis"));
        }

        [Fact]
        public void G13c_the_wall_clock_reaching_expiry_does_not_expire_the_seal_in_session()
        {
            Armed("600.00");
            Clock.Advance(TimeSpan.FromMinutes(10));
            Guardian.Tick();

            Clock.SetWallClockOnly(SessionEnd.AddMinutes(5));   // wall says "past 17:00", monotonic says 10 minutes
            Guardian.Tick();

            Assert.NotEqual(StateKind.Disarmed, Guardian.Status.Kind);
            Assert.NotNull(Guardian.Status.SealHash);
            Assert.False(HasEvent(Ev.SealExpired));
        }

        [Fact]
        public void G13c_after_a_restart_the_wall_clock_is_all_the_evidence_there_is_and_the_seal_expires()
        {
            // The documented gap of SPEC 17.2: without monotonic continuity, a premeditated bypass wins.
            Armed("600.00");
            Clock.Advance(TimeSpan.FromHours(3));              // honest time, past 17:00 CT
            var restarted = NewGuardian("run-2");

            Assert.Equal(StateKind.Disarmed, restarted.Status.Kind);
            var expired = LastEvent(Ev.SealExpired);
            Assert.Equal("wallclock", ((JsonObject)expired["payload"]).GetString("basis"));
        }

        [Fact]
        public void G13d_a_sleep_like_divergence_does_not_pin_fail_closed_forever()
        {
            Armed("600.00");

            // The machine sleeps for an hour: the wall clock advances, the monotonic counter does not.
            Clock.SetWallClockOnly(Start.AddHours(1));
            Guardian.Tick();
            Assert.Equal(StateKind.FailClosed, Guardian.Status.Kind);
            Assert.True(HasEvent(Ev.ClockAnomaly));

            // Now time passes honestly again and the P&L is computable: the unknown resolves.
            Clock.Advance(TimeSpan.FromMinutes(1));
            Guardian.Tick();

            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
            Assert.True(Guardian.Status.EntriesAllowed);
            Assert.True(HasEvent(Ev.FailClosedCleared));
        }

        [Fact]
        public void G13d_clearing_an_unknown_re_computes_and_can_land_straight_in_a_lockout()
        {
            Armed("600.00");

            // Unknown first: an open position with no price.
            Feed.SetPnl(Account, 0m, 0m);
            Guardian.OnExecution(new ExecutionRecord(Account, Instrument, Clock.UtcNow, 5000m, 1, Side.Long, 0m, PointValue, "in"));
            Feed.SetPnl(Account, 0m, null);
            Guardian.Tick();
            Assert.Equal(StateKind.FailClosed, Guardian.Status.Kind);

            // The price comes back, and it shows the limit was already breached while we were blind.
            Feed.SetPnl(Account, 0m, -800m);
            Guardian.Tick();

            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);
            Assert.True(HasEvent(Ev.LimitBreached));
        }

        [Fact]
        public void G13_small_clock_drift_within_tolerance_is_not_an_anomaly()
        {
            Armed("600.00");
            Clock.Advance(TimeSpan.FromMinutes(5));
            Clock.SetWallClockOnly(Clock.UtcNow.AddSeconds(30));   // an NTP step, well inside 120s
            Guardian.Tick();

            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
            Assert.False(HasEvent(Ev.ClockAnomaly));
        }

        [Fact]
        public void G16_an_account_that_disappears_from_the_platform_fails_closed()
        {
            Armed("600.00");
            Feed.Remove(Account);
            Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, Guardian.Status.Kind);
            Assert.False(Guardian.Status.EntriesAllowed);
            Assert.True(HasEvent(Ev.AccountUnknown));
        }

        [Fact]
        public void G16_a_disconnected_account_fails_closed_too()
        {
            Armed("600.00");
            Feed.SetState(Account, new AccountState(true, ConnectionState.Disconnected, "UsDollar"));
            Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, Guardian.Status.Kind);
            var unknown = LastEvent(Ev.AccountUnknown);
            Assert.Contains("Disconnected", ((JsonObject)unknown["payload"]).GetString("detail"));
        }

        [Fact]
        public void G16_an_account_that_comes_back_clears_the_unknown()
        {
            Armed("600.00");
            Feed.Remove(Account);
            Guardian.Tick();
            Assert.Equal(StateKind.FailClosed, Guardian.Status.Kind);

            Feed.SetState(Account, new AccountState(true, ConnectionState.Connected, "UsDollar"));
            Feed.SetPnl(Account, 0m, 0m);
            Guardian.Tick();

            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
        }

        [Fact]
        public void G16_arming_with_an_account_the_platform_does_not_know_is_rejected()
        {
            NewGuardian();
            var result = Guardian.Arm(Config(accounts: "[\"Sim101\",\"Ghost\"]"));

            Assert.False(result.Ok);
            Assert.Contains(result.Reasons, r => r.Contains("Ghost") && r.Contains("not known"));
            Assert.Equal(StateKind.Disarmed, Guardian.Status.Kind);   // rejection is not a lockout
        }

        [Fact]
        public void G16_arming_with_a_mismatched_currency_is_rejected()
        {
            NewGuardian();
            Feed.SetState(Account, new AccountState(true, ConnectionState.Connected, "Euro"));
            var result = Guardian.Arm(Config());

            Assert.False(result.Ok);
            Assert.Contains(result.Reasons, r => r.Contains("cross-currency"));
        }
    }
}
