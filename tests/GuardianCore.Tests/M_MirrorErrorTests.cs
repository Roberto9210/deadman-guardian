// The mirror error: the guardian acting when it should not.
//
// Everything tested until now checks that it ACTS when it must - 16 synthetic soak runs and one real
// run with fills. The opposite failure was never investigated, and it is the only open defect that can
// cost a user real money: flattening or blocking with a good position open.
//
// These tests do not fix anything. Each one REPRODUCES a scenario from docs/error_espejo.md so that a
// later fix has something to turn green, and so that the ones we choose not to fix are on record as
// decisions rather than oversights.
//
// Two of them already happened on a real machine on 2026-08-22, unprompted.

using System;
using System.Linq;
using GuardianCore;

namespace GuardianCore.Tests
{
    public class M_MirrorErrorTests
    {
        // ================================================================ M1: money
        //
        // The single worst path in the map: OnOrderObserved cancels on order.Account without ever
        // checking that account is one Core was asked to guard. The decision layer trusts its caller,
        // which is the exact inversion of how everything else here is built - and what it would cancel
        // is the worst thing to cancel, a protective stop on an account holding money.
        //
        // Today the only thing preventing it is a property of the ADAPTER (it subscribes to one
        // account). That is not a rule, it is a coincidence of wiring.

        /// <summary>FIXED 2026-08-22. This test asserted the defect until the fix landed, went red on
        /// the first build with it, and now asserts the corrected behaviour: an order on an account
        /// the guardian was not asked to guard produces ZERO broker calls and ONE ledger line.
        ///
        /// The ledger line is not decoration. Refusing in silence would hide that the wiring changed
        /// underneath us, and a foreign order arriving here means exactly that.</summary>
        [Fact]
        public void M1_A_foreign_order_produces_no_broker_call_and_one_ledger_line()
        {
            var h = new Harness();
            h.Armed("600.00");                       // config guards Sim101 and nothing else
            h.LoseExactly(600.00m);
            h.Guardian.Tick();
            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);

            h.Broker.Calls.Clear();

            h.Guardian.OnOrderObserved(new OrderSnapshot("9999999", "o-1", "ES 09-26", "Buy"));

            Assert.DoesNotContain(h.Broker.Calls, c => c.Contains("9999999"));
            Assert.Empty(h.Broker.Calls);            // not "no cancel on that account" - NO call at all

            var foreign = h.LastEvent(Ev.ForeignAccountOrderObserved);
            Assert.NotNull(foreign);
            var payload = (JsonObject)foreign["payload"];
            Assert.Equal("9999999", payload.GetString("account"));
            Assert.Equal("ES 09-26", payload.GetString("instrument"));
            Assert.Contains("Sim101", payload.GetString("guarded"), StringComparison.Ordinal);
        }

        /// <summary>And the guarded account still gets cancelled - the fix must not have turned the
        /// enforcement off along with the over-reach.</summary>
        [Fact]
        public void M1_A2_an_order_on_the_guarded_account_is_still_cancelled()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(600.00m);
            h.Guardian.Tick();
            h.Broker.Calls.Clear();

            h.Guardian.OnOrderObserved(new OrderSnapshot(Harness.Account, "o-2", Harness.Instrument, "Buy"));

