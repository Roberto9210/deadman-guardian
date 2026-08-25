// Which account the adapter watches - the decision, extracted so it can be interrogated without
// NinjaTrader running. Same move as BotAccountRule and for the same reason: the code that decides
// and the code that consults the world must not be the same code, or proving the decision needs the
// platform in the state under test.
//
// M15, the defect this replaces: _guardedAccount was a hardcoded "Sim101" and the ONLY place that
// changed it was the arm path. SubscribeToAccount() runs at boot, before any arm - and on a restart
// with a restored ARMED seal the arm path never runs, so the adapter watched Sim101 whatever the
// seal said. Invisible on this machine because its config IS Sim101; broken from the first restart
// for anybody else. It degrades into M2 (fail-closed on the first realised dollar) or, worse, into
// M3 (an open position bleeding invisibly under an ARMED window).
//
// The rule: the guarded account comes from the SEALED CONFIG or it does not exist. No default, no
// literal, no memory of a previous answer. "We do not know yet" produces no subscription and says
// so - noisy and correct instead of silent and wrong.

using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.AddOns.DeadmanGuardian
{
    public sealed class GuardedAccountDecision
    {
        /// <summary>The account to subscribe to, or null when there is nothing safe to subscribe to.</summary>
        public string Account { get; private set; }

        /// <summary>Always present: what happened, for the adapter log - so an auditor can see which
        /// account every session actually watched, or why it watched none.</summary>
        public string Reason { get; private set; }

        public bool Subscribe { get { return Account != null; } }

        private GuardedAccountDecision(string account, string reason)
        {
            Account = account; Reason = reason;
        }

        public static GuardedAccountDecision Watch(string account, string reason)
        {
            return new GuardedAccountDecision(account, reason);
        }

        public static GuardedAccountDecision Nothing(string reason)
        {
            return new GuardedAccountDecision(null, reason);
        }

        public override string ToString()
        {
            return (Subscribe ? "WATCH " + Account : "NO SUBSCRIPTION") + " - " + Reason;
        }
    }

    public static class GuardedAccountRule
    {
        /// <summary>Decides from the sealed config's account list (Guardian.GuardedAccounts - null
        /// until a config is in force) and nothing else. There is deliberately no fallback parameter:
        /// a caller with a plausible default to offer is the caller this rule exists to refuse.</summary>
        public static GuardedAccountDecision Decide(IReadOnlyList<string> guardedAccounts)
        {
            if (guardedAccounts == null)
                return GuardedAccountDecision.Nothing(
                    "no configuration is in force yet - nothing to watch until Arm, and pretending " +
                    "otherwise is how a hardcoded default watches the wrong account");

            if (guardedAccounts.Count == 0)
                return GuardedAccountDecision.Nothing("the sealed configuration lists no accounts");

            if (guardedAccounts.Count > 1)
                // Unreachable while M16's refusal stands (multi-account configs cannot arm), kept as
                // its own answer rather than folded into "watch the first": if that refusal is ever
                // lifted, this rule must be REVISITED, not silently half-right.
                return GuardedAccountDecision.Nothing(
                    "the sealed configuration lists " + guardedAccounts.Count + " accounts; the " +
                    "adapter can watch one, and choosing silently would guard the rest only in part");

            var account = guardedAccounts[0];
            if (string.IsNullOrWhiteSpace(account))
                return GuardedAccountDecision.Nothing("the sealed configuration's account name is empty");

            return GuardedAccountDecision.Watch(account, "from the sealed configuration");
        }
    }
}
