// cert-4: CERTIFY THE BYTES YOU READ.
//
// THE DEFECT, found by Ventana B reading this repo and confirmed here. DeadmanGuardianAddOn.ExportDay
// did this:
//
//     var verify  = ledger.Verify();      // re-reads the FILE
//     var entries = ledger.ReadAll();     // reads the file AGAIN
//     Certificate.Issue(entries, state, request, verify.Ok);
//
// Verify() never saw `entries`. So `ledgerVerified` was true of one read and printed over another,
// with a LIVE GUARDIAN APPENDING BETWEEN THEM. "The value is correct today" was not established: it is
// correct only if nothing was appended in that window, which nothing checks and nothing prevents. When
// it came out right, it came out right by coincidence.
//
// THE SAME DEFECT A SECOND TIME, IN THE SAME METHOD: ExportDay re-reads state.json and TryParse does
// not check the seal's hash, while SnapshotMatchesHash() has exactly one caller in the whole repo -
// Guardian.Start. So the guardian's SEAL_MISMATCH lockout is true of the file version it read AT BOOT,
// not of the version the certificate prints. The guardian rewrites that file all session, so the two
// reads differ WITHOUT AN ADVERSARY.
//
// It is one defect appearing twice - verify one read, publish another - and it is the house's first
// class: a true assertion over the wrong set.
//
// WHAT WAS REJECTED AS A FIX, and it matters: marking provenance. A row labelled "supplied by the
// caller" next to the name `ledgerVerified` is the house defect wearing the clothes of honesty. The
// fix is to make the sentence true instead of qualifying it.

