// The bots' account rail, asked every question it exists to answer - with no platform, no account
// and no connection.
//
// This suite exists because of a specific impossibility. The rail used to read Account.All, a
// NinjaTrader static, so proving that it refuses a funded account would have required connecting a
// funded account. The decision now lives in a pure file (nt/bots/BotAccountRule.cs, compiled in by
// link) and every case below is a list somebody typed.
//
// The real account number is NOT here and never will be. This repository is public; the certificate
// salts account names per installation precisely so nobody can correlate a trader across machines.
// 9999999/Provider31 has the identical SHAPE - a name that is not Sim101 with a provider that is not
// Simulator - so the regression value is the same and nothing leaks.

using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.DeadmanGuardian;

namespace GuardianCore.Tests
{
    public class Bots_AccountRuleTests
    {
        private const string Target = "Sim101";

        private static AccountFacts Sim(string name = Target, ConnState c = ConnState.Connected) =>
            new AccountFacts(name, "Simulator", c);

        private static AccountFacts Funded(ConnState c) => new AccountFacts("9999999", "Provider31", c);

        private static AccountVerdict Decide(params AccountFacts[] accounts) =>
            BotAccountRule.Decide(new List<AccountFacts>(accounts), Target);

        // ---------------------------------------------------------------- the two that decide

        /// <summary>The case that changed sign. It used to be "both present => allow and choose
        /// Sim101"; choosing correctly is a check, and a check is a weaker guarantee than the money
        /// not being reachable. A bot built to lose does not run while a funded account is live.</summary>
        [Fact]
        public void A_connected_funded_account_stops_the_run_even_though_Sim101_is_fine()
        {
            var v = Decide(Sim(), Funded(ConnState.Connected));

            Assert.False(v.Allowed);
            Assert.Contains("CONNECTED", v.Reason);
            Assert.Contains("9999999", v.Reason);      // the denial names what it saw
            Assert.Null(v.Chosen);                     // and does NOT fall back to picking Sim101
        }

        /// <summary>Its mandatory partner. Without this the rule could be broken in the way that makes
        /// it useless - refusing always - and every other test would still pass. The funded account is
        /// listed on any real trader's machine whether or not it is reachable; that alone must not
        /// stop anything, or the bots never run and somebody deletes the rail.</summary>
        [Fact]
        public void A_funded_account_that_is_merely_listed_does_not_stop_anything()
        {
            var v = Decide(Sim(), Funded(ConnState.Disconnected));

            Assert.True(v.Allowed);
            Assert.Equal(Target, v.Chosen);
        }

        /// <summary>The real shape of this machine: four accounts, three of them irrelevant.</summary>
        [Fact]
        public void The_healthy_session_of_a_real_machine_is_allowed()
        {
            var v = Decide(
                new AccountFacts("Backtest", "Simulator", ConnState.Disconnected),
                new AccountFacts("Playback101", "Playback", ConnState.Disconnected),
                Sim(),
                Funded(ConnState.Disconnected));

            Assert.True(v.Allowed);
            Assert.Equal(Target, v.Chosen);
        }

        // ---------------------------------------------------------------- unknown is not disconnected

        /// <summary>The optimistic default this project refuses everywhere else. "We could not read the
        /// connection" must never become "it is probably disconnected", because the account it would be
        /// guessing about is the one holding real money.</summary>
        [Fact]
        public void An_unknown_connection_state_denies_rather_than_being_assumed_disconnected()
        {
            var v = Decide(Sim(), Funded(ConnState.Unknown));

            Assert.False(v.Allowed);
            Assert.Contains("UNKNOWN", v.Reason);
            Assert.Contains("9999999", v.Reason);
        }

        /// <summary>Even when the unknown one is a simulator: unknown is unknown.</summary>
        [Fact]
        public void An_unknown_state_denies_whichever_account_it_belongs_to()
        {
            var v = Decide(Sim(), new AccountFacts("Backtest", "Simulator", ConnState.Unknown));

            Assert.False(v.Allowed);
            Assert.Contains("UNKNOWN", v.Reason);
        }

