// cert-3b: THE CERTIFICATE CANNOT SEE THE DAY THAT MOST JUSTIFIES THIS PRODUCT.
//
// Found on 2026-09-01 by a side question of the brake audit, not by looking for it. EnterLockout has
// THREE callers and only ONE of them writes LIMIT_BREACHED (Guardian.cs:767). The other two are the
// tamper routes: config.json edited while sealed (CONFIG_TAMPERED, :566) and the sealed snapshot
// edited by hand inside state.json (SEAL_MISMATCH, :228). The certificate reads six event types
// (Certificate.cs:192-206) and NEITHER of those is among them.
//
// So a day on which the trader edited their configuration to loosen their own limit, and the guardian
// locked the account for it, certifies as: lockoutsTriggered 0, changeAttemptsWhileSealed 0,
// limitRespected TRUE. A day with no news.
//
// THAT IS THE DAY A TRADER WOULD WANT TO SHOW. It is the proof that the product does what it
// promises - somebody tried to loosen the brake and could not - and it is the only one the document
// cannot count. The brake worked; the evidence does not carry it.
//
// It is the exact inverse of what ALAYA found the same day: there, a brake that could not fail signed
// that it had protected. Here, a brake that DID act does not appear. One asserts too much, the other
// too little, and both produce a document that does not match reality.
//
// WHAT THIS FILE DOES AND DOES NOT DO. It does not fix the blind spot: making the tamper lockouts
// REPORTABLE is a decision about what the document should say, and it needs the field owner and the
// verifier's owner, not this session. What it does is refuse to let the silence stay silent - the
// certificate now STATES what it cannot see, in the list that exists for exactly that, the same way
// `ordersRejectedWhileLocked` states that its zero is a fact about the software.
//
// The first two tests are GREEN ON A DEFECT (the M4-M7 convention of this repo): they assert what is
// wrong today, so that the day the real fix lands they go red and force someone back here.

