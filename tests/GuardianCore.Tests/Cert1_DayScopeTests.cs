// cert-1: the certificate has no SCOPE, and that is one defect wearing two names.
//
// The addon hands Certificate.Issue the WHOLE ledger; Issue takes min/max(seq) of whatever it was
// given; Recompute then walks all of it. So limitRespected, lockoutsTriggered and failClosedEpisodes
// are totals of every day the installation has ever run, printed under a heading that names ONE day,
// and `daysCovered: 1` is not merely hardcoded - it is FALSE.
//
// Published evidence from this machine: certificate-2026-08-24.json carries ledgerRange {fromSeq: 1}
// and a fail-closed episode from 2026-08-21.
//
// It is the house's own subtype, the hardest one to see: A TRUE ASSERTION OVER THE WRONG SET. Every
// piece survives its own inspection - the arithmetic is right, the sentence is right - and the
// defect lives only in the joint between them.
//
// THE DEFINITION OF A DAY, and it is chosen so a stranger can check it without trusting the issuer:
//
//     the day N STARTS at the first entry carrying payload.dayKey == N
//     and ENDS where the NEXT day starts - or at the end of the record, if no next day exists
//
// The first draft was [min, max] over the dayKey-carrying entries, and it was WRONG in a way four
// existing C-tests caught before any of it shipped: on a day that has closed it is right, but on a
// day still OPEN the last such entry is DAY_OPENED, near the beginning, so everything the day
// actually did fell outside its own certificate. The open day is the one a trader exports at 16:55.
//
// Anyone with the ledger derives either half with one scan. It needs no event ordering, no
// positional heuristics, and it works on ledgers ALREADY WRITTEN - which is indispensable, because
// certificates are issued over history that exists.
//
// It was chosen over the obvious "between DAY_OPENED and DAY_CLOSED" for a reason found by reading
// the live ledger rather than the code: DAY_OPENED IS WRITTEN LAST in the arming sequence -
// CONFIG_LOADED, ARMED, SEAL_CREATED, then DAY_OPENED - so that window would EXCLUDE THE ARMED EVENT
// THAT ESTABLISHES THE LIMIT, which is the single most important claim in the document.
//
// The known gap, named rather than hidden: CONFIG_LOADED precedes ARMED and carries no dayKey, so it
// falls outside. It is the entry holding configHash. Closing that gap means CONFIG_LOADED carrying a
// dayKey, which is an additive FIELD and therefore belongs to the extension contract with Ventana B.

