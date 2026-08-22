// A10 has a hole it cannot close by itself, and this is the patch.
//
// The rule says user-facing text is single-source. Some surfaces cannot obey it: install.ps1 is
// PowerShell and cannot reference GuardianCore, so its copy of any sentence is unavoidable. A rule
// that cannot be obeyed protects nothing - it just moves the failure somewhere nobody is looking.
//
// It already happened. "NOT PROTECTED" was removed from the status window and went on greeting the
// reader from the installer's closing text - the first thing anyone installing this reads - for
// twenty minutes, and only a passing glance caught it.
//
// So the uncoverable surfaces get a CHECK instead of the rule: this test walks the repository and
// goes red if a retired phrase is still sitting in a script or a source file. Cheap, mechanical, and
// it fires on the next one without anybody having to remember.
//
// Note it reads the phrases from Messages.Retired rather than spelling them here. A test that
// hard-coded them would contain the very strings it forbids, and would have to exempt itself - an
// exemption that then covers whatever else drifts into this file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardianCore;

namespace GuardianCore.Tests
{
    public class C_RetiredPhrasesTests
    {
        /// <summary>Files that are ALLOWED to contain retired wording, each for a stated reason.
        /// A declared exception is fine; a tacit one is the defect.</summary>
        private static readonly string[] Allowed =
        {
            // declares them, so it must name them
            Path.Combine("src", "GuardianCore", "Messages.cs"),
            // this file: reads them from Messages at runtime, never spells them - listed anyway so a
            // future edit that does spell one is caught by review rather than silently permitted
            Path.Combine("tests", "GuardianCore.Tests", "C_RetiredPhrasesTests.cs"),
        };

        /// <summary>Documentation is exempt by design: SPEC, AMENDMENTS, the READMEs and the proposals
        /// DISCUSS retired wording - they quote it to explain what was wrong with it and why it went.
        /// Forbidding that would delete the record of the correction, which is the opposite of the
        /// point. Only files that PRODUCE text a user sees are scanned.</summary>
        private static readonly string[] ScannedExtensions = { ".cs", ".ps1" };

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
            return dir.FullName;
        }

        private static IEnumerable<string> FilesToScan(string root)
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => ScannedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "backups" + Path.DirectorySeparatorChar))
                .Where(f => !Allowed.Any(a => f.EndsWith(a, StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public void No_retired_wording_survives_anywhere_that_produces_user_facing_text()
        {
            var root = RepoRoot();
            var offences = new List<string>();

            foreach (var file in FilesToScan(root))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                for (var i = 0; i < lines.Length; i++)
                {
                    foreach (var phrase in Messages.Retired)
                    {
                        if (lines[i].IndexOf(phrase, StringComparison.Ordinal) >= 0)
                        {
                            offences.Add(file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar) +
                                         ":" + (i + 1) + "  ->  " + lines[i].Trim());
                        }
                    }
                }
            }

            Assert.True(offences.Count == 0,
                "Retired wording is still in front of a user. Either update the text or, if it is being " +
                "quoted to explain history, move that discussion into documentation:" +
                Environment.NewLine + string.Join(Environment.NewLine, offences));
        }

        /// <summary>The list has to be non-empty and has to hold the one that already escaped once, or
        /// the check above is a green that depends on nothing.</summary>
        [Fact]
        public void The_retired_list_is_not_empty_and_carries_the_phrase_that_already_escaped()
        {
            Assert.NotEmpty(Messages.Retired);
            Assert.Contains(Messages.Retired, p => Messages.HeadlineCannotSee != p && p.Contains("PROTECTED"));
        }

        /// <summary>And nothing currently in use may be on the retired list - otherwise retiring a
        /// phrase while still shipping it would pass both tests.</summary>
        [Fact]
        public void Nothing_still_in_use_is_listed_as_retired()
        {
            var live = new[]
            {
                Messages.HeadlineArmed, Messages.HeadlineLocked,
                Messages.HeadlineCannotSee, Messages.HeadlineNotArmed
            };

            foreach (var phrase in Messages.Retired)
                Assert.DoesNotContain(live, l => l.IndexOf(phrase, StringComparison.Ordinal) >= 0);
        }
    }
}
