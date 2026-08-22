// Which account a test bot is allowed to run on - the decision, with nothing else in it.
//
// THIS FILE MUST NEVER REFERENCE NINJATRADER. That is the whole point of it existing.
//
// The rule used to live inside BotSafety.VerifyAccount, which read Account.All - a NinjaTrader
// static. Deciding and consulting the world were the same code, so proving that the rail rejects a
// funded account would have required connecting a funded account. Here the caller supplies the
// facts, so every case can be asked in a unit test with no platform, no account and no connection -
// the same reason GuardianCore's tests run without any of those.
//
// THE RULE IS ABOUT CONNECTION, NOT PRESENCE, and that distinction was bought the hard way.
// On 2026-08-22 it was claimed that connecting the Simulated Data Feed removes the funded account
// from the session. A run disproved it the same day: with that feed connected and no "Simulation:"
// line anywhere in the log, Account.All still listed
//     [Backtest/Simulator, Playback101/Playback, Sim101/Simulator, 2127534/Provider31]
// NinjaTrader's mutual exclusion is between CONNECTIONS; Account.All enumerates every CONFIGURED
// account regardless. So "the funded account is present" is the normal state of a real trader's
// machine and cannot be the trigger - a rail that never lets anything run is a bot somebody deletes
// next week. What must stop a bot is a funded account that is REACHABLE.
//
// And an unknown connection state denies. That is the same fail-closed rule the guardian applies to
// its own unknowns: the optimistic default here would be "probably disconnected", which is exactly
// the plausible default this project refuses everywhere else.

using System;
using System.Collections.Generic;
using System.Linq;

namespace NinjaTrader.NinjaScript.AddOns.DeadmanGuardian
{
    /// <summary>What an account's connection is doing, as far as anyone can tell. `Unknown` is a real
    /// answer and is never collapsed into `Disconnected`.</summary>
    public enum ConnState { Connected, Disconnected, Unknown }

    /// <summary>The three facts that decide, and nothing else. No NinjaTrader types.</summary>
    public sealed class AccountFacts
    {
        public string Name { get; private set; }
        public string Provider { get; private set; }
        public ConnState Connection { get; private set; }

        public AccountFacts(string name, string provider, ConnState connection)
        {
            Name = name;
            Provider = provider;
            Connection = connection;
        }

        /// <summary>The exact shape the soak prints, so a mapping change is visible against a real run.
        /// Name/Provider/Connection - the third field was added on 2026-08-22 because the line was
        /// returning the same text whether or not the funded account was reachable, which made it
        /// useless for the one safety question it was being used to answer.</summary>
        public override string ToString()
        {
            return (Name ?? "?") + "/" + (Provider ?? "?") + "/" + Connection;
        }
    }

    public sealed class AccountVerdict
    {
        public bool Allowed { get; private set; }
        public string Chosen { get; private set; }
        public string Reason { get; private set; }

        private AccountVerdict(bool allowed, string chosen, string reason)
        {
            Allowed = allowed; Chosen = chosen; Reason = reason;
        }

        public static AccountVerdict Allow(string chosen) { return new AccountVerdict(true, chosen, null); }
        public static AccountVerdict Deny(string reason) { return new AccountVerdict(false, null, reason); }

        public override string ToString()
        {
            return Allowed ? ("ALLOW " + Chosen) : ("DENY: " + Reason);
        }
    }

    public static class BotAccountRule
    {
        /// <summary>The only provider a CONNECTED account may have while a test bot runs. Not a list of
        /// villains to recognise: anything else denies, including a broker nobody has heard of yet.</summary>
        public const string SafeProvider = "Simulator";

        /// <summary>Every denial names what it saw. A rail that refuses without saying which account
        /// stopped it is a rail somebody disables to get on with their day.</summary>
        public static AccountVerdict Decide(IReadOnlyList<AccountFacts> accounts, string target)
        {
            if (string.IsNullOrEmpty(target))
                return AccountVerdict.Deny("no target account name was given");

            if (accounts == null || accounts.Count == 0)
                return AccountVerdict.Deny("no accounts at all - the platform is not ready");

            // 1. Unknown first. An unknown connection is not a disconnection, and guessing which one it
            //    is would be the plausible default this project refuses.
            var unknown = accounts.Where(a => a != null && a.Connection == ConnState.Unknown).ToList();
            if (unknown.Count > 0)
                return AccountVerdict.Deny("connection state is UNKNOWN for " + Names(unknown) +
                                           " - unknown is not disconnected, so this fails closed");

            if (accounts.Any(a => a == null))
                return AccountVerdict.Deny("the account list contains a null entry");

            // 2. Anything reachable that is not a simulator stops the run. Presence is fine; reach is not.
            var live = accounts
                .Where(a => a.Connection == ConnState.Connected &&
                            !string.Equals(a.Provider, SafeProvider, StringComparison.Ordinal))
                .ToList();
            if (live.Count > 0)
                return AccountVerdict.Deny("a non-simulator account is CONNECTED: " + Names(live) +
                                           " - a bot that loses on purpose does not run while real money is reachable");

            // 3. Exactly one account by that name, matched ordinally. Sim101 and sim101 are not the same.
            var matches = accounts.Where(a => string.Equals(a.Name, target, StringComparison.Ordinal)).ToList();
            if (matches.Count == 0)
                return AccountVerdict.Deny("no account named '" + target + "' is present");
            if (matches.Count > 1)
                return AccountVerdict.Deny(matches.Count + " accounts are named '" + target + "' - refusing to guess which");

            var chosen = matches[0];

            if (!string.Equals(chosen.Provider, SafeProvider, StringComparison.Ordinal))
                return AccountVerdict.Deny("'" + target + "' has Provider=" + (chosen.Provider ?? "?") +
                                           ", not " + SafeProvider);

            if (chosen.Connection != ConnState.Connected)
                return AccountVerdict.Deny("'" + target + "' is " + chosen.Connection + ", so nothing can be sent to it");

            return AccountVerdict.Allow(chosen.Name);
        }

        /// <summary>The line the soak and the bots both print. ONE formatter, so a change in what is
        /// shown cannot drift between the two places that show it.</summary>
        public static string Describe(IEnumerable<AccountFacts> accounts)
        {
            if (accounts == null) return "Account.All = [unavailable]";
            return "Account.All = [" + string.Join(", ", accounts.Select(a => a == null ? "?" : a.ToString())) + "]";
        }

        private static string Names(IEnumerable<AccountFacts> accounts)
        {
            return string.Join(", ", accounts.Select(a => a.Name + "/" + a.Provider));
        }
    }
}