using System.Collections.Generic;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    public class Cert4_CertifyWhatYouReadTests : Harness
    {
        private const string TestSalt = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";

        private CertificateRequest Req() => new CertificateRequest
        {
            Alias = "roberto", DayKey = Guardian.Status.DayKey, IssuerVersion = "0.1.0",
            IssuerBuildHash = "test", AccountSalt = TestSalt,
        };

        /// <summary>A REAL chained ledger, written by the production writer. Hand-built JsonObjects
        /// stopped being usable as a fixture the day ledgerVerified started meaning what it says.</summary>
        private List<JsonObject> RealLedger()
        {
            if (Guardian == null || Guardian.Status.Kind != StateKind.Armed) Armed("600.00");
            Guardian.Tick();
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

        private static bool LedgerVerifiedIn(string json) =>
            ((JsonObject)Parse(json)["claims"])["ledgerVerified"].ToCanonical() == "true";

        // ------------------------------------------------------------------ the keystone

        /// <summary>THE ONE THAT NAMES IT. The caller says the chain is fine; the entries handed over
        /// say otherwise. The document must follow the ENTRIES, because those are what it describes.
        /// This is the read-skew made deterministic: no timing needed, just two disagreeing sources.</summary>
        [Fact]
        public void Cert4a_the_document_follows_the_entries_it_was_given_not_the_callers_word()
        {
            var entries = RealLedger();
            var state = State();

            // Change a payload without recomputing the hash: exactly what a byte that changed between
            // the two reads would look like to the second one.
            var last = entries[entries.Count - 1];
            ((JsonObject)last["payload"]).Set("smuggled", "yes");

            var r = Certificate.Issue(entries, state, Req(), true);   // caller insists it is fine
            Assert.True(r.Ok, r.Reason);
            Assert.False(LedgerVerifiedIn(r.Json), "the emitter published the caller's word");
        }

        /// <summary>And the mirror, which is what proves the parameter is superseded rather than
        /// merely ANDed: a sound chain verifies even when the caller says it did not.</summary>
        [Fact]
        public void Cert4b_a_sound_chain_verifies_even_when_the_caller_says_otherwise()
        {
            var r = Certificate.Issue(RealLedger(), State(), Req(), false);
            Assert.True(r.Ok, r.Reason);
            Assert.True(LedgerVerifiedIn(r.Json));
        }

        /// <summary>The control that must not move: the ordinary case still issues and still says
        /// true. If this ever fails, the fix broke the thing it was protecting.</summary>
        [Fact]
        public void Cert4c_the_ordinary_case_still_issues_and_still_verifies()
        {
            var r = Certificate.Issue(RealLedger(), State(), Req(), true);
            Assert.True(r.Ok, r.Reason);
            Assert.True(LedgerVerifiedIn(r.Json));
            Assert.Equal("true", ((JsonObject)Parse(r.Json)["claims"])["limitRespected"].ToCanonical());
        }

        // ------------------------------------------------------------------ the seal, same defect

        /// <summary>The seal read at issue time is not the seal read at boot. If its snapshot no
        /// longer hashes to its own SealHash, the limits and the zone in this document are not the
        /// ones that were committed to - so there is nothing honest to publish. Refuse, exactly as
        /// the signing path four lines away refuses a half-signature.</summary>
        [Fact]
        public void Cert4d_a_seal_whose_snapshot_no_longer_matches_its_hash_is_refused()
        {
            var st = State();
            var s = st.Seal;
            st.Seal = new Seal(s.SealHash, s.ConfigSnapshot.Replace("600.00", "9000.00"), s.ArmedAtUtc,
                               s.ExpiresAtUtc, s.DayKey, s.LedgerHeadHash, s.MonoAtArmMs,
                               s.SealDurationMs, s.RunId);

            var r = Certificate.Issue(RealLedger(), st, Req(), true);
            Assert.False(r.Ok, "a certificate was issued over a snapshot that does not match its seal");
            Assert.Contains("CERT_SEAL_MISMATCH", r.Reason);
            Assert.Null(r.Json);
        }

        // ------------------------------------------------------------------ one rule, two doors

        // ------------------------------------------------------------------ where provenance IS the answer

        /// <summary>THE OTHER HALF OF THE SAME RULING, and the contrast is the lesson. Marking
        /// provenance was REJECTED for ledgerVerified - a row labelled "supplied by the caller" beside
        /// that name is the house defect in honest clothing, and the fix there was to make the
        /// sentence true. It is ACCEPTED for these four, because there is no truth to check them
        /// against: the alias is the trader's word about themselves, and the other three are empty
        /// because nothing fills them.
        ///
        /// The difference between the two halves, in one line: whether something COULD have been
        /// verified and was not.</summary>
        [Fact]
        public void Cert4f_the_document_states_the_four_things_nothing_backs()
        {
            var text = string.Join(" ", Certificate.Limitations);

            Assert.Contains("alias is the trader's own word", text);
            Assert.Contains("trust level is L1", text);
            Assert.Contains("empty list of gaps", text);
            Assert.Contains("unsigned certificates", text);

            // and none of them reassures - the containment that already guards this list
            foreach (var l in Certificate.Limitations)
                Assert.True(l.Contains("does not") || l.Contains("not an audit"),
                            "a limitation that does not limit anything: " + l);
        }

        /// <summary>ONE IMPLEMENTATION, TWO ENTRY POINTS - and this is the test that keeps it that
        /// way. The file door and the memory door must give the same verdict on the same content,
        /// INCLUDING the same broken seq. A second implementation would pass the agreement half and
        /// drift later; two independent implementations of the same wrong rule agree with each other,
        /// and their agreement reads as corroboration.
        ///
        /// The control that must FAIL is the second half: if both doors said Ok on a broken ledger,
        /// the test would be measuring nothing.</summary>
        [Fact]
        public void Cert4e_both_doors_agree_on_good_and_on_broken()
        {
            RealLedger();
            var ledger = new Ledger(Store, LedgerPath);

            var fileGood = ledger.Verify();
            var memGood = Ledger.VerifyEntries(ledger.ReadAll().ToList());
            Assert.True(fileGood.Ok);
            Assert.True(memGood.Ok);

            // A parseable line with a hash that does not match its contents - the shape both doors are
            // meant to catch. (An UNPARSEABLE line is the one case where they legitimately differ:
            // ReadAll drops it before the memory door can see it. That is documented on VerifyEntries.)
            var forged = JsonValue.Obj()
                .Set("seq", 9999).Set("event", Ev.DayClosed).Set("tsUtc", "2026-08-31T23:00:00.000Z")
                .Set("schemaVersion", 1).Set("payload", JsonValue.Obj())
                .Set("prev", "00").Set("hash", "ff");
            Store.AppendLine(LedgerPath, forged.ToCanonical());

            var fileBad = ledger.Verify();
            var memBad = Ledger.VerifyEntries(ledger.ReadAll().ToList());
            Assert.False(fileBad.Ok);
            Assert.False(memBad.Ok);
            Assert.Equal(fileBad.BrokenSeq, memBad.BrokenSeq);
        }
    }
}
