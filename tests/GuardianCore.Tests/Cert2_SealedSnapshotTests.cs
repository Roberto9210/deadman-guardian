// cert-2: what the certificate does when the SEALED SNAPSHOT cannot answer.
//
// THE PREMISE CHANGED ON THE WAY IN, AND THAT IS THE POINT OF THIS HEADER. The question arrived as
// "session.timezone comes out empty through the LT-2 path, so fetch the zone from Core the way the
// messages now do". Verifying it first said otherwise, in two independent ways:
//
//   1. `sessionResetTimeZone` is in GuardianConfig.RequiredKeys (GuardianConfig.cs:18-22). A config
//      without it NEVER PARSES, so it never becomes a seal. The emitter reads the zone from the
//      SEALED SNAPSHOT itself - which is the same source Core reads after a restart, so there was
//      never a plumbing gap on this channel to begin with.
//   2. All six certificates issued on this machine carry "America/Chicago". None is empty.
//
// So `?? ""` here is not representing an honest absence. It is converting an unreachable state into
// a filler value - and the filler is the one thing a verifier rejects.
//
// WHAT THE FALLBACK REALLY COSTS, which is more than the zone: when the snapshot does not parse at
// all, this emitter still produces a document - with an EMPTY ACCOUNTS ARRAY and no limits. An empty
// collection asserts absence: that certificate says "this trader guarded no accounts" when the truth
// is "the emitter could not read its own seal".
//
// The answer this file holds the emitter to is its own neighbours', four lines away in both
// directions: C7 refuses rather than inventing an alias or a dayKey, and the signing path refuses
// rather than half-signing ("an unsigned certificate is honest, a half-signed one is not").

