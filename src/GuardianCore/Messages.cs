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

        /// <summary>The one headline that asks for something. "LOCKED" is a statement about the
        /// guardian; this is an instruction to the reader, and the difference is the whole point of
        /// the state existing separately (LT-4 / candidate 8).</summary>
        public const string HeadlineNeedsYou = "THE GUARDIAN NEEDS YOU";
        public const string HeadlineCannotSee = "CANNOT SEE YOUR ACCOUNT";
        public const string HeadlineNotArmed = "NOT ARMED";

        /// <summary>The limit was reached, and the guardian did NOT close anything, because the breach
        /// rests on figures it adopted at restart rather than fills it watched happen (M22).
        ///
        /// It needs its own headline because CANNOT SEE YOUR ACCOUNT is false here in the most
        /// misleading way available: the guardian can see the account perfectly. What it will not do
        /// is act on a number it did not witness. A reader shown "cannot see your account" at their
        /// daily limit, with a position still open, has been told the wrong thing to go fix.</summary>
        public const string HeadlineLimitNotFlattened = "DAILY LIMIT REACHED - NOTHING CLOSED";

        /// <summary>The opening words the guardian writes as its reason in that state. Single-source,
        /// and the coupling is deliberate: the producer builds its reason from this constant and the
        /// window recognises the state by it, so there is one sentence rather than two copies that
        /// drift. Matching on a string is the cost of Status carrying no discriminator; when one is
        /// added, this predicate is the only place that changes.</summary>
        public const string ReasonLimitNotFlattened = "daily limit reached on figures I did not see happen";

        public static bool IsLimitNotFlattened(string reason)
        {
            return !string.IsNullOrEmpty(reason)
                && reason.StartsWith(ReasonLimitNotFlattened, StringComparison.Ordinal);
        }

        /// <summary>Wording this product used to put in front of a user and no longer does.
        ///
        /// A10 says user-facing text is single-source - but some surfaces CANNOT import this file.
        /// install.ps1 is PowerShell; it cannot reference GuardianCore, so its copy of any sentence is
        /// unavoidable. A rule that cannot be obeyed protects nothing, so those surfaces are covered by
        /// a CHECK instead: a test scans every script and source file for these phrases and goes red if
        /// a retired one is still sitting somewhere. Retiring wording means adding it here.
        ///
        /// "NOT PROTECTED" is first because it survived its own removal: it had been taken out of the
        /// status window and was still greeting the reader from the installer's closing text.</summary>
        public static readonly string[] Retired = { "NOT PROTECTED" };

        /// <summary>Headline by state AND cause. ui-1 - the headline being derived from the state
        /// alone - stays open for the other states; this overload closes the one case where the state
        /// headline was outright false rather than merely coarse.</summary>
        public static string Headline(StateKind kind, string reason)
        {
            if (kind == StateKind.FailClosed && IsLimitNotFlattened(reason)) return HeadlineLimitNotFlattened;
            return Headline(kind);
        }

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

        /// <summary>The panel's text for the one state where this product depends on a person.
        ///
        /// It exists because of what 2026-08-26 cost: the guardian asked for help 165 times through
        /// NinjaScript.Log, and the person it was asking answered, five days later, "no me di cuenta".
        /// He does not read that tab. Meanwhile the panel - Topmost, on screen, the one surface he
        /// sees without looking for it - showed the ordinary locked text, promising that no position
        /// would stay open while one was open and stuck.
        ///
        /// THREE THINGS ARE TRUE AT ONCE AND ALL THREE HAVE TO BE HERE. Something is still open; the
        /// guardian has NOT stopped; and it needs the person. Dropping the second would inherit the
        /// vocabulary of the flag this is derived from - `exhausted`, MaxFlattenAttempts - and both of
        /// those names assert a giving-up that the code does not do. That is the same lie removed from
        /// LockoutStillOpen an hour before this was written, arriving through a name instead of a
        /// sentence.</summary>
        public static string DetailNeedsYou(string account)
        {
            return "SOMETHING IS STILL OPEN on " + Safe(account) + ". The guardian keeps trying and " +
                   "has not stopped - but it cannot finish this one alone. CLOSE IT YOURSELF NOW, " +
                   "then check the platform.";
        }

        /// <summary>The taskbar and Alt-Tab title: the only text a person reads WITHOUT switching to
        /// the window. It was the constant "deadman-guardian" - the product's own name, which the
        /// reader already knows - so a free channel carried nothing.
        ///
        /// The STATE LEADS, because a taskbar entry is truncated from the right: whatever is at the
        /// end is the first thing lost. And the needs-you title obeys LT4h's rule too - no vocabulary
        /// of giving up - which matters more here than anywhere, because this is the string read most
        /// often and inspected least.</summary>
        public static string WindowTitle(StateKind kind, bool needsHuman)
        {
            var state = needsHuman ? "CLOSE IT YOURSELF"
                      : kind == StateKind.Locked ? HeadlineLocked
                      : kind == StateKind.Armed ? HeadlineArmed
                      : kind == StateKind.FailClosed ? HeadlineCannotSee
                      : HeadlineNotArmed;
            return state + " - deadman-guardian";
        }

        public static string DetailLocked(string account, string until)
        {
            // See LockoutComplete for why "any new order will be cancelled" is gone from every message.
            return "Daily limit reached on " + Safe(account) + ". This does not block new orders - " +
                   "nothing here can - but no position will stay open" + UntilClause(until) + ".";
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

        /// <summary>What to DO, which here is a decision only the trader can make. The guardian is
        /// not going to close the position, and saying so plainly is the whole point: a reader who
        /// believes it was handled will leave a position open past their limit.</summary>
        public static string DetailLimitNotFlattened(string account, string reason, string until)
        {
            return "You are at your daily limit on " + Safe(account) + ". " + (reason ?? "") +
                   " I have NOT closed anything, and I am not going to on these figures: part of this " +
                   "loss is a number I adopted from the platform when this session started, and I do " +
                   "not close positions on the strength of fills I never saw." +
                   " New entries are blocked" + UntilClause(until) + "." +
                   " If you want your positions closed, close them yourself. From the next fill I do " +
                   "see, I enforce the limit normally.";
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

        // ---------------------------------------------------------------- LT-2: absent figures
        //
        // A real person read "You are down $40.00 and your limit is $0.00" on 2026-08-26, with a $40
        // limit, because _personalLimit is assigned only in the arm path and a restore does not run it.
        //
        // A PLAUSIBLE DEFAULT LIES; AN ABSENCE TELLS THE TRUTH BY SAYING NOTHING - and the field's
        // TYPE decides which it can do. Until() could return null and the clause vanished cleanly; a
        // decimal had no such option and printed money that was never true.
        //
        // So the figures arrive nullable, and an absent one is SUPPRESSED and the reader is pointed at
        // the record that does have it. Zero is NOT absence: ORDERS_CANCELLED carried a real count of
        // 0 on 2026-08-26 - there were no resting orders - and "no orders were cancelled" and "I do
        // not know how many" are different facts that must read differently.

        public static string LockoutImminent(string account, decimal? dayLoss, decimal? limit)
        {
            var figures = dayLoss.HasValue && limit.HasValue
                ? " You are down $" + Money.Format(dayLoss.Value) + " and your limit is $" +
                  Money.Format(limit.Value) + "."
                : limit.HasValue
                    ? " Your limit is $" + Money.Format(limit.Value) + "; the figure for today is in your record."
                    : " The figures for today are in your record.";

            return "DAILY LOSS LIMIT REACHED. The guardian is closing your day on " + Safe(account) + "." +
                   figures +
                   " I am about to cancel your working orders and close your positions. " +
                   "NinjaTrader will switch off any strategy running on this account as a result - " +
                   "that is NinjaTrader reacting to the positions being closed, not an error, and nothing is broken.";
        }

        public static string LockoutComplete(string account, decimal? dayLoss, decimal? limit,
                                             int? ordersCancelled, string until)
        {
            var cancelled = ordersCancelled.HasValue
                ? Plural(ordersCancelled.Value, "order") + " cancelled and positions closed on " + Safe(account)
                : "Your positions were closed on " + Safe(account);

            var against = dayLoss.HasValue && limit.HasValue
                ? ", at $" + Money.Format(dayLoss.Value) + " against a $" + Money.Format(limit.Value) + " limit."
                : limit.HasValue
                    ? ", against a $" + Money.Format(limit.Value) + " limit."
                    : ".";

            // Said once, and only when something really is missing. A reader who is told to check the
            // record when nothing is missing learns to ignore the sentence.
            var missing = (dayLoss.HasValue && limit.HasValue && ordersCancelled.HasValue)
                ? ""
                : " The figures this message cannot state are in your record - I was restarted and did " +
                  "not witness them myself.";

            // ------------------------------------------------------------ C of LT-4, 2026-08-31
            //
            // What stood here was "Any new order will be cancelled{until}." It was read by a real
            // person on 2026-08-31 at 09:10:30, one second after FLATTEN_VERIFIED, and it was FALSE
            // in two separate ways:
            //
            //   1. it promised CANCELLATION, which stopped happening with the LT-1 fix, and
            //   2. it promised PREVENTION, which NO version of this product can deliver - NT8 has no
            //      pre-submit veto (2,912 types scanned, STEP3_FINDINGS section 4), so an order that
            //      is sent reaches the market and fills.
            //
            // The second half is the important one: the sentence was not made false by a defect, it
            // was never true. LT4d pins the replacement against a vocabulary of impossibility rather
            // than against one phrase, because the defect is a WAY OF SPEAKING and the next person to
            // write a message here will invent their own version of the same false promise.
            //
            // What is true under the LT-4 fix is a claim about the OUTCOME, not the mechanism: orders
            // are not stopped, and exposure does not stand. That is the promise, and it is the whole
            // promise - no timing, no blocking, no impossibility.
            return "LOCKED. " + cancelled + against + missing +
                   " This does not block new orders - nothing here can - but no position will stay open" +
                   UntilClause(until) + ". This is what you asked for.";
        }

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
                   " limit. This does not block new orders - nothing here can - but no position will " +
                   "stay open" + UntilClause(until) + ". This is what you asked for.";
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
            // "new orders are still being cancelled" was the SAME false clause as LockoutComplete's,
            // sitting in the message that fires in the WORSE case - the one where the reader most
            // needs an accurate picture. Fixing message two and leaving this one would have moved the
            // lie rather than removed it. What is true here: the guardian does not give up. The loop
            // re-enters every tick while Locked and unverified; MaxFlattenAttempts only lights a flag
            // (Guardian.cs:1044) and gates nothing.
            return "COULD NOT CLOSE EVERYTHING on " + Safe(account) + " after " +
                   Plural(attempts, "attempt") + ". The daily limit is reached and the guardian keeps " +
                   "trying, but something is still open. CLOSE IT YOURSELF NOW, then check the " +
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
