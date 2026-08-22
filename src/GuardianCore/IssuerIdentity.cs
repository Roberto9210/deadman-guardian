// Who emitted this certificate, said accurately or not said at all.
//
// Both fields were phantom claims before this file existed:
//
//   issuer.version   read Assembly.GetName().Version, which is AssemblyVersion. Nothing set it,
//                    so every certificate ever issued said "1.0.0.0" - a number that identifies
//                    no build and was never chosen by anyone.
//   issuer.buildHash hashed Assembly.Location in the CLI (a FILE PATH: two different builds at
//                    the same path collide, the same build moved changes value) and
//                    Assembly.FullName in the add-on (which only varies when the version does).
//                    Neither was a fingerprint of anything.
//
// A certificate exists to be checked by someone who does not trust us. A field that looks like
// evidence and is not is worse there than an absent field, so both are now either true or
// omitted - SPEC section 4.1, no defaults, applied to the issuer block.

using System;
using System.Reflection;

namespace GuardianCore
{
    public static class IssuerIdentity
    {
        /// <summary>The product version, from AssemblyInformationalVersion so a pre-release
        /// suffix survives ("0.1.0-beta" rather than "0.1.0.0"). Falls back to the assembly
        /// version, and returns null rather than inventing anything when neither is meaningful -
        /// the certificate then omits the field instead of asserting a number nobody chose.</summary>
        public static string VersionOf(Assembly assembly)
        {
            if (assembly == null) return null;

            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informational != null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
            {
                // SourceLink and some SDKs append "+<commit>"; keep it, it identifies the build.
                return informational.InformationalVersion.Trim();
            }

            var version = assembly.GetName().Version;
            if (version == null) return null;
            var text = version.ToString();
            // The .NET default when nothing is set. It identifies no build, so it is not a version.
            return text == "0.0.0.0" || text == "1.0.0.0" ? null : text;
        }

        /// <summary>A build fingerprint: SHA-256 over the assembly bytes exactly as given, first 16
        /// hex characters, lowercase. No normalisation of any kind - the PE header, its timestamp and
        /// the MVID are all hashed along with the code.
        ///
        /// WHAT THIS FIELD ANSWERS, said precisely because a certificate is read by someone who does
        /// not trust us: "is this the same binary I was given?" - NOT "was this built from the same
        /// source?". Two builds of identical source normally differ here, because the toolchain writes
        /// a fresh MVID and timestamp into the PE header. A reader comparing two certificates learns
        /// that the binaries differ; they do not learn that the code did.
        ///
        /// The definition is deliberately the one a stranger reaches for first:
        ///     sha256sum GuardianCore.dll | cut -c1-16
        /// An earlier version hashed the base64 TEXT of the bytes, which produced a value nobody could
        /// reproduce without knowing the quirk - so anyone running the obvious command saw a mismatch
        /// and had every reason to conclude the field was a lie. A check that raises false alarms is
        /// worse than one nobody runs.
        ///
        /// The CALLER supplies the bytes. GuardianCore performs no file I/O anywhere - every other
        /// read and write goes through an injected IFileStore - and reading a file here to save the
        /// caller four lines would break that invariant for a cosmetic field.
        ///
        /// Returns null for null or empty input, so an unreadable assembly omits the field rather
        /// than publishing the hash of nothing.</summary>
        public static string BuildHashOf(byte[] assemblyBytes)
        {
            if (assemblyBytes == null || assemblyBytes.Length == 0) return null;
            return Hashing.Sha256Hex(assemblyBytes).Substring(0, 16);
        }
    }
}
