// M15: which account does the adapter watch after a restart?
//
// The defect: _guardedAccount was a hardcoded "Sim101", overwritten only inside the arm path. A
// restart with a restored ARMED seal never arms, so the adapter watched Sim101 whatever the seal
// said - broken from the first restart for any trader whose account is not Sim101.
//
// HONESTY ABOUT RED-FIRST, because the requirement was "red before, green after": the defect lived in
// ADAPTER code that cannot execute in a unit test (it references NinjaTrader assemblies). There was no
// test that could have been written against the old code and gone red - the decision was a literal in
// an untestable file, which is itself part of the defect. What was done instead: the decision was
// EXTRACTED (GuardedAccountRule, pure) exactly as BotAccountRule was, and these tests assert the
// corrected behaviour through the seam that made it testable at all. The adapter keeps a five-line
// shell whose verification is its own log line ("guarded account (boot): ..."), which prints on every
// start - the same evidence-by-running pattern as the soak's Account.All line.
//
// The scenario of record, pinned end to end: a seal ARMED for account "X" is restored, nobody
// re-arms, and the decision comes out "watch X" - where the old code would have watched Sim101.

using System;
using System.Collections.Generic;
using GuardianCore;
using NinjaTrader.NinjaScript.AddOns.DeadmanGuardian;

namespace GuardianCore.Tests
{
    public class M15_GuardedAccountTests
    {
        // ---------------------------------------------------------------- the restore path, end to end

        /// <summary>The real M15 case: restored seal, no re-arm, account is NOT Sim101. Core must
        /// surface the sealed account, and the rule must decide to watch exactly that one. Under the
        /// old code the answer at this point was the literal "Sim101".</summary>
        [Fact]
        public void A_restored_seal_for_another_account_resolves_to_that_account_not_to_a_default()
        {
            var h = new Harness();
            h.Feed.SetState("X", new AccountState(true, ConnectionState.Connected, "UsDollar"));
            h.NewGuardian();
            var armed = h.Guardian.Arm(Harness.Config(accounts: "[\"X\"]"));
            Assert.True(armed.Ok, armed.ToString());

            // The process dies and comes back. Nobody arms again.
            h.Guardian.Stop();
            h.NewGuardian("run-2");

            Assert.Equal(StateKind.Armed, h.Guardian.Status.Kind);       // the seal survived
            var decision = GuardedAccountRule.Decide(h.Guardian.GuardedAccounts);

            Assert.True(decision.Subscribe);
            Assert.Equal("X", decision.Account);
            Assert.DoesNotContain("Sim101", decision.Account, StringComparison.Ordinal);
        }

        /// <summary>Condition (b): the sealed account not existing in NT8 is the ADAPTER's runtime
        /// problem (Accounts.Find returns null -> logged, no subscription), but the state the trader
        /// sees must not claim ARMED-and-watching. That is Core's existing SPEC 10 path: the feed
        /// reports the account unknown and entries block. Pinned here so the M15 rework cannot have
        /// loosened it.</summary>
        [Fact]
        public void A_sealed_account_the_platform_does_not_know_blocks_instead_of_claiming_armed()
        {
            var h = new Harness();
            h.Feed.SetState("X", new AccountState(true, ConnectionState.Connected, "UsDollar"));
            h.NewGuardian();
            Assert.True(h.Guardian.Arm(Harness.Config(accounts: "[\"X\"]")).Ok);

            h.Guardian.Stop();
            h.Feed.SetState("X", AccountState.Missing());               // renamed / gone at restart
            h.NewGuardian("run-2");
            h.Guardian.Tick();

            Assert.Equal(StateKind.FailClosed, h.Guardian.Status.Kind);
            Assert.False(h.Guardian.Status.EntriesAllowed);
        }

        // ---------------------------------------------------------------- M16: the refusal

        /// <summary>A config listing two accounts must not arm: nothing sealed, no state change, and
        /// the reason says the refusal is deliberate. Core's own plural handling stays (M1's check,
        /// the snapshot loop) as defence in depth for the day this is lifted.</summary>
        [Fact]
        public void M16_a_config_with_two_accounts_is_refused_at_arm_with_nothing_sealed()
        {
            var h = new Harness();
            h.Feed.SetState("A1", new AccountState(true, ConnectionState.Connected, "UsDollar"));
            h.Feed.SetState("A2", new AccountState(true, ConnectionState.Connected, "UsDollar"));
            h.NewGuardian();

            var result = h.Guardian.Arm(Harness.Config(accounts: "[\"A1\",\"A2\"]"));

            Assert.False(result.Ok);
            Assert.Contains(result.Reasons, r => r.Contains("only one is supported"));
            Assert.Contains(result.Reasons, r => r.Contains("deliberate"));
            Assert.Equal(StateKind.Disarmed, h.Guardian.Status.Kind);   // nothing sealed
            Assert.Null(h.Guardian.GuardedAccounts);                    // nothing to subscribe to
            Assert.False(h.Guardian.Status.Sealed);
        }

        // ---------------------------------------------------------------- GuardedAccounts itself

        /// <summary>Null until a config is in force - a real answer, distinct from an empty list. An
        /// adapter that subscribes on the strength of "guards nothing" when the truth is "does not
        /// know yet" is the M15 defect with different wiring.</summary>
        [Fact]
        public void Before_any_config_the_guarded_accounts_are_unknown_not_empty()
        {
            var h = new Harness();
            h.NewGuardian();

            Assert.Null(h.Guardian.GuardedAccounts);
        }

        [Fact]
        public void After_arming_the_guarded_accounts_are_the_sealed_ones()
        {
            var h = new Harness();
            h.Armed("600.00");

            Assert.NotNull(h.Guardian.GuardedAccounts);
            Assert.Equal(new[] { Harness.Account }, h.Guardian.GuardedAccounts);
        }

        // ---------------------------------------------------------------- the rule, exhaustively

        [Fact]
        public void No_config_means_no_subscription_and_says_why()
        {
            var d = GuardedAccountRule.Decide(null);

            Assert.False(d.Subscribe);
            Assert.Null(d.Account);
            Assert.Contains("no configuration", d.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void An_empty_list_means_no_subscription()
        {
            Assert.False(GuardedAccountRule.Decide(new List<string>()).Subscribe);
        }

        /// <summary>Unreachable while M16's refusal stands, kept as its own answer: if multi-account
        /// is ever allowed to arm, this rule must refuse rather than silently watch the first.</summary>
        [Fact]
        public void Two_accounts_refuse_rather_than_silently_watching_the_first()
        {
            var d = GuardedAccountRule.Decide(new List<string> { "A", "B" });

            Assert.False(d.Subscribe);
            Assert.Contains("2 accounts", d.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void A_blank_account_name_means_no_subscription()
        {
            Assert.False(GuardedAccountRule.Decide(new List<string> { " " }).Subscribe);
        }

        /// <summary>Condition (c): whichever way it goes, the decision explains itself - the adapter
        /// logs decision.ToString() verbatim, so this string IS the audit line.</summary>
        [Fact]
        public void Every_decision_carries_its_reason()
        {
            Assert.False(string.IsNullOrWhiteSpace(GuardedAccountRule.Decide(null).Reason));
            Assert.False(string.IsNullOrWhiteSpace(GuardedAccountRule.Decide(new List<string> { "X" }).Reason));
            Assert.Contains("WATCH X", GuardedAccountRule.Decide(new List<string> { "X" }).ToString(), StringComparison.Ordinal);
            Assert.Contains("NO SUBSCRIPTION", GuardedAccountRule.Decide(null).ToString(), StringComparison.Ordinal);
        }
    }
}
