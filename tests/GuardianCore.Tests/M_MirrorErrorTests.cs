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

        /// <summary>REWRITTEN 2026-08-26 (A11). It used to assert that the guarded account still got
        /// cancelled - "the fix must not have turned the enforcement off along with the over-reach" -
        /// and LT-1 showed the over-reach and the enforcement were the same act. Cancelling blindly on
        /// observation killed the guardian's own flatten order and the trader's exits.
        ///
        /// What M1 was really about survives untouched and is what this asserts now: a FOREIGN account
        /// is refused loudly. M1_A already covers that; this one pins the guarded account's side of the
        /// boundary, which is now "observed, recorded by the events the lockout itself writes, and not
        /// acted upon".</summary>
        [Fact]
        public void M1_A2_the_guarded_account_is_observed_without_being_acted_upon()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(600.00m);
            h.Guardian.Tick();
            h.Broker.Calls.Clear();

            h.Guardian.OnOrderObserved(new OrderSnapshot(Harness.Account, "o-2", Harness.Instrument, "Buy"));

            Assert.Empty(h.Broker.Calls);
            // Nor is it treated as foreign: the account IS guarded, and saying otherwise would be a
            // different lie in the ledger.
            Assert.DoesNotContain(Ev.ForeignAccountOrderObserved, h.Events());
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

        /// <summary>FIXED (Option A). This test asserted the defect - FailClosed for the rest of the
        /// session - and went RED on the first build with the restart baseline, exactly as the method
        /// requires. It now asserts the corrected behaviour: the platform's figure, corroborated
        /// against this guardian's own last same-day checkpoint, is adopted and the guardian returns
        /// to ARMED with the day's loss intact - not reset, not forgotten.</summary>
        [Fact]
        public void M2_A_restart_after_a_realised_loss_readopts_the_loss_and_returns_to_armed()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(50.00m);
            h.Clock.Advance(TimeSpan.FromMinutes(6));  // let the interval checkpoint record the -50
            h.Guardian.Tick();
            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);

            // The process restarts. State and ledger survive on disk; the P&L book does not.
            h.Guardian.Stop();
            h.NewGuardian("run-2");
            h.Guardian.Tick();

            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);
            Assert.True(h.Guardian.Status.EntriesAllowed);

            var adopted = h.LastEvent(Ev.PnlBaselineAdopted);
            Assert.NotNull(adopted);
            var payload = (JsonObject)adopted["payload"];
            Assert.Equal("-50.00", payload.GetString("platform"));
            Assert.Equal("-50.00", payload.GetString("coreCheckpoint"));
            Assert.Equal("-50.00", payload.GetString("adopted"));

            // The loss is BACK in the day's arithmetic: another 550 must now reach the 600 limit.
            // (A guardian that had quietly reset to zero would need the full 600 again.)
            h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                h.Clock.UtcNow, 5000m, 1, Side.Long, 0m, Harness.PointValue, "in-r2"));
            h.Feed.SetPnl(Harness.Account, -600.00m, 0m);
            h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                h.Clock.UtcNow, 4890m, 1, Side.Short, 0m, Harness.PointValue, "out-r2"));
            h.Guardian.Tick();
            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);
        }

        // ================================================================ M3: the other direction
        //
        // The same root cause pointing the other way, and the more dangerous of the two: after a
        // restart Core holds no position, so HasOpenPosition is false, so the platform's unrealised is
        // never read - and DayLoss comes out zero while a real position bleeds. The window says ARMED.
        // It does not fail. It lies.

        /// <summary>FIXED TWICE, and the second time is the interesting one.
        ///
        /// It first asserted M3's own defect - ARMED while a position bled unseen - and was rewritten
        /// when Option A landed. It then went RED again with the M22 fix, because the scenario it had
        /// been given by then (baseline 0, -800 unrealised) IS M22a: the guardian now closes that
        /// position rather than blocking. Rather than let two tests own one scenario, this one keeps
        /// what is M3's alone: after a restart, an open position's unrealised loss is COUNTED again.
        /// A loss well under the limit isolates that claim from the breach path entirely, so a future
        /// change to when the guardian flattens cannot turn this test red for an unrelated reason.
        ///
        /// The number the old code reported in this exact situation was 0.00, while the trader was
        /// down 300 dollars and the window said ARMED.</summary>
        [Fact]
        public void M3_A_restart_no_longer_hides_an_open_positions_unrealised_loss()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.Clock.Advance(TimeSpan.FromMinutes(6));
            h.Guardian.Tick();                               // checkpoint: realised 0, on record
            h.Guardian.Stop();

            // New process. The platform still holds the position, 300 under water.
            h.Broker.SetPosition(Harness.Account, Harness.Instrument, 1, 5000m);
            h.Feed.SetPnl(Harness.Account, 0m, -300.00m);
            h.NewGuardian("run-2");
            h.Guardian.Tick();                               // adopts the baseline and the position
            h.Clock.Advance(TimeSpan.FromMinutes(6));
            h.Guardian.Tick();                               // and now a checkpoint carrying the figure

            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);

            var adopted = h.LastEvent(Ev.PnlBaselineAdopted);
            Assert.NotNull(adopted);
            Assert.Equal(1, (int)(((JsonObject)adopted["payload"]).GetInt("positionsAdopted") ?? 0));

            var checkpoint = h.LastEvent(Ev.PnlCheckpoint);
            Assert.NotNull(checkpoint);
            Assert.Equal("300.00", ((JsonObject)checkpoint["payload"]).GetString("dayLoss"));

            // Nothing was closed, and nothing should have been: 300 is not a breach of 600.
            Assert.DoesNotContain(h.Broker.Calls, c => c.StartsWith("flatten:", StringComparison.Ordinal));
        }

        // ================================================================ the three conditions

        /// <summary>CONDITION 1, end to end. A baseline adopted at the limit BLOCKS and does not
        /// flatten - and the moment a real fill arrives, the same breach flattens for real. The two
        /// halves in one test, because the boundary between them IS the condition.</summary>
        [Fact]
        public void C1_A_baseline_at_the_limit_blocks_without_flattening_until_a_real_fill_arrives()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(598.00m);
            h.Clock.Advance(TimeSpan.FromMinutes(6));
            h.Guardian.Tick();                               // checkpoint: -598
            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);
            h.Guardian.Stop();

            // While nobody was running, the platform figure moved to -602: past the limit, but within
            // tolerance of the checkpoint, so the period is corroborated and the WORSE figure adopted.
            h.Feed.SetPnl(Harness.Account, -602.00m, 0m);
            h.NewGuardian("run-2");
            h.Broker.Calls.Clear();
            h.Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.False(h.Guardian.Status.EntriesAllowed);
            Assert.Empty(h.Broker.Calls);                    // no flatten, no cancel - NOTHING
            Assert.Contains(Ev.LimitBreachedBaselineOnly, h.Events());
            Assert.DoesNotContain(Ev.LimitBreached, h.Events());

            // A second tick must not flap: still blocked, no second event.
            h.Guardian.Tick();
            Assert.Equal(1, h.Events().Count(e => e == Ev.LimitBreachedBaselineOnly));

            // Now a REAL fill is observed. The same breach may now flatten - and does.
            h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                h.Clock.UtcNow, 5000m, 1, Side.Long, 0m, Harness.PointValue, "in-c1"));
            h.Feed.SetPnl(Harness.Account, -612.00m, 0m);
            h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                h.Clock.UtcNow, 4950m, 1, Side.Short, 0m, Harness.PointValue, "out-c1"));
            h.Guardian.Tick();

            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);
            Assert.Contains(Ev.LimitBreached, h.Events());
            Assert.Contains(h.Broker.Calls, c => c.StartsWith("flatten:", StringComparison.Ordinal));
        }

        /// <summary>CONDITION 2. Within tolerance the two figures may still differ; the one that
        /// leaves the trader CLOSER to the limit is adopted, and both go to the ledger with their
        /// source - never the friendlier number, never silently.</summary>
        [Fact]
        public void C2_When_the_figures_differ_the_more_conservative_is_adopted_and_both_are_recorded()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(50.00m);
            h.Clock.Advance(TimeSpan.FromMinutes(6));
            h.Guardian.Tick();                               // checkpoint: -50
            h.Guardian.Stop();

            h.Feed.SetPnl(Harness.Account, -52.00m, 0m);     // platform is worse, within tolerance
            h.NewGuardian("run-2");
            h.Guardian.Tick();

            var payload = (JsonObject)h.LastEvent(Ev.PnlBaselineAdopted)["payload"];
            Assert.Equal("-50.00", payload.GetString("coreCheckpoint"));
            Assert.Equal("-52.00", payload.GetString("platform"));
            Assert.Equal("-52.00", payload.GetString("adopted"));
            Assert.Contains("conservative", payload.GetString("why"), StringComparison.Ordinal);
            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);
        }

        [Fact]
        public void C2b_The_conservative_choice_works_in_the_other_direction_too()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(50.00m);
            h.Clock.Advance(TimeSpan.FromMinutes(6));
            h.Guardian.Tick();
            h.Guardian.Stop();

            h.Feed.SetPnl(Harness.Account, -47.00m, 0m);     // platform is FRIENDLIER - not adopted
            h.NewGuardian("run-2");
            h.Guardian.Tick();

            var payload = (JsonObject)h.LastEvent(Ev.PnlBaselineAdopted)["payload"];
            Assert.Equal("-50.00", payload.GetString("adopted"));
            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);
        }

        /// <summary>CONDITION 3. NT8 exposes nothing that states the period of its realised figure
        /// (verified by reflection: bare numbers, no "since when"), so the only establishment is
        /// agreement with this guardian's own same-day checkpoint. Beyond tolerance, fills-while-dead
        /// and a platform session reset are indistinguishable - so nothing is adopted, and the reason
        /// says exactly that.</summary>
        [Fact]
        public void C3_A_figure_that_moved_beyond_tolerance_while_dead_is_refused_not_adopted()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(50.00m);
            h.Clock.Advance(TimeSpan.FromMinutes(6));
            h.Guardian.Tick();                               // checkpoint: -50
            h.Guardian.Stop();

            h.Feed.SetPnl(Harness.Account, -80.00m, 0m);     // moved 30 while nobody was watching
            h.NewGuardian("run-2");
            h.Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.Contains("indistinguishable", h.Guardian.Status.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(Ev.PnlBaselineAdopted, h.Events());

            var payload = (JsonObject)h.LastEvent(Ev.PnlBaselineRefused)["payload"];
            Assert.Equal("-50.00", payload.GetString("coreCheckpoint"));
            Assert.Equal("-80.00", payload.GetString("platform"));

            // Still refused on the next tick, and the refusal is logged exactly once.
            h.Guardian.Tick();
            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.Equal(1, h.Events().Count(e => e == Ev.PnlBaselineRefused));
        }

        [Fact]
        public void C3b_A_platform_figure_with_no_same_day_checkpoint_to_corroborate_is_refused()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(50.00m);
            h.Guardian.Stop();                               // no 5-minute advance: no checkpoint

            h.NewGuardian("run-2");
            h.Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.Contains("no same-day checkpoint", h.Guardian.Status.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(Ev.PnlBaselineAdopted, h.Events());
        }

        /// <summary>Surfaced by the pre-F5 contingency question on 2026-08-25: a refused baseline
        /// must not HAUNT the next fresh arm. The pending flag was set at restore and cleared only by
        /// the day ROLL - but an expiry does not roll the day: it disarms, and Arm() then sets the new
        /// dayKey directly, so RollDayIfNeeded never fires and the stale pending re-evaluated a
        /// baseline on a day that was never restored. A fresh arm has a fresh book; there is nothing
        /// to adopt, and any disagreement with the platform belongs to the ordinary cross-check with
        /// its own reason - not to a restart that did not happen.</summary>
        [Fact]
        public void C3d_A_refused_baseline_does_not_haunt_the_next_fresh_arm()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(50.00m);
            h.Guardian.Stop();                        // no checkpoint written: restore will refuse

            h.NewGuardian("run-2");
            h.Guardian.Tick();
            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.Contains("restart baseline refused", h.Guardian.Status.Reason, StringComparison.Ordinal);

            // The day ends: the seal expires and the guardian disarms.
            h.Clock.Advance(TimeSpan.FromHours(3));
            h.Guardian.Tick();
            Assert.Equal(StateKind.Disarmed, h.Guardian.Status.Kind);

            // A fresh arm on the new day. The platform still reports the old figure - whether that is
            // acceptable is the ordinary cross-check's question, with its own reason. What may NOT
            // happen is a "restart baseline" verdict on a day that was never restored.
            var result = h.Guardian.Arm(Harness.Config("600.00"));
            Assert.True(result.Ok, result.ToString());
            h.Guardian.Tick();

            Assert.DoesNotContain("restart baseline refused", h.Guardian.Status.Reason ?? "", StringComparison.Ordinal);
            Assert.Equal(1, h.Events().Count(e => e == Ev.PnlBaselineRefused));   // only the pre-arm one
        }

        // ================================================================ M22: the proxy in condition 1
        //
        // Condition 1 says "an ADOPTED baseline may block but never flatten". The code says
        // "!HasObservedFill", which is a PROXY for that - and a wider one, because TotalDayLoss is not
        // made of adopted figures alone:
        //
        //     DayPnl = GrossRealized + Unrealized - Commissions
        //
        // GrossRealized may come from an adopted baseline. Unrealized comes LIVE from the platform on
        // every tick and was never adopted - adopting a position decides only whether it is READ, not
        // where its value comes from. So a breach carried entirely by a live, moving loss is refused a
        // flatten on the grounds that an adopted figure caused it, when the adopted figure may be zero.
        //
        // It is M3 in a narrow form, reintroduced by the fix for M3.

        /// <summary>FIXED. Asserted the defect, went RED on the first build with the fix, rewritten
        /// here to assert the corrected behaviour.
        ///
        /// Restart mid-morning: the platform reports zero realised and there is no same-day checkpoint,
        /// so the trivially-established branch adopts a baseline of ZERO, and the open position is
        /// adopted. It then bleeds past the limit. The old gate refused to flatten, recording that the
        /// limit was reached "on adopted figures alone" - over a figure of 0.00, which cannot have
        /// caused anything. Now the observed loss reaches the limit by itself, so the position is
        /// closed.</summary>
        [Fact]
        public void M22a_A_zero_baseline_cannot_excuse_leaving_a_bleeding_position_open()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.Guardian.Stop();                                   // no checkpoint: adoption takes the p==0 branch

            h.Broker.SetPosition(Harness.Account, Harness.Instrument, 1, 5000m);
            h.Feed.SetPnl(Harness.Account, 0m, -700.00m);        // realised 0, unrealised -700, limit 600
            h.NewGuardian("run-2");
            h.Guardian.Tick();

            var adopted = h.LastEvent(Ev.PnlBaselineAdopted);
            Assert.NotNull(adopted);
            Assert.Equal("0.00", ((JsonObject)adopted["payload"]).GetString("adopted"));

            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);
            Assert.Contains(Ev.LimitBreached, h.Events());
            Assert.DoesNotContain(Ev.LimitBreachedBaselineOnly, h.Events());
            Assert.Contains(h.Broker.Calls, c => c.StartsWith("flatten:", StringComparison.Ordinal));
        }

        /// <summary>THE CONTAINMENT. Asserts CORRECT behaviour and must stay GREEN after the fix - if it
        /// goes red, the fix took condition 1 with it.
        ///
        /// Here the adopted baseline really does carry the breach: -500 adopted plus -200 live is -700
        /// against a 600 limit, and removing the adopted part leaves 200, which breaches nothing. Block,
        /// and do not flatten - exactly as Roberto's condition 1 requires.</summary>
        [Fact]
        public void M22b_A_load_bearing_adopted_baseline_still_never_flattens()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(500.00m);
            h.Clock.Advance(TimeSpan.FromMinutes(6));
            h.Guardian.Tick();                                   // checkpoint: -500
            h.Guardian.Stop();

            h.Broker.SetPosition(Harness.Account, Harness.Instrument, 1, 5000m);
            h.Feed.SetPnl(Harness.Account, -500.00m, -200.00m);
            h.NewGuardian("run-2");
            h.Guardian.Tick();

            var adopted = h.LastEvent(Ev.PnlBaselineAdopted);
            Assert.NotNull(adopted);
            Assert.Equal("-500.00", ((JsonObject)adopted["payload"]).GetString("adopted"));

            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.Contains(Ev.LimitBreachedBaselineOnly, h.Events());
            Assert.DoesNotContain(h.Broker.Calls, c => c.StartsWith("flatten:", StringComparison.Ordinal));
        }

        /// <summary>FIXED. Asserted the defect, went RED with the fix, rewritten.
        ///
        /// The live loss breaches ON ITS OWN: -650 unrealised against a 600 limit, with only -100
        /// adopted. Take the adopted part away entirely and the limit is still crossed - so the reason
        /// for not flattening never existed, and the position is now closed.</summary>
        [Fact]
        public void M22c_A_live_loss_that_breaches_on_its_own_must_flatten_even_after_a_restart()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.LoseExactly(100.00m);
            h.Clock.Advance(TimeSpan.FromMinutes(6));
            h.Guardian.Tick();                                   // checkpoint: -100
            h.Guardian.Stop();

            h.Broker.SetPosition(Harness.Account, Harness.Instrument, 1, 5000m);
            h.Feed.SetPnl(Harness.Account, -100.00m, -650.00m);
            h.NewGuardian("run-2");
            h.Guardian.Tick();

            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);
            Assert.Contains(Ev.LimitBreached, h.Events());
            Assert.DoesNotContain(Ev.LimitBreachedBaselineOnly, h.Events());
            Assert.Contains(h.Broker.Calls, c => c.StartsWith("flatten:", StringComparison.Ordinal));
        }

        /// <summary>FIXED, and it is the sign trap. Asserted the defect, went RED with the fix.
        ///
        /// The adopted baseline is a PROFIT this guardian never saw: +300 realised before the restart,
        /// against -900 unrealised now. The total is -600, exactly the limit, so a breach. Take the
        /// adopted part away and the live loss is NINE HUNDRED - LARGER than the total.
        ///
        /// This is the only shape where observed loss exceeds total loss, and therefore the only one
        /// that catches an inverted sign in the subtraction: with losing baselines both figures move
        /// the same way and a flipped sign hides inside a smaller number that still looks plausible.
        /// Here a flipped sign gives 300 instead of 900 - under the limit - and the position that most
        /// needs closing would be the one left open. The assertion below is what makes that visible:
        /// with the subtraction the right way round the observed loss is 900 and the guardian acts.</summary>
        [Fact]
        public void M22d_A_profitable_adopted_baseline_does_not_suppress_a_live_breach()
        {
            var h = new Harness();
            h.Armed("600.00");

            // A round trip that GAINS 300: long at 5000, out at 5060, $5 a point.
            h.Feed.SetPnl(Harness.Account, 0m, 0m);
            h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                h.Clock.UtcNow, 5000m, 1, Side.Long, 0m, Harness.PointValue, "in-gain"));
            h.Feed.SetPnl(Harness.Account, 300.00m, 0m);
            h.Guardian.OnExecution(new ExecutionRecord(Harness.Account, Harness.Instrument,
                h.Clock.UtcNow, 5060m, 1, Side.Short, 0m, Harness.PointValue, "out-gain"));

            h.Clock.Advance(TimeSpan.FromMinutes(6));
            h.Guardian.Tick();                                   // checkpoint: +300
            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);
            h.Guardian.Stop();

            // Restart. The profit is re-adopted; a new position is now deep under water.
            h.Broker.SetPosition(Harness.Account, Harness.Instrument, 1, 5000m);
            h.Feed.SetPnl(Harness.Account, 300.00m, -900.00m);
            h.NewGuardian("run-2");
            h.Guardian.Tick();

            var adopted = h.LastEvent(Ev.PnlBaselineAdopted);
            Assert.NotNull(adopted);
            Assert.Equal("300.00", ((JsonObject)adopted["payload"]).GetString("adopted"));

            Assert.Equal(StateKind.Locked, h.Guardian.Status.Kind);
            Assert.Contains(Ev.LimitBreached, h.Events());
            Assert.DoesNotContain(Ev.LimitBreachedBaselineOnly, h.Events());
            Assert.Contains(h.Broker.Calls, c => c.StartsWith("flatten:", StringComparison.Ordinal));
        }

        /// <summary>An adopted position whose entry price the platform cannot state refuses the whole
        /// adoption: every later closing fill's realised P&L would be garbage, and a guessed entry
        /// price is the plausible default this project refuses everywhere.</summary>
        [Fact]
        public void C3c_A_position_without_an_average_price_refuses_the_adoption()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.Clock.Advance(TimeSpan.FromMinutes(6));
            h.Guardian.Tick();
            h.Guardian.Stop();

            h.Broker.SetPosition(Harness.Account, Harness.Instrument, 1);   // no average price
            h.Feed.SetPnl(Harness.Account, 0m, -100.00m);
            h.NewGuardian("run-2");
            h.Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.Contains("average price", h.Guardian.Status.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(Ev.PnlBaselineAdopted, h.Events());
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
