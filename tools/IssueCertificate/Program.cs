// "Export my day" - the trader's explicit action (CERT_SPEC section 3c).
//
// This is the ONLY way a certificate comes into existence. Nothing in the engine calls it:
// no timer, no event, no lockout, no shutdown hook. A human runs it, on their own machine,
// against their own ledger, and the files land next to that ledger. There is no send.
//
//     issue-certificate --alias roberto-soak --day 2026-08-21
//
// It refuses rather than guesses: no alias, no day, no seal, unreadable chain - each is a
// named refusal, never a plausible default.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardianCore;

namespace GuardianCore.Tools
{
    internal static class Program
    {
        private sealed class DiskStore : IFileStore
        {
            public bool Exists(string path) => File.Exists(path);
            public string ReadAllText(string path) => File.ReadAllText(path);
            public IEnumerable<string> ReadLines(string path) => File.ReadLines(path);
            public void WriteAtomic(string path, string contents) =>
                throw new NotSupportedException("the exporter does not write through the store");
            public void AppendLine(string path, string line) =>
                throw new NotSupportedException("the exporter never appends to a ledger");
        }

        private static int Main(string[] args)
        {
            string alias = null, day = null, home = null, prev = null, outDir = null;
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--alias") alias = args[i + 1];
                else if (args[i] == "--day") day = args[i + 1];
                else if (args[i] == "--home") home = args[i + 1];
                else if (args[i] == "--previous") prev = args[i + 1];
                else if (args[i] == "--out") outDir = args[i + 1];
            }

            home ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8", "deadman-guardian");

            var ledgerPath = Path.Combine(home, "ledger.jsonl");
            var statePath = Path.Combine(home, "state.json");

            if (!File.Exists(ledgerPath)) return Fail("CERT_NO_LEDGER: no ledger at " + ledgerPath);
            if (!File.Exists(statePath)) return Fail("CERT_NO_STATE: no state file at " + statePath);
            if (string.IsNullOrWhiteSpace(alias))
                return Fail("CERT_ALIAS_MISSING: pass --alias. The name on your own document is yours to choose.");
            if (string.IsNullOrWhiteSpace(day))
                return Fail("CERT_DAYKEY_MISSING: pass --day YYYY-MM-DD. The session day is stated, never read off the clock.");

            var store = new DiskStore();
            var ledger = new Ledger(store, ledgerPath);

            // The chain is verified HERE and the answer is carried into the document as it is.
            // A broken chain does not stop the export: it produces a certificate that says
            // ledgerVerified=false, which is the honest outcome and which the public verifier
            // will refuse to call limitRespected.
            var verify = ledger.Verify();
            if (!verify.Ok)
                Console.Error.WriteLine("WARNING: the ledger chain breaks at seq " + verify.BrokenSeq +
                                        " - the certificate will say so rather than hide it.");

            PersistedState state; string stateError;
            if (!PersistedState.TryParse(File.ReadAllText(statePath), out state, out stateError))
                return Fail("CERT_STATE_UNREADABLE: " + stateError);

            var entries = ledger.ReadAll().ToList();
            var version = typeof(Certificate).Assembly.GetName().Version?.ToString() ?? "0.0.0";

            var result = Certificate.Issue(entries, state, new CertificateRequest
            {
                Alias = alias,
                DayKey = day,
                PreviousCertHash = prev,
                IssuerVersion = version,
                IssuerBuildHash = Hashing.Sha256Hex(typeof(Certificate).Assembly.Location).Substring(0, 16),
                DaysCovered = 1,
            }, verify.Ok);

            if (!result.Ok) return Fail(result.Reason);

            outDir ??= Path.Combine(home, "certificates");
            Directory.CreateDirectory(outDir);
            var stem = Path.Combine(outDir, "certificate-" + day);
            File.WriteAllText(stem + ".json", result.Json, new System.Text.UTF8Encoding(false));
            File.WriteAllText(stem + ".html", result.Html, new System.Text.UTF8Encoding(false));

            Console.WriteLine("issued   " + stem + ".json");
            Console.WriteLine("         " + stem + ".html");
            Console.WriteLine("certHash " + result.CertHash);
            Console.WriteLine();
            Console.WriteLine("Now contradict it, with software that is not ours:");
            Console.WriteLine("    pip install deadman-kit");
            Console.WriteLine("    python -m deadman.verify_certificate \"" + stem + ".json\" \"" + ledgerPath + "\"");
            return 0;
        }

        private static int Fail(string reason)
        {
            Console.Error.WriteLine("REFUSED: " + reason);
            return 2;
        }
    }
}