            Assert.Contains(h.Broker.Calls, c => c.Contains(Harness.Account));
            Assert.Contains(Ev.OrderRejectedLocked, h.Events());
        }

        /// <summary>A Locked state whose sealed config no longer parses leaves Core unable to verify
        /// anything. Unable to confirm the account is ours is not permission to act on it.</summary>
        [Fact]
        public void M1_A3_with_no_config_to_check_against_nothing_reaches_the_broker()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(600.00m);
            h.Guardian.Tick();

            // A fresh process that restores LOCKED but cannot re-read the sealed config.
            h.Guardian.Stop();
            var corrupted = h.Store.ReadAllText(Harness.StatePath)
                .Replace("\"configSnapshot\":\"", "\"configSnapshot\":\"not json ");
            h.Store.WriteAtomic(Harness.StatePath, corrupted);
            h.NewGuardian("run-2");
            h.Broker.Calls.Clear();

            h.Guardian.OnOrderObserved(new OrderSnapshot(Harness.Account, "o-3", Harness.Instrument, "Buy"));

            Assert.Empty(h.Broker.Calls);
        }

        [Fact]
        public void M1b_The_guarded_account_is_the_only_one_that_ever_reaches_the_pnl_sum()
        {
            // The containment that DOES exist, pinned so a fix to M1 is not mistaken for this one:
            // a foreign account cannot influence the number that triggers a lockout.
            var h = new Harness();
            h.Armed("600.00");
            h.Feed.SetPnl("9999999", -100000m, 0m);   // a catastrophe on an account we do not guard
            h.Guardian.Tick();

            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);
        }

        // ================================================================ M2: observed on 2026-08-22
        //
        // Core's book is pure memory and is only cleared by ResetDay(). On a restart it starts at zero
        // while the platform still reports the session's realised P&L, so the SPEC 5.4 cross-check sees
        // a disagreement it cannot explain and fails closed - for the rest of the session, because only
        // the day roll clears it.
        //
        // Real instance: production guardian, 19:59Z, "core 0.00 vs platform -50.00 differ by 50.00,
        // tolerance 5.00", 103 consecutive PNL_DISAGREEMENT entries. The $50 was Bot A's.

        [Fact]
        public void M2_A_restart_after_a_realised_loss_blocks_entries_for_the_rest_of_the_day()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(50.00m);                    // Core sees the fills, platform agrees
            h.Guardian.Tick();
            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);

            // The process restarts. State and ledger survive on disk; the P&L book does not.
            h.Guardian.Stop();
            h.NewGuardian("run-2");
            h.Guardian.Arm(Harness.Config("600.00"));
            h.Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.Contains("SourcesDisagree", h.Guardian.Status.Reason, StringComparison.Ordinal);
            Assert.False(h.Guardian.Status.EntriesAllowed);

            // And nothing short of the day rolling clears it: the disagreement is still there.
            h.Guardian.Tick();
            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
        }

        // ================================================================ M3: the other direction
        //
        // The same root cause pointing the other way, and the more dangerous of the two: after a
        // restart Core holds no position, so HasOpenPosition is false, so the platform's unrealised is
        // never read - and DayLoss comes out zero while a real position bleeds. The window says ARMED.
        // It does not fail. It lies.

        [Fact]
        public void M3_A_restart_with_an_open_position_reports_zero_loss_while_the_position_bleeds()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.Guardian.Stop();

            // New process. Core's book is empty; the platform holds a position deep under water.
            h.NewGuardian("run-2");
            h.Guardian.Arm(Harness.Config("600.00"));
            h.Feed.SetPnl(Harness.Account, 0m, -800.00m);   // realised 0, unrealised -800
            h.Guardian.Tick();

            // -800 is past a 600 limit. The guardian does not notice, because it does not believe
            // there is a position to have unrealised P&L on.
            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);
            Assert.True(h.Guardian.Status.EntriesAllowed);
            Assert.DoesNotContain(Ev.LimitBreached, h.Events());
        }

        // ================================================================ M4: the laptop lid
        //
        // Reproduces the ARITHMETIC of a wake-from-sleep, not the sleep itself: wall clock forward an
        // hour, monotonic barely moved. Whether Windows' Stopwatch actually behaves this way across S3
        // is a property of the machine and has to be measured on one - see error_espejo.md.

        [Fact]
        public void M4_Waking_from_sleep_looks_like_a_forward_clock_jump_and_blocks_entries()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.Guardian.Tick();

            h.Clock.SetWallClockOnly(h.Clock.UtcNow.AddHours(1));
            h.Clock.AdvanceMonotonicOnly(TimeSpan.FromSeconds(2));
            h.Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.False(h.Guardian.Status.EntriesAllowed);
            Assert.Contains(Ev.ClockAnomaly, h.Events());
        }

        // ================================================================ M5: one bad tick
        //
        // A single evaluation without a price, while a position is open, is enough.

        [Fact]
        public void M5_One_tick_without_a_price_on_an_open_position_blocks_entries()
        {
            var h = new Harness();
            h.Armed("600.00");

            // Open a position Core can see, then take the price away.
            h.Feed.SetPnl(Harness.Account, 0m, 0m);
            h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                h.Clock.UtcNow, 5000m, 1, Side.Long, 0m, Harness.PointValue, "open-1"));
            h.Feed.SetPnl(Harness.Account, 0m, null);      // platform cannot price it this instant

            h.Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.False(h.Guardian.Status.EntriesAllowed);
            Assert.Contains(Ev.PnlUncomputable, h.Events());
        }

        // ================================================================ M6: observed, repeatedly
        //
        // Seen on this machine on both 21 and 22 August, several times.

        [Fact]
        public void M6_A_disconnection_blocks_entries_while_it_lasts()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.Guardian.Tick();
            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);

            h.Feed.SetState(Harness.Account, new AccountState(true, ConnectionState.Disconnected, "UsDollar"));
            h.Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.False(h.Guardian.Status.EntriesAllowed);
            Assert.Contains(Ev.AccountUnknown, h.Events());

            // It does clear on its own once the account comes back - unlike M2.
            h.Feed.SetState(Harness.Account, new AccountState(true, ConnectionState.Connected, "UsDollar"));
            h.Guardian.Tick();
            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);
        }

        // ================================================================ M7: improbable, pinned anyway
        //
        // Deduplication is CONDITIONAL: `if (ex.ExecutionId != null && ...)`. An execution with a null
        // id is counted every time it arrives. Whether NT8 ever emits one is unknown and cannot be
        // settled here - this only pins what Core does IF it happens, so the day somebody measures it
        // the consequence is already written down.

        [Fact]
        public void M7_An_execution_without_an_id_is_counted_twice_and_can_flatten_a_good_position()
        {
            var h = new Harness();
            h.Armed("600.00");

            // A round trip losing 400, delivered twice with no execution id.
            for (var i = 0; i < 2; i++)
            {
                h.Feed.SetPnl(Harness.Account, 0m, 0m);
                h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                    h.Clock.UtcNow, 5000m, 1, Side.Long, 0m, Harness.PointValue, null));
                h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                    h.Clock.UtcNow, 4920m, 1, Side.Short, 0m, Harness.PointValue, null));
            }

            // 400 twice is 800, past a 600 limit that a single delivery would never have reached.
            h.Feed.SetPnl(Harness.Account, -800.00m, 0m);
            h.Guardian.Tick();

            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);
            Assert.Contains(Ev.LimitBreached, h.Events());
        }

        [Fact]
        public void M13_The_same_execution_WITH_an_id_is_counted_once()
        {
            // The containment that does work, pinned so the M7 fix does not weaken it.
            var h = new Harness();
            h.Armed("600.00");

            for (var i = 0; i < 2; i++)
            {
                h.Feed.SetPnl(Harness.Account, 0m, 0m);
                h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                    h.Clock.UtcNow, 5000m, 1, Side.Long, 0m, Harness.PointValue, "in-1"));
                h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                    h.Clock.UtcNow, 4920m, 1, Side.Short, 0m, Harness.PointValue, "out-1"));
            }

            h.Feed.SetPnl(Harness.Account, -400.00m, 0m);
            h.Guardian.Tick();

            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);
        }
    }
}
