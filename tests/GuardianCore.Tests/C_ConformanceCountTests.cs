// The conformance number, computed instead of typed.
//
// "26 of 26 named guarantees implemented" sat in README.md as a HAND-WRITTEN LITERAL. Nobody
// computed it, so it could never go down. A number that can only ever say 26 is not a measurement -
// it is an assertion wearing a measurement's clothes, and it stayed at 26 for six days after G8
// stopped being implemented, while the public site repeated it.
//
// So the count now comes from the ONE place where a guarantee is defined - the SPEC section 15 table -
// where a row can be marked NOT IMPLEMENTED, and this test fails if the published statement does not
// match what that table says.
//
// WHY THE SPEC TABLE AND NOT A SEPARATE STATUS FILE: two files drift, and the one that gets fixed is
// never the one that gets read. The definition and the status are the same row.
//
// THE RULE APPLIED BEFORE WRITING IT - is there a change cheaper than the real fix that turns the red
// green? The red is "README does not match the SPEC table". The cheapest way to clear it is to edit
// the published number and name the missing guarantee, WHICH IS THE FIX. Marking G8 implemented in
// the SPEC would also clear it, but that is not cheaper - it is the same lie, moved into a versioned
// table where it shows up in a diff next to the guarantee's own text. There is no gesture that makes
// this green while leaving a false claim where a reader finds it.
//
// WHAT IT DOES NOT MEASURE, said so nobody signs with it: it does not check that an implemented
// guarantee is CORRECTLY implemented. G8's own history is the proof that a guarantee can have three
// passing tests and still be unimplemented. This test measures that WHAT WE PUBLISH MATCHES WHAT WE
// RECORD - nothing more, and that is exactly the gap that went unnoticed for six days.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GuardianCore.Tests
{
    public class C_ConformanceCountTests
    {
        private const string NotImplementedMarker = "NOT IMPLEMENTED";

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

        /// <summary>Every row of the SPEC section 15 table, as (id, implemented).</summary>
        private static List<(string Id, bool Implemented)> GuaranteesFromSpec()
        {
            var spec = File.ReadAllLines(Path.Combine(RepoRoot(), "SPEC.md"));
            var rows = new List<(string, bool)>();
            foreach (var line in spec)
            {
                var m = Regex.Match(line, @"^\|\s*(G\d+[a-z]?)\b(.*?)\|(.*)$");
                if (!m.Success) continue;
                var id = m.Groups[1].Value;
                var implemented = !line.Contains(NotImplementedMarker, StringComparison.Ordinal);
                rows.Add((id, implemented));
            }
            return rows;
        }

        [Fact]
        public void C_The_spec_table_is_the_only_source_and_it_parses()
        {
            var rows = GuaranteesFromSpec();
            Assert.True(rows.Count > 0, "no guarantee rows were found in SPEC.md section 15 - "
                                        + "the table shape changed and this test is now blind, "
                                        + "which is worse than a wrong number");
            Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());
        }

        [Fact]
        public void C_The_published_conformance_number_is_the_one_the_spec_table_supports()
        {
            var rows = GuaranteesFromSpec();
            var total = rows.Count;
            var implemented = rows.Count(r => r.Implemented);
            var expected = implemented + " of " + total + " named guarantees implemented";

            var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
            Assert.True(readme.Contains(expected, StringComparison.Ordinal),
                        "README.md must publish exactly \"" + expected + "\". The SPEC table says "
                        + implemented + " of " + total + " are implemented. Update the published "
                        + "number - do NOT mark a guarantee implemented to make this pass.");
        }

        [Fact]
        public void C_Every_unimplemented_guarantee_is_named_where_the_number_is_published()
        {
            var missing = GuaranteesFromSpec().Where(r => !r.Implemented).Select(r => r.Id).ToList();
            var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));

            // A count without the names is the same defect one step smaller: "25 of 26" tells a
            // reader that something is missing and refuses to say what, which is the shape of a
            // number that exists to be quoted rather than checked.
            foreach (var id in missing)
                Assert.True(readme.Contains(id, StringComparison.Ordinal),
                            "README.md publishes a conformance number short of the total but never "
                            + "names " + id + ". The missing guarantee must be named where the "
                            + "number is published.");
        }
    }
}