using System.Collections.Generic;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    public class Cert3_TamperLockoutTests : Harness
    {
        private const string TestSalt = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";

        private CertificateRequest Req() => new CertificateRequest
        {
            Alias = "roberto", DayKey = Guardian.Status.DayKey, IssuerVersion = "0.1.0",
            IssuerBuildHash = "test", AccountSalt = TestSalt,
        };

        /// <summary>A day shaped exactly like the one that matters: armed, then the config edited
        /// while sealed, then the lockout. No LIMIT_BREACHED anywhere, because no limit was breached -
        /// the trader was stopped BEFORE that, which is the point.
        ///
        /// WRITTEN THROUGH THE PRODUCTION WRITER, not hand-built (changed for cert-4). Since the
        /// emitter verifies the chain of what it is handed, a fixture of loose JsonObjects would come
        /// back "ledgerVerified: false" for a reason that has nothing to do with what these tests are
        /// about - and Cert3a would have failed for the wrong reason, which is worse than not having
        /// it. A new Ledger over the same store resumes the existing chain (LoadHead in its
        /// constructor), so these events continue the ones Armed() already wrote.</summary>
        private List<JsonObject> ATamperedDay()
        {
            if (Guardian == null || Guardian.Status.Kind != StateKind.Armed) Armed("600.00");
            var ledger = new Ledger(Store, LedgerPath);
            var at = Clock.UtcNow;
            ledger.Append(Ev.ConfigTampered, at, JsonValue.Obj());     // the trader raises their limit
            ledger.Append(Ev.OrdersCancelled, at, JsonValue.Obj());    // the guardian locks for it
            ledger.Append(Ev.FlattenRequested, at, JsonValue.Obj());
            ledger.Append(Ev.FlattenVerified, at, JsonValue.Obj());
            return LedgerEntries();
        }

        private PersistedState State()
        {
            if (Guardian == null || Guardian.Status.Kind != StateKind.Armed) Armed("600.00");
            PersistedState s; string err;
            Assert.True(PersistedState.TryParse(StateOnDisk(), out s, out err), err);
            return s;
        }

        private static JsonObject Parse(string json)
        {
            JsonValue v; string err;
            Assert.True(JsonParser.TryParse(json, out v, out err), err);
            return (JsonObject)v;
        }

        // ------------------------------------------------------------- green on the defect

        /// <summary>THE ONE THAT NAMED IT, now asserting the fix. It was written green-on-the-defect
        /// on 2026-09-01 so that it would turn RED the day the tamper lockouts became countable, and
        /// that is exactly what happened - within the day. `lockoutsTriggered` counts what its name
        /// says: EVERY route into EnterLockout, of which there are three.</summary>
        [Fact]
        public void Cert3a_a_day_locked_for_tampering_now_counts_that_lockout()
        {
            var r = Certificate.Issue(ATamperedDay(), State(), Req(), true);
            Assert.True(r.Ok, r.Reason);

            var claims = (JsonObject)Parse(r.Json)["claims"];
            Assert.Equal(1, claims.GetInt("lockoutsTriggered"));
        }

        /// <summary>AND THE KNOCK-ON THAT WAS NOT TAKEN, which is the more careful half. `limitRespected`
        /// still reads TRUE on a tamper day, because the trader did not breach their loss limit - they
        /// tried to loosen it and were stopped. Deriving it from the wider count would have published
        /// "the limit was not respected" about a day when it was, which is the same defect pointing the
        /// other way.
        ///
        /// So the two fields now say two different true things: a lockout happened, AND the loss limit
        /// was respected. Whether they should be coupled is semantics and a product decision, held
        /// separately on purpose.</summary>
        [Fact]
        public void Cert3b_the_wider_count_does_not_drag_limitRespected_with_it()
        {
            var r = Certificate.Issue(ATamperedDay(), State(), Req(), true);
            var claims = (JsonObject)Parse(r.Json)["claims"];

            Assert.Equal(1, claims.GetInt("lockoutsTriggered"));
            Assert.Equal("true", claims["limitRespected"].ToCanonical());
        }

        /// <summary>The seal-tamper route counts too - there are THREE callers of EnterLockout and the
        /// certificate has to see all of them, not two.</summary>
        [Fact]
        public void Cert3c_a_hand_edited_seal_counts_as_a_lockout_as_well()
        {
            if (Guardian == null || Guardian.Status.Kind != StateKind.Armed) Armed("600.00");
            new Ledger(Store, LedgerPath).Append(Ev.SealMismatch, Clock.UtcNow, JsonValue.Obj());

            var r = Certificate.Issue(LedgerEntries(), State(), Req(), true);
            var claims = (JsonObject)Parse(r.Json)["claims"];

            Assert.Equal(1, claims.GetInt("lockoutsTriggered"));
        }

        /// <summary>THE CONTROL THAT MUST STILL FAIL THE OTHER WAY, and it is what keeps the previous
        /// test from being read as "everything reads true now": a day with a REAL breach still counts
        /// it AND still publishes limitRespected FALSE. The two routes are distinguished, not merged.</summary>
        [Fact]
        public void Cert3d_a_real_breach_still_falsifies_limit_respected()
        {
            ATamperedDay();
            new Ledger(Store, LedgerPath).Append(Ev.LimitBreached, Clock.UtcNow, JsonValue.Obj());
            var day = LedgerEntries();

            var claims = (JsonObject)Parse(Certificate.Issue(day, State(), Req(), true).Json)["claims"];

            Assert.Equal(2, claims.GetInt("lockoutsTriggered"));                  // tamper + breach
            Assert.Equal("false", claims["limitRespected"].ToCanonical());        // the breach decides this
        }

        // ------------------------------------------------------------- what the document says about itself

        /// <summary>THE VERSION LINE, and it exists because of the twelve certificates already issued
        /// with the old count and nothing marking them. A reader comparing two documents from this
        /// installation must be able to see that the number changed meaning rather than the trader's
        /// behaviour.</summary>
        [Fact]
        public void Cert3e_the_document_says_what_this_version_counts()
        {
            var doc = Parse(Certificate.Issue(ATamperedDay(), State(), Req(), true).Json);
            var limitations = (JsonArray)doc["limitations"];

            var text = string.Join(" ", limitations.Items.Select(i => ((JsonString)i).Value));
            Assert.Contains("CONFIG_TAMPERED", text);
            Assert.Contains("SEAL_MISMATCH", text);
            Assert.Contains("lockoutsTriggered", text);
            Assert.Contains("2026-09-01", text);              // certificates issued before it counted one route
        }

        /// <summary>CONTAINMENT. The limitation says what was not measured; it must not say the
        /// tampering did not happen, and it must not promise the fix. A limitation that reassures is
        /// the house defect wearing the clothes of honesty.</summary>
        [Fact]
        public void Cert3d_the_limitation_neither_reassures_nor_promises()
        {
            var forbidden = new[]
            {
                "no tampering", "was not tampered", "nobody tried", "will be fixed",
                "soon", "in a future version", "safe", "you can be sure",
            };

            foreach (var l in Certificate.Limitations)
                foreach (var phrase in forbidden)
                    Assert.False(l.IndexOf(phrase, System.StringComparison.OrdinalIgnoreCase) >= 0,
                                 "'" + phrase + "' in: " + l);
        }
    }
}