using System;
using System.Collections.Generic;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    public class Cert1_DayScopeTests : Harness
    {
        private const string TestSalt = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";
        private const string Yesterday = "2026-08-30";
        private const string Today = "2026-08-31";

        private static CertificateRequest Req(string day) => new CertificateRequest
        {
            Alias = "roberto", DayKey = day, IssuerVersion = "0.1.0",
            IssuerBuildHash = "test", AccountSalt = TestSalt,
        };

        private static JsonObject E(int seq, string ev, string dayKey = null, string orderId = null)
        {
            var payload = JsonValue.Obj();
            if (dayKey != null) payload.Set("dayKey", dayKey);
            if (orderId != null) payload.Set("orderId", orderId);
            return JsonValue.Obj()
                .Set("seq", seq)
                .Set("event", ev)
                .Set("tsUtc", "2026-08-31T12:00:0" + (seq % 10) + ".000Z")
                .Set("payload", payload);
        }

        /// <summary>Two days in one ledger, shaped like the real one - including DAY_OPENED arriving
        /// LAST in the arming sequence, which is what rules out the obvious definition.</summary>
        private static List<JsonObject> TwoDays() => new List<JsonObject>
        {
            E(1, Ev.ConfigLoaded),                       // no dayKey - the known gap
            E(2, Ev.Armed,          Yesterday),
            E(3, Ev.SealCreated),
            E(4, Ev.DayOpened,      Yesterday),
            E(5, Ev.LimitBreached),                      // YESTERDAY's breach
            E(6, Ev.OrderRejectedLocked, null, "o-1"),   // and yesterday's rejection
            E(7, Ev.DayClosed,      Yesterday),
            E(8, Ev.Disarmed,       Yesterday),

            E(9,  Ev.ConfigLoaded),
            E(10, Ev.Armed,         Today),
            E(11, Ev.SealCreated),
            E(12, Ev.DayOpened,     Today),
            E(13, Ev.DayClosed,     Today),
            E(14, Ev.Disarmed,      Today),
        };

        private PersistedState State()
        {
            Armed("600.00");
            PersistedState s; string err;
            return PersistedState.TryParse(StateOnDisk(), out s, out err) ? s : null;
        }

        // ------------------------------------------------------------------ the scope

        /// <summary>THE ONE THAT NAMES THE DEFECT. A certificate for today must not contain a single
        /// event from yesterday - and yesterday here has a breach and a rejection, which are exactly
        /// the claims a prop firm reads.</summary>
        [Fact]
        public void Cert1a_a_certificate_for_today_carries_nothing_from_yesterday()
        {
            var r = Certificate.Issue(TwoDays(), State(), Req(Today), true);
            Assert.True(r.Ok, r.Reason);

            var doc = ParseDoc(r.Json);
            var claims = (JsonObject)doc["claims"];
            var range = (JsonObject)claims["ledgerRange"];

            Assert.Equal(10, range.GetInt("fromSeq"));   // the ARMED that opens today
            Assert.Equal(14, range.GetInt("toSeq"));     // the DISARMED that closes it

            // yesterday's breach and yesterday's rejection are NOT today's
            Assert.Equal(0, claims.GetInt("lockoutsTriggered"));
            Assert.Equal(0, claims.GetInt("ordersRejectedWhileLocked"));
        }

        /// <summary>And the mirror: yesterday's certificate carries yesterday's breach, so the scope
        /// is narrowing rather than simply losing events.</summary>
        [Fact]
        public void Cert1b_yesterdays_certificate_still_carries_yesterdays_breach()
        {
            var r = Certificate.Issue(TwoDays(), State(), Req(Yesterday), true);
            Assert.True(r.Ok, r.Reason);

            var claims = (JsonObject)ParseDoc(r.Json)["claims"];
            var range = (JsonObject)claims["ledgerRange"];

            Assert.Equal(2, range.GetInt("fromSeq"));
            Assert.Equal(8, range.GetInt("toSeq"));
            Assert.Equal(1, claims.GetInt("lockoutsTriggered"));
        }

        /// <summary>daysCovered stops being a number somebody typed. It is 1 because the span holds
        /// one dayKey - counted, not asserted.
        ///
        /// THIS TEST PASSED BEFORE THE FIX, FOR THE WRONG REASON: the value was hardcoded to 1 in the
        /// request, so it read 1 while describing nine days. A green that means nothing until the
        /// thing under it changes - which is why the assertion is kept and the reason is written
        /// here instead.</summary>
        [Fact]
        public void Cert1c_daysCovered_is_one_because_it_is_true_not_because_it_is_wired()
        {
            var r = Certificate.Issue(TwoDays(), State(), Req(Today), true);
            var continuity = (JsonObject)ParseDoc(r.Json)["continuity"];

            Assert.Equal(1, continuity.GetInt("daysCovered"));
        }

        /// <summary>A day the ledger does not delimit produces NO certificate. Fail-closed, the same
        /// as everywhere else: a document about a day with no evidence would be an assertion with no
        /// source.</summary>
        [Fact]
        public void Cert1d_a_day_with_no_entries_is_refused_never_guessed()
        {
            var r = Certificate.Issue(TwoDays(), State(), Req("2026-07-04"), true);

            Assert.False(r.Ok);
            Assert.Contains("2026-07-04", r.Reason, StringComparison.Ordinal);
        }

        /// <summary>THE ONE THE FIRST DRAFT GOT WRONG, and the existing C-tests caught it before any
        /// of this shipped.
        ///
        /// The first definition made the span [min, max] over the entries carrying the dayKey. On a
        /// day that has CLOSED that is right. On a day still OPEN the last such entry is DAY_OPENED,
        /// near the beginning - so everything the day actually did fell outside its own certificate,
        /// and four C-tests went red asserting exactly that.
        ///
        /// A day ends where the NEXT day begins; with no next day it runs to the end of the record.
        /// This is the open-day half, which is the half a trader exports at 16:55.</summary>
        [Fact]
        public void Cert1f_a_day_that_has_not_closed_yet_runs_to_the_end_of_the_ledger()
        {
            // today's entries with nothing after them, plus events that carry NO dayKey and would be
            // lost by a span that stopped at DAY_OPENED
            var open = new List<JsonObject>
            {
                E(1, Ev.ConfigLoaded),
                E(2, Ev.Armed,     Today),
                E(3, Ev.DayOpened, Today),
                E(4, Ev.LimitBreached),          // after DAY_OPENED, no dayKey of its own
                E(5, Ev.PnlCheckpoint),
            };

            var r = Certificate.Issue(open, State(), Req(Today), true);
            Assert.True(r.Ok, r.Reason);

            var claims = (JsonObject)ParseDoc(r.Json)["claims"];
            Assert.Equal(5, ((JsonObject)claims["ledgerRange"]).GetInt("toSeq"));
            Assert.Equal(1, claims.GetInt("lockoutsTriggered"));   // the breach is inside its own day
        }

        // ------------------------------------------------------------------ candidate 6

        /// <summary>ordersRejectedWhileLocked reports 0 BECAUSE NOTHING PRODUCES THE EVENT any more -
        /// the LT-1 fix stopped cancelling on observation and ORDER_REJECTED_LOCKED lost its only
        /// writer. A zero meaning "this function does not exist" published beside zeros meaning "this
        /// did not happen" is the house defect in a number.
        ///
        /// The field STAYS - removing it would change the document's shape for a verifier that is not
        /// ours to break - and the certificate says what the zero means, in the list that exists for
        /// exactly this: what the document does not say.</summary>
        [Fact]
        public void Cert1e_the_rejected_orders_zero_says_that_the_capability_is_absent()
        {
            var r = Certificate.Issue(TwoDays(), State(), Req(Today), true);
            var doc = ParseDoc(r.Json);

            Assert.Equal(0, ((JsonObject)doc["claims"]).GetInt("ordersRejectedWhileLocked"));

            var lims = ((JsonArray)doc["limitations"]).Items
                .Select(i => ((JsonString)i).Value).ToList();
            Assert.Contains(lims, l => l.IndexOf("orders rejected", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static JsonObject ParseDoc(string json)
        {
            JsonValue v; string err;
            Assert.True(JsonParser.TryParse(json, out v, out err), err);
            return (JsonObject)v;
        }
    }
}
