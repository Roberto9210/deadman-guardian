// issuer.version and issuer.buildHash: true, or absent.
//
// Both were phantom claims. `version` read AssemblyVersion, which nothing set, so every
// certificate said "1.0.0.0" - a number identifying no build and chosen by no one. `buildHash`
// hashed a file PATH in the CLI and Assembly.FullName in the add-on. In a document written to be
// checked by someone who does not trust us, a field that looks like evidence and is not is worse
// than no field at all.

using System;
using System.Linq;
using System.Reflection;
using System.Text;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    public class C_IssuerIdentityTests
    {
        [Fact]
        public void The_core_assembly_no_longer_reports_the_sdk_default()
        {
            // The regression guard for the whole exercise: if someone drops the version
            // properties from the csproj, every certificate silently goes back to claiming
            // 1.0.0.0 and nothing else would notice.
            var version = IssuerIdentity.VersionOf(typeof(Certificate).Assembly);

            Assert.False(string.IsNullOrWhiteSpace(version));
            Assert.NotEqual("1.0.0.0", version);
            Assert.NotEqual("0.0.0.0", version);
            Assert.StartsWith("0.1.0", version, StringComparison.Ordinal);
        }

        [Fact]
        public void The_version_carries_the_prerelease_suffix_the_readme_states_in_words()
        {
            // "In testing. No release, no version tag, no users." The number must not claim more.
            var version = IssuerIdentity.VersionOf(typeof(Certificate).Assembly);
            Assert.Contains("-beta", version);
        }

        [Fact]
        public void An_assembly_with_nothing_set_yields_null_rather_than_a_default()
        {
            // Any assembly that never set a version: the BCL itself is not one, so use a
            // dynamic assembly, which is created with the 0.0.0.0 default and no attributes.
            var dynamic = AssemblyBuilderShim();
            Assert.Null(IssuerIdentity.VersionOf(dynamic));
        }

        [Fact]
        public void A_null_assembly_yields_null_rather_than_throwing()
        {
            Assert.Null(IssuerIdentity.VersionOf(null));
        }

        // ------------------------------------------------------------------ buildHash

        [Fact]
        public void Build_hash_is_a_fingerprint_of_the_bytes_not_of_a_path()
        {
            var a = Encoding.UTF8.GetBytes("one build");
            var b = Encoding.UTF8.GetBytes("another build");

            Assert.Equal(IssuerIdentity.BuildHashOf(a), IssuerIdentity.BuildHashOf(a));
            Assert.NotEqual(IssuerIdentity.BuildHashOf(a), IssuerIdentity.BuildHashOf(b));
            Assert.Equal(16, IssuerIdentity.BuildHashOf(a).Length);
        }

        /// <summary>The published definition, pinned. Every other buildHash test is behavioural -
        /// deterministic, sensitive, not-a-path - and all of them passed while the implementation
        /// hashed the base64 TEXT of the bytes instead of the bytes. A field a stranger is invited to
        /// recompute needs its exact formula under test, or the documentation is the only thing
        /// holding it and documentation does not fail a build.</summary>
        [Fact]
        public void Build_hash_is_exactly_the_first_16_hex_of_sha256_over_the_bytes()
        {
            var bytes = Encoding.UTF8.GetBytes("whatever bytes a build happens to have");

            // What CERT_CONFORMANCE.md tells a third party to run, expressed in code.
            var expected = Hashing.Sha256Hex(bytes).Substring(0, 16);

            Assert.Equal(expected, IssuerIdentity.BuildHashOf(bytes));
            Assert.Matches("^[0-9a-f]{16}$", IssuerIdentity.BuildHashOf(bytes));
        }

        [Fact]
        public void A_single_changed_byte_changes_the_build_hash()
        {
            var a = new byte[] { 1, 2, 3, 4, 5 };
            var b = new byte[] { 1, 2, 3, 4, 6 };
            Assert.NotEqual(IssuerIdentity.BuildHashOf(a), IssuerIdentity.BuildHashOf(b));
        }

        [Fact]
        public void Unreadable_bytes_yield_null_rather_than_the_hash_of_nothing()
        {
            Assert.Null(IssuerIdentity.BuildHashOf(null));
            Assert.Null(IssuerIdentity.BuildHashOf(new byte[0]));
        }

        // ------------------------------------------------------------------ the document

        [Fact]
        public void The_certificate_omits_what_the_issuer_could_not_determine()
        {
            // SPEC section 4.1 reaching the issuer block: unknown is absent, never a placeholder.
            var harness = new Harness();
            harness.Armed("600.00");
            harness.Guardian.Tick();

            PersistedState state; string error;
            Assert.True(PersistedState.TryParse(harness.StateOnDisk(), out state, out error), error);

            var result = Certificate.Issue(harness.LedgerEntries(), state, new CertificateRequest
            {
                Alias = "someone",
                DayKey = "2026-08-19",
                AccountSalt = new string('a', 64),
                IssuerVersion = null,        // could not be determined
                IssuerBuildHash = null,      // could not be read
            }, true);

            Assert.True(result.Ok, result.Reason);

            JsonValue parsed; string err;
            Assert.True(JsonParser.TryParse(result.Json, out parsed, out err), err);
            var issuer = (JsonObject)((JsonObject)parsed)["issuer"];

            Assert.Equal("deadman-guardian", issuer.GetString("tool"));
            Assert.False(issuer.Has("version"), "an unknown version must be omitted, not defaulted");
            Assert.False(issuer.Has("buildHash"), "an unreadable build hash must be omitted");
            Assert.DoesNotContain("1.0.0.0", result.Json);
        }

        [Fact]
        public void A_real_issue_carries_both_fields_and_they_are_not_the_old_placeholders()
        {
            var harness = new Harness();
            harness.Armed("600.00");
            harness.Guardian.Tick();

            PersistedState state; string error;
            Assert.True(PersistedState.TryParse(harness.StateOnDisk(), out state, out error), error);

            var assembly = typeof(Certificate).Assembly;
            var result = Certificate.Issue(harness.LedgerEntries(), state, new CertificateRequest
            {
                Alias = "someone",
                DayKey = "2026-08-19",
                AccountSalt = new string('a', 64),
                IssuerVersion = IssuerIdentity.VersionOf(assembly),
                IssuerBuildHash = IssuerIdentity.BuildHashOf(Encoding.UTF8.GetBytes("pretend bytes")),
            }, true);

            JsonValue parsed; string err;
            Assert.True(JsonParser.TryParse(result.Json, out parsed, out err), err);
            var issuer = (JsonObject)((JsonObject)parsed)["issuer"];

            Assert.StartsWith("0.1.0", issuer.GetString("version"), StringComparison.Ordinal);
            Assert.Equal(16, issuer.GetString("buildHash").Length);

            // The old buildHash hashed Assembly.Location - a path. Prove we are not doing that.
            var pathHash = Hashing.Sha256Hex(assembly.Location).Substring(0, 16);
            Assert.NotEqual(pathHash, issuer.GetString("buildHash"));
        }

        // ------------------------------------------------------------------ helper

        private static Assembly AssemblyBuilderShim()
        {
            var name = new AssemblyName("deadman.versionless.probe");
            return System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                name, System.Reflection.Emit.AssemblyBuilderAccess.Run);
        }
    }
}