using System.Collections.Generic;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    public class Cert2_SealedSnapshotTests : Harness
    {
        private const string TestSalt = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";
        private const string Today = "2026-08-31";

        private static CertificateRequest Req() => new CertificateRequest
        {
            Alias = "roberto", DayKey = Today, IssuerVersion = "0.1.0",
            IssuerBuildHash = "test", AccountSalt = TestSalt,
        };

        private static JsonObject E(int seq, string ev, string dayKey = null)
        {
            var payload = JsonValue.Obj();
            if (dayKey != null) payload.Set("dayKey", dayKey);
            return JsonValue.Obj()
                .Set("seq", seq).Set("event", ev)
                .Set("tsUtc", "2026-08-31T12:00:0" + (seq % 10) + ".000Z")
                .Set("payload", payload);
        }

        private static List<JsonObject> OneDay() => new List<JsonObject>
        {
            E(1, Ev.ConfigLoaded),
            E(2, Ev.Armed,      Today),
            E(3, Ev.SealCreated),
            E(4, Ev.DayOpened,  Today),
            E(5, Ev.DayClosed,  Today),
        };

        /// <summary>Arms ONCE and then re-reads from disk, so a test that needs two states does not
        /// arm twice. Writing it the naive way cost a red: the second Arm lands in TryChangeConfig
        /// and is rejected, because arming while sealed is a config change (SPEC 7.2). The brake
        /// audited yesterday caught this file.</summary>
        private PersistedState State()
        {
            if (Guardian == null || Guardian.Status.Kind != StateKind.Armed) Armed("600.00");
            PersistedState s; string err;
            Assert.True(PersistedState.TryParse(StateOnDisk(), out s, out err), err);
            return s;
        }

        /// <summary>The same state, with the sealed snapshot replaced. The seal's own hash is left
        /// alone on purpose: Issue does not check SnapshotMatchesHash - Start does, and it locks out
        /// (Guardian.cs:224-229). What is under test here is the emitter, not the tamper detector.</summary>
        private PersistedState StateWithSnapshot(string snapshot)
        {
            var st = State();
            var s = st.Seal;
            st.Seal = new Seal(s.SealHash, snapshot, s.ArmedAtUtc, s.ExpiresAtUtc, s.DayKey,
                               s.LedgerHeadHash, s.MonoAtArmMs, s.SealDurationMs, s.RunId);
            return st;
        }

        private PersistedState StateWithoutKey(string key)
        {
            var st = State();
            JsonValue v; string err;
            Assert.True(JsonParser.TryParse(st.Seal.ConfigSnapshot, out v, out err), err);
            return StateWithSnapshot(((JsonObject)v).Remove(key).ToCanonical());
        }

        private static JsonObject Parse(string json)
        {
            JsonValue v; string err;
            Assert.True(JsonParser.TryParse(json, out v, out err), err);
            return (JsonObject)v;
        }

        // ------------------------------------------------------------------ the control

        /// <summary>THE CASE THAT MUST KEEP PASSING. It is green before the fix and after it: an
        /// ordinary sealed day still issues, and the zone in the document is the zone in the seal -
        /// read from the seal, never from this machine's clock.</summary>
        [Fact]
        public void Cert2a_an_ordinary_seal_still_issues_and_carries_its_own_zone()
        {
            var r = Certificate.Issue(OneDay(), State(), Req(), true);
            Assert.True(r.Ok, r.Reason);

            var session = (JsonObject)Parse(r.Json)["session"];
            Assert.Equal("America/Chicago", session.GetString("timezone"));
        }

        // ------------------------------------------------------------------ the two refusals

        /// <summary>A seal whose snapshot has lost its zone is a seal this emitter cannot describe.
        /// Blanking the field publishes a value a verifier rejects; refusing says the true thing.</summary>
        [Fact]
        public void Cert2b_a_snapshot_without_its_timezone_is_refused_rather_than_blanked()
        {
            var r = Certificate.Issue(OneDay(), StateWithoutKey("sessionResetTimeZone"), Req(), true);

            Assert.False(r.Ok, "a certificate was issued for a seal with no time zone");
            Assert.Contains("CERT_TIMEZONE_MISSING", r.Reason);
        }

        /// <summary>THE ONE THAT COSTS MORE THAN THE ZONE. With an unparseable snapshot the emitter
        /// used to publish a whole document whose `accounts` array was EMPTY - a certificate about
        /// no accounts at all, which reads as a fact about the trader and is a fact about the
        /// emitter.</summary>
        [Fact]
        public void Cert2c_an_unparseable_snapshot_is_refused_rather_than_silently_emptied()
        {
            var r = Certificate.Issue(OneDay(), StateWithSnapshot("{ not json"), Req(), true);

            Assert.False(r.Ok, "a certificate was issued from a snapshot that does not parse");
            Assert.Contains("CERT_SNAPSHOT_UNPARSEABLE", r.Reason);
        }

        /// <summary>A refusal that still hands back a document is worse than no refusal: something
        /// downstream writes the file anyway. Same shape as the rollback that announced ROLLED BACK
        /// having restored nothing.</summary>
        [Fact]
        public void Cert2d_a_refusal_hands_back_no_document_at_all()
        {
            foreach (var st in new[] { StateWithoutKey("sessionResetTimeZone"),
                                       StateWithSnapshot("{ not json") })
            {
                var r = Certificate.Issue(OneDay(), st, Req(), true);
                Assert.False(r.Ok);
                Assert.Null(r.Json);
                Assert.Null(r.Html);
                Assert.Null(r.CertHash);
            }
        }

        /// <summary>The refusal names the key, so whoever reads it looks in the right file. "Could
        /// not build the certificate" sends someone to the wrong place.</summary>
        [Fact]
        public void Cert2e_the_refusal_names_the_key_and_the_file_it_lives_in()
        {
            var r = Certificate.Issue(OneDay(), StateWithoutKey("sessionResetTimeZone"), Req(), true);
            Assert.Contains("sessionResetTimeZone", r.Reason);
        }

        /// <summary>AND WHERE THE ZONE COMES FROM, pinned so the fix cannot drift into reading the
        /// machine. The document echoes the SEAL's zone even when it is not this machine's - which is
        /// the whole reason the field is trustworthy after a restart (SPEC 7.4: the sealed snapshot
        /// is what remains in force, not any file on disk).</summary>
        [Fact]
        public void Cert2f_the_zone_in_the_document_is_the_zone_in_the_seal()
        {
            var st = State();
            JsonValue v; string err;
            Assert.True(JsonParser.TryParse(st.Seal.ConfigSnapshot, out v, out err), err);
            var elsewhere = ((JsonObject)v).Set("sessionResetTimeZone", "Asia/Tokyo").ToCanonical();

            var r = Certificate.Issue(OneDay(), StateWithSnapshot(elsewhere), Req(), true);
            Assert.True(r.Ok, r.Reason);

            var session = (JsonObject)Parse(r.Json)["session"];
            Assert.Equal("Asia/Tokyo", session.GetString("timezone"));
        }
    }
}
