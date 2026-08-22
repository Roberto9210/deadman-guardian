// Every sentence a human reads from this product, in one place.
//
// AMENDMENTS A10: the strings a user reads are single-source, consumed by every surface that shows
// them - the status window and the NinjaTrader Log take THE SAME string, not similar ones. That
// amendment exists because an overclaim was once found living one paragraph below its own
// correction: two copies never diverge together, one gets fixed and the other does not, so the
// survivor is by construction the stale one, waiting on the worst day its reader will have.
//
// They live in GuardianCore, with no NinjaTrader reference, for a second reason: these are product
// CLAIMS. "You cannot trade until 17:00" was one of them and it was false - the guardian detects and
// cancels, it does not physically prevent an order, and SPEC section 17 says so two documents away.
// A claim that can be wrong needs a test, and a test needs the string to be reachable without a
// platform.
//
// Written in English because that is the language NinjaTrader writes the rest of its Log in, and a
// message that switches language halfway down a log is a message people stop reading.

using System;
using System.Globalization;

namespace GuardianCore
{
    public static class Messages
    {
        // ---------------------------------------------------------------- headlines
        //
        // "NOT PROTECTED" used to serve both FailClosed and Disarmed. They are opposites: in
        // fail-closed the seal is alive, the guardian IS armed and IS blocking entries, and all that
        // is missing is sight of the account. In disarmed nothing is armed and nobody is watching.
        // Worse, for fail-closed the old wording misled toward the dangerous side - it suggested
        // nothing was operating when something was. A real person went looking for the Arm button
        // because of it (2026-08-22).

        public const string HeadlineArmed = "ARMED";
        public const string HeadlineLocked = "LOCKED";
        public const string HeadlineCannotSee = "CANNOT SEE YOUR ACCOUNT";
        public const string HeadlineNotArmed = "NOT ARMED";

        public static string Headline(StateKind kind)
        {
            switch (kind)
            {
                case StateKind.Armed: return HeadlineArmed;
                case StateKind.Locked: return HeadlineLocked;
                case StateKind.FailClosed: return HeadlineCannotSee;
                default: return HeadlineNotArmed;
            }
        }

        // ---------------------------------------------------------------- details
        //
        // The detail line has to say WHAT TO DO. "Blocked, state unknown: AccountUnknown on Sim101:
        // account is Disconnected" is precise and useless: it describes internal state and offers no
        // next step.

        public static string DetailArmed(string account)
        {
            return "Watching " + Safe(account) + ". Entries allowed.";
        }

        public static string DetailLocked(string account, string until)
        {
            return "Daily limit reached on " + Safe(account) + ". Any new order will be cancelled" +
                   UntilClause(until) + ".";
        }

        /// <summary>Fail-closed. <paramref name="hasSeal"/> decides the last sentence and it is NOT
        /// decoration: FailClosed does not always have a seal - StartCorrupt enters it with none - so
        /// "you are still armed" cannot be asserted without looking. Each variant is true in the
        /// moment it is written, which is the same discipline the ledger has.</summary>
        public static string DetailCannotSee(string reason, bool hasSeal, string until)
        {
            var head = string.IsNullOrEmpty(reason)
                ? "I cannot read your account, so I cannot see your P&L and cannot hold your limit."
                : "I cannot read your account (" + reason + "), so I cannot see your P&L and cannot hold your limit.";

            var meanwhile = " Meanwhile I am not letting new positions open.";
            var fix = " Connect the feed under Connections and the guardian comes back on its own - nothing needs restarting.";

            var arm = hasSeal
                ? " You are still armed: your limit holds" + UntilClause(until) +
                  ". That is why there is no Arm button - there is nothing to arm, the connection is what is missing."
                : " Nothing is armed yet. The Arm button will appear once I can see the account.";

            return head + meanwhile + fix + arm;
        }

        public static string DetailNotArmed(string configPath)
        {
            return "Nothing is being watched and no limit is in force. Press Arm to start." +
                   (string.IsNullOrEmpty(configPath) ? "" : " Config: " + configPath);
        }

        // ---------------------------------------------------------------- the lockout, in two parts
        //
        // TWO messages, because one cannot be true twice. The Log is read downwards, so the
        // explanation has to reach it before NinjaTrader's own "Disabling NinjaScript strategy" -
        // but a message written BEFORE the broker is touched cannot say "I cancelled 3 orders and
        // closed your positions", and if the cancel partially fails the record would be asserting
        // something false in a file sold as evidence.
        //
        // So: one at LIMIT_BREACHED, in the future tense, when the breach is known and nothing has
        // been touched. One after FLATTEN_VERIFIED, in the past tense, with the real figures.

        public static string LockoutImminent(string account, decimal dayLoss, decimal limit)
        {
            return "DAILY LOSS LIMIT REACHED. The guardian is closing your day on " + Safe(account) + ". " +
                   "You are down $" + Money.Format(dayLoss) + " and your limit is $" + Money.Format(limit) + ". " +
                   "I am about to cancel your working orders and close your positions. " +
                   "NinjaTrader will switch off any strategy running on this account as a result - " +
                   "that is NinjaTrader reacting to the positions being closed, not an error, and nothing is broken.";
        }

        public static string LockoutComplete(string account, decimal dayLoss, decimal limit,
                                             int ordersCancelled, string until)
        {
            return "LOCKED. " + Plural(ordersCancelled, "order") + " cancelled and positions closed on " +
                   Safe(account) + ", at $" + Money.Format(dayLoss) + " against a $" + Money.Format(limit) +
                   " limit. Any new order will be cancelled" + UntilClause(until) + ". " +
                   "This is what you asked for.";
        }

        /// <summary>ONLY for a TERMINAL LOCKOUT_INCOMPLETE - the one carrying exhausted:true.
        ///
        /// The first real run (2026-08-22) proved why this matters: in a NORMAL successful lockout the
        /// transient LOCKOUT_INCOMPLETE appears about half a second before FLATTEN_VERIFIED, because
        /// the flatten is a real market order and takes time to fill. Firing this text there would
        /// tell every user, in every ordinary lockout, to go and hand-close a position that is closing
        /// itself. Two other sites emit the same event for per-step exceptions and carry no
        /// `exhausted` field at all - and the ABSENCE of the field is not `false`, it is a different
        /// event. The caller must require the field, never infer it.</summary>
        public static string LockoutStillOpen(string account, int attempts)
        {
            return "COULD NOT CLOSE EVERYTHING on " + Safe(account) + " after " +
                   Plural(attempts, "attempt") + ". The daily limit is reached and new orders are still " +
                   "being cancelled, but something is still open. CLOSE IT YOURSELF NOW, then check the " +
                   "platform. This is the one case where the guardian needs you.";
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>"until 17:00 (America/Chicago)" - the zone is never dropped. "Until 17:00" means
        /// nothing to a reader in another one, and the guardian knows which zone it was configured
        /// with, so omitting it is a choice rather than a limitation.</summary>
        public static string Until(string resetLocalTime, string zoneId)
        {
            if (string.IsNullOrWhiteSpace(resetLocalTime)) return null;
            return string.IsNullOrWhiteSpace(zoneId)
                ? resetLocalTime
                : resetLocalTime + " (" + zoneId + ")";
        }

        private static string UntilClause(string until)
        {
            return string.IsNullOrWhiteSpace(until) ? "" : " until " + until;
        }

        private static string Plural(int n, string noun)
        {
            return n.ToString(CultureInfo.InvariantCulture) + " " + noun + (n == 1 ? "" : "s");
        }

        private static string Safe(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? "this account" : s;
        }
    }
}
