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

        /// <summary>THE ONE THAT NAMES IT. Green today, and it must go RED the day the tamper
        /// lockouts become reportable - which is why it is written as an assertion rather than as a
        /// comment somewhere.</summary>
        [Fact]
        public void Cert3a_a_day_locked_for_tampering_still_certifies_as_a_quiet_day()
        {
            var r = Certificate.Issue(ATamperedDay(), State(), Req(), true);
            Assert.True(r.Ok, r.Reason);

            var doc = Parse(r.Json);
            var claims = (JsonObject)doc["claims"];
            var commitment = (JsonObject)doc["commitment"];

            Assert.Equal(0, claims.GetInt("lockoutsTriggered"));                 // a lockout happened
            Assert.Equal(0, commitment.GetInt("changeAttemptsWhileSealed"));     // an attempt happened
            Assert.Equal("true", claims["limitRespected"].ToCanonical());        // and the day reads clean
        }

        /// <summary>And the mirror, so the first test cannot be read as "the emitter counts nothing":
        /// the same day with a real breach in it counts the breach. The blind spot is specific to the
        /// tamper routes, not general.</summary>
        [Fact]
        public void Cert3b_the_same_day_with_a_real_breach_does_count_it()
        {
            ATamperedDay();
            new Ledger(Store, LedgerPath).Append(Ev.LimitBreached, Clock.UtcNow, JsonValue.Obj());
            var day = LedgerEntries();

            var doc = Parse(Certificate.Issue(day, State(), Req(), true).Json);
            var claims = (JsonObject)doc["claims"];

            Assert.Equal(1, claims.GetInt("lockoutsTriggered"));
            Assert.Equal("false", claims["limitRespected"].ToCanonical());
        }

        // ------------------------------------------------------------- the mitigation

        /// <summary>The silence stops being silent. Not a fix - a statement of what the document
        /// cannot see, in the list that exists for that, reaching the same reader the number
        /// reaches.</summary>
        [Fact]
        public void Cert3c_the_document_states_the_blind_spot_it_has()
        {
            var doc = Parse(Certificate.Issue(ATamperedDay(), State(), Req(), true).Json);
            var limitations = (JsonArray)doc["limitations"];

            var text = string.Join(" ", limitations.Items.Select(i => ((JsonString)i).Value));
            Assert.Contains("CONFIG_TAMPERED", text);
            Assert.Contains("SEAL_MISMATCH", text);
            Assert.Contains("lockoutsTriggered", text);
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