        // ---------------------------------------------------------------- the allowlist is positive

        /// <summary>Not a list of villains to recognise. A provider nobody has heard of denies for the
        /// same reason the funded one does, without anyone having to add it anywhere.</summary>
        [Fact]
        public void A_connected_provider_nobody_declared_denies_without_being_recognised()
        {
            var v = Decide(Sim(), new AccountFacts("SomeBroker", "Provider99", ConnState.Connected));

            Assert.False(v.Allowed);
            Assert.Contains("Provider99", v.Reason);
        }

        [Fact]
        public void A_connected_playback_account_is_not_a_simulator_and_denies()
        {
            var v = Decide(Sim(), new AccountFacts("Playback101", "Playback", ConnState.Connected));

            Assert.False(v.Allowed);
            Assert.Contains("Playback", v.Reason);
        }

        // ---------------------------------------------------------------- the target itself

        [Fact]
        public void The_funded_account_alone_denies()
        {
            var v = Decide(Funded(ConnState.Disconnected));

            Assert.False(v.Allowed);
            Assert.Contains(Target, v.Reason);
        }

        [Fact]
        public void The_right_name_with_the_wrong_provider_denies()
        {
            var v = Decide(new AccountFacts(Target, "Provider31", ConnState.Connected));

            Assert.False(v.Allowed);
            Assert.Contains("Provider31", v.Reason);
        }

        /// <summary>Ordinal and exact. Sim1010 is a different account and sim101 is a different
        /// account, and a rail that accepted either would be one typo from the wrong platform.</summary>
        [Theory]
        [InlineData("Sim1010")]
        [InlineData("sim101")]
        [InlineData("Sim101 ")]
        public void A_name_that_merely_looks_like_the_target_denies(string name)
        {
            var v = Decide(Sim(name));

            Assert.False(v.Allowed);
            Assert.Contains(Target, v.Reason);
        }

        [Fact]
        public void Two_accounts_with_the_target_name_deny_rather_than_guessing()
        {
            var v = Decide(Sim(), Sim());

            Assert.False(v.Allowed);
            Assert.Contains("refusing to guess", v.Reason);
        }

        [Fact]
        public void The_target_disconnected_denies_because_nothing_can_be_sent_to_it()
        {
            var v = Decide(Sim(Target, ConnState.Disconnected));

            Assert.False(v.Allowed);
            Assert.Contains("Disconnected", v.Reason);
        }

        [Fact]
        public void An_empty_or_absent_list_denies_and_says_the_platform_is_not_ready()
        {
            Assert.False(BotAccountRule.Decide(new List<AccountFacts>(), Target).Allowed);
            Assert.False(BotAccountRule.Decide(null, Target).Allowed);
        }

        // ---------------------------------------------------------------- the printed line

        /// <summary>The soak prints this on every run, and it is the only verification the NinjaTrader
        /// mapping gets. Until 2026-08-22 it carried Name/Provider only, so it read identically whether
        /// or not the funded account was reachable - while being used to answer exactly that. Pinning
        /// the format here means a change to it has to come past a test.</summary>
        [Fact]
        public void The_printed_line_carries_all_three_fields_including_connection()
        {
            var line = BotAccountRule.Describe(new List<AccountFacts> { Sim(), Funded(ConnState.Disconnected) });

            Assert.Equal("Account.All = [Sim101/Simulator/Connected, 9999999/Provider31/Disconnected]", line);
        }

        [Fact]
        public void The_printed_line_survives_an_unreadable_account_without_inventing_one()
        {
            var line = BotAccountRule.Describe(new List<AccountFacts> { null!, Sim() });

            Assert.Equal("Account.All = [?, Sim101/Simulator/Connected]", line);
        }
    }
}
