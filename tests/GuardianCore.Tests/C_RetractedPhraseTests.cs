// RETRACTED PHRASES - a sentence we took back must not come back.
//
// WHY THIS EXISTS, with the case that produced it: on 2026-09-02 the public site stopped saying
// "a hand-written number can only ever go up" - literally false, since nothing obliges a hand-written
// number to go down - and the same false sentence went on standing in README.md until 2026-09-03.
// Nobody was careless. The correction travelled to the copy and never came back to the source, and
// no mechanism existed that could have noticed. Retraction is the one kind of edit where the old
// text is known EXACTLY, so it is the one kind that a machine can hold.
//
// THE HARD PART, and how it is resolved - a phrase can appear legitimately INSIDE a retraction, and
// a test that has to be switched off the first time it is inconvenient is not a test. Two mechanical
// decisions, neither of them a guess about context:
//
//   (a) WHAT is banned is the ASSERTIVE FORM, not the topic. "cancelled on sight" names a mechanism
//       and appears legitimately seven times in this repo - past tense, in quotes, negated. "is
//       cancelled on sight" and "are cancelled on sight" are the claim being MADE, and every
//       occurrence of those is false today. The narrowing does the work a context heuristic would
//       have done, and it does it in the string, where it cannot be argued with.
//
//   (b) WHERE it is enforced is LIVING documents only. A dated record is SUPPOSED to contain what we
//       used to say - that is what makes it a record, and rewriting it would be falsifying the log
//       (the house rule: a living document is corrected, a dated one is annotated). Frozen is decided
//       by the filename carrying a date, plus four named paths. It is ONE boundary shared by every
//       phrase, not an exception per phrase.
//
// THE RULE APPLIED BEFORE WRITING IT - is there a change cheaper than the real fix that turns the red
// green? Inside a living file: no. There is no quotation escape, no "we used to say" escape, no
// per-line allowlist. The sentence has to change. The only cheat available is declaring a living file
// a frozen record, which is a line in this file asserting that a README is a historical document -
// visible in the diff and impossible to write with a straight face.
//
// WHY "26 of 26" IS NOT ON THE LIST, said out loud because its absence looks like an oversight:
// C_ConformanceCountTests already measures that property directly - it compares the published number
// against the SPEC table and goes red if they disagree. A string ban would be a PROXY for a property
// that already has a real test, and it would fire on that test's own header comment. Where a real
// test exists, the string ban is the weaker instrument and does not get added.
//
// WHAT IT DOES NOT MEASURE: it holds sentences we already caught. It says nothing about the next
// false sentence, which will be phrased in words nobody has banned yet. It is a ratchet, not a guard.
//
// SCOPE, and the gap that stays open: this scans THIS repository. The public site is a separate
// repository on a separate remote, and the phrases live there too - that is where this one was
// corrected first. No test here can reach it without measuring the machine it runs on instead of the
// property. That check is manual today, and it is written down in docs/frases-retiradas-20260903.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GuardianCore.Tests
{
    public class C_RetractedPhraseTests
    {
        /// <summary>A sentence that was taken back, in the forms that ASSERT it.</summary>
        private sealed class Retraction
        {
            public Retraction(string why, string when, params string[] forms)
            {
                Why = why; When = when; Forms = forms;
            }

            /// <summary>Literal forms, matched case-insensitively. Each one asserts the claim.</summary>
            public string[] Forms { get; }
            /// <summary>Why it was retracted - travels with the ban so nobody has to go looking.</summary>
            public string Why { get; }
            public string When { get; }
        }

        private static readonly Retraction[] Retracted =
        {
            new Retraction(
                why: "literally false: nothing obliges a hand-written number to go DOWN, which is the " +
                     "actual defect. It could always have been edited down; nothing made it happen.",
                when: "2026-09-02 on the public site, 2026-09-03 here",
                "a hand-written number can only ever go up",
                "can only ever go up"),

            new Retraction(
                why: "cancel-on-observation was removed by a916bba on 2026-08-27, after it cancelled the " +
                     "guardian's own flatten orders and four orders of the trader's. G8 is NOT " +
                     "IMPLEMENTED (A12). Nothing is cancelled while locked. The past tense and the " +
                     "quoted mention stay legitimate; the present-tense claim does not.",
                when: "2026-08-27",
                "is cancelled on sight",
                "are cancelled on sight",
                "cancels every new order",
                "every new order is cancelled"),

            new Retraction(
                why: "a test count climbs on its own, says nothing about coverage, and is wrong again " +
                     "the following week. It was removed rather than updated on 2026-09-01.",
                when: "2026-09-01",
                "137 tests",
                "137 collected test cases"),

            new Retraction(
                why: "intent vocabulary asserting an effect there is no mechanism to produce: 2,912 " +
                     "types were scanned and no pre-submit hook exists, so nothing stops an order from " +
                     "reaching the broker. Naming the phrase in order to warn about it is legitimate; " +
                     "asserting it is not.",
                when: "2026-09-03",
                "it blocks new entries",
                "blocks new entries until",
                "and blocks new entries"),

            new Retraction(
                why: "measured: the twelve rejections are 4 distinct orderIds, and two of them were " +
                     "SellShort and Buy, which OPEN. The event carries no quantity and no position, so " +
                     "whether any of them was an exit is not in the record at all.",
                when: "2026-09-02",
                "twelve of the trader's own exits",
                "twelve of the trader's exits"),
        };

        // ---- scope -------------------------------------------------------------------------------

        private static readonly string[] TextExtensions =
            { ".cs", ".md", ".py", ".ps1", ".csproj", ".sln", ".jsonl", ".txt", ".yml", ".yaml", ".html" };

        private static readonly string[] SkipDirectories =
            { ".git", "bin", "obj", ".vs", "node_modules", "TestResults" };

        /// <summary>
        /// A dated record is SUPPOSED to contain what we used to say. Correcting it would falsify the
        /// log. One boundary, shared by every phrase - never an exception per phrase.
        /// </summary>
        private static bool IsFrozenRecord(string relative)
        {
            var name = Path.GetFileName(relative);

            // docs/site-corrections-20260901.md, docs/live-test-findings-20260826.md, ...
            if (Regex.IsMatch(name, @"[-_]20\d{6}\.")) return true;

            switch (relative)
            {
                // Append-only ledger of amendments; every entry is dated and quotes what it replaces.
                case "AMENDMENTS.md":
                // Dated evidence from the platform probes.
                case "nt/STEP3_FINDINGS.md":
                // This file necessarily contains every banned phrase - it is the list.
                case "tests/GuardianCore.Tests/C_RetractedPhraseTests.cs":
                    return true;
            }

            return relative.StartsWith("nt/probe/evidence/", StringComparison.Ordinal)
                || relative.StartsWith("nt/backups/", StringComparison.Ordinal);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null &&
                   !(File.Exists(Path.Combine(dir.FullName, "SPEC.md")) &&
                     Directory.Exists(Path.Combine(dir.FullName, "src"))))
            {
                dir = dir.Parent;
            }
            Assert.True(dir != null, "could not locate the repository root from " + AppContext.BaseDirectory);
            return dir!.FullName;
        }

        /// <summary>Every living text file, as a repo-relative path with forward slashes.</summary>
        private static List<string> LivingFiles(string root)
        {
            var found = new List<string>();

            void Walk(string dir)
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (SkipDirectories.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase))
                        continue;
                    Walk(sub);
                }

                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    if (!TextExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        continue;

                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (IsFrozenRecord(relative)) continue;
                    found.Add(relative);
                }
            }

            Walk(root);
            return found;
        }

        // ---- the controls ------------------------------------------------------------------------
        //
        // A sweep that scans nothing is green, and a matcher that matches nothing is green. Both of
        // those are the failure this test is most likely to have, and neither of them announces
        // itself. So the sweep is measured before it is trusted.

        [Fact]
        public void C_The_sweep_actually_reaches_the_files_it_claims_to_scan()
        {
            var root = RepoRoot();
            var files = LivingFiles(root);

            Assert.True(files.Count >= 80,
                "the sweep found only " + files.Count + " living files, which is too few to be scanning " +
                "this repository. A sweep that reaches nothing passes silently.");

            foreach (var required in new[] { "README.md", "SPEC.md" })
                Assert.Contains(required, files);

            foreach (var area in new[] { "src/", "docs/", "nt/", "tests/" })
                Assert.True(files.Any(f => f.StartsWith(area, StringComparison.Ordinal)),
                    "the sweep reached no file under " + area);

            // And the boundary is real: dated records exist and are excluded on purpose.
            Assert.DoesNotContain("AMENDMENTS.md", files);
            Assert.DoesNotContain("docs/site-corrections-20260901.md", files);
        }

        [Fact]
        public void C_The_matcher_fires_on_a_sentence_that_asserts_a_retracted_claim()
        {
            // The positive control: every banned form must be detected in a line that contains it,
            // including in a casing nobody used. If this goes green by accident, so does the sweep.
            foreach (var retraction in Retracted)
            {
                Assert.NotEmpty(retraction.Forms);
                Assert.False(string.IsNullOrWhiteSpace(retraction.Why),
                    "a banned phrase without its reason is a rule nobody can apply");

                foreach (var form in retraction.Forms)
                {
                    var line = "The documentation says " + form.ToUpperInvariant() + ", which it should not.";
                    Assert.Contains(form, line, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        // ---- the assertion -----------------------------------------------------------------------

        [Fact]
        public void C_No_living_file_repeats_a_sentence_that_was_retracted()
        {
            var root = RepoRoot();
            var hits = new List<string>();

            foreach (var relative in LivingFiles(root))
            {
                var lines = File.ReadAllLines(Path.Combine(root, relative));
                for (var i = 0; i < lines.Length; i++)
                {
                    foreach (var retraction in Retracted)
                    foreach (var form in retraction.Forms)
                    {
                        if (lines[i].IndexOf(form, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        hits.Add(
                            relative + ":" + (i + 1) + "  \"" + form + "\"" + Environment.NewLine +
                            "      retracted " + retraction.When + " - " + retraction.Why + Environment.NewLine +
                            "      line: " + Excerpt(lines[i], form));
                    }
                }
            }

            Assert.True(hits.Count == 0,
                "a retracted sentence is standing in a living file. Change the sentence - do not add an " +
                "exception, and do not declare the file a frozen record." + Environment.NewLine +
                Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, hits));
        }

        private static string Excerpt(string line, string form)
        {
            var at = line.IndexOf(form, StringComparison.OrdinalIgnoreCase);
            var from = Math.Max(0, at - 60);
            var to = Math.Min(line.Length, at + form.Length + 60);
            return (from > 0 ? "..." : "") + line.Substring(from, to - from).Trim() + (to < line.Length ? "..." : "");
        }
    }
}
