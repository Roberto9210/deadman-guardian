// WHICH BINARY WROTE THIS ROW - a question the ledger could not answer.
//
// Measured 2026-09-03 over the production ledger: GUARDIAN_STARTED carries only {"state":…}; a grep
// for build identity across all 8,119 rows returns 0; adapter.log does not record it either. The one
// surface that carries it is the certificate, issued on demand. Identifying the running build meant
// measuring string literals inside a DLL - a trick, not a datum, and one that only works while
// somebody remembers to run it.
//
// TWO FIELDS, AND THEIR DISAGREEMENT IS THE POINT. The operator caught this before it was written:
// the add-on's existing helper reads the FILE from disk (Assembly.Location -> File.ReadAllBytes), and
// install.ps1 copies the new GuardianCore.dll while the process is still running the old one in
// memory. For those 77 seconds a file hash would declare the NEW binary while EXECUTING THE OLD -
// lying in the exact moment this field exists for.
//
//   coreMvid   the loaded assembly's ModuleVersionId, read from metadata already in memory.
//              WHAT IS EXECUTING. Computed by Core itself, because a field that says "which build am
//              I" should be answered by the build it describes - an adapter supplying it is the
//              caller-trusting inversion M1 exists to prevent.
//   coreBuild  sha256[:16] of the file on disk, supplied by the host because that one is I/O and
//              Core performs none (IssuerIdentity.cs:64). WHAT IS ON DISK - the value the
//              certificate publishes and install.ps1 can stamp.
//
// Hashing the loaded BYTES is not reachable: the in-memory image is not byte-identical to the file
// (relocations, section alignment), so its hash would compare against nothing. The MVID is the
// reachable identity, and it is not a hash - it is not comparable with coreBuild, on purpose.
//
// THIS IS THE RECORD, NOT THE BRAKE. coreExpected and addonBuild are the comparison's inputs and
// belong to its own tanda (docs/freno-identidad-build-20260902.md). Recording is not enforcing.
//
// WHAT IT DOES NOT MEASURE: that two different builds always get different MVIDs. That is the
// compiler's contract, and if it ever broke the field would go constant - which no test here can
// see. Said out loud rather than assumed.

using System;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    public class C_BuildIdentityTests : Harness
    {
        private static string LoadedMvid =>
            typeof(Guardian).Assembly.ManifestModule.ModuleVersionId.ToString("N");

        [Fact]
        public void C_A_cold_start_records_both_identities_and_keeps_fresh()
        {
            BuildHash = "0123456789abcdef";
            NewGuardian();

            var payload = (JsonObject)LastEvent(Ev.GuardianStarted)["payload"];
            Assert.Equal("0123456789abcdef", payload.GetString("coreBuild"));
            Assert.Equal(LoadedMvid, payload.GetString("coreMvid"));
            Assert.Equal(true, payload.GetBool("fresh"));      // Ventana B's condition, untouched
        }

        [Fact]
        public void C_A_restored_start_records_them_too()
        {
            BuildHash = "0123456789abcdef";
            Armed("600.00");
            NewGuardian();                                     // second process, same store

            var payload = (JsonObject)LastEvent(Ev.GuardianStarted)["payload"];
            Assert.Equal("0123456789abcdef", payload.GetString("coreBuild"));
            Assert.Equal(LoadedMvid, payload.GetString("coreMvid"));
        }

        /// <summary>THE CONTROL. Absent before invented: a value the host could not obtain omits its
        /// key. Never "", never "unknown" - that doctrine already cost seven `?? ""` and a rejected
        /// DECORATIVE_FILLER, and the cheap implementation of this field is exactly the one that
        /// breaks it.</summary>
        [Fact]
        public void C_A_build_hash_the_host_could_not_read_is_absent_not_empty()
        {
            BuildHash = null;
            NewGuardian();

            Assert.Null(((JsonObject)LastEvent(Ev.GuardianStarted)["payload"]).GetString("coreBuild"));
            Assert.DoesNotContain("coreBuild", Store.GetRaw(LedgerPath));
        }

        [Fact]
        public void C_An_empty_build_hash_is_also_absent()
        {
            BuildHash = "";                                    // a host that computed nothing
            NewGuardian();
            Assert.DoesNotContain("coreBuild", Store.GetRaw(LedgerPath));
        }

        /// <summary>Core writes what it was handed. If it ever re-hashed, truncated or normalised the
        /// value, the ledger would publish something the host cannot reproduce - which is the defect
        /// IssuerIdentity was written to end.</summary>
        [Fact]
        public void C_Core_writes_the_supplied_value_verbatim_and_never_interprets_it()
        {
            BuildHash = "NOT-A-HASH-AT-ALL";
            NewGuardian();

            Assert.Equal("NOT-A-HASH-AT-ALL",
                ((JsonObject)LastEvent(Ev.GuardianStarted)["payload"]).GetString("coreBuild"));
        }

        /// <summary>coreMvid is not the host's to supply and does not go missing with it: it comes
        /// from metadata already in memory, so it is present even when the file could not be read.
        /// That is the whole reason it exists - the file is exactly what is unreliable mid-deploy.</summary>
        [Fact]
        public void C_The_executing_identity_is_present_even_when_the_file_hash_is_not()
        {
            BuildHash = null;
            NewGuardian();

            var mvid = ((JsonObject)LastEvent(Ev.GuardianStarted)["payload"]).GetString("coreMvid");
            Assert.Equal(LoadedMvid, mvid);
            Assert.Matches("^[0-9a-f]{32}$", mvid);
        }
    }
}
