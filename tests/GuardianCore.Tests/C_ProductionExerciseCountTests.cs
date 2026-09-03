// THE SECOND NUMBER: how many guarantees have ever been EXERCISED IN PRODUCTION.
//
// The first number (C_ConformanceCountTests) answers "is it implemented" - a property of the code.
// This one answers a different question that nobody in this market publishes: "has the world ever
// made this rule do its job". A guarantee can be implemented, unit-tested, green forever, and have
// never once been reached by a real account. G3 and G4 are exactly that, measured on 2026-09-03.
//
// WHY THE MARKER IS POSITIVE, AND THIS IS THE WHOLE DESIGN. A negative marker - counting the rows
// tagged NOT EXERCISED - would let this number be BORN AT 26 and fall only when somebody does the
// work of disproving a row. That is the "26 of 26" defect rebuilt with a new name: a figure that
// starts flattering and can only be lowered by effort nobody is obliged to spend.
//
// With a POSITIVE marker the default is "not established", which is the fail-closed direction, and
// the number can only RISE when somebody measures a guarantee against the production ledger and
// cites where they saw it. Silence counts against us, which is the only arrangement under which a
// self-reported figure means anything.
//
// THE RULE APPLIED BEFORE WRITING IT - is there a change cheaper than the real fix that turns the
// red green? To raise the number you must write PRODUCTION EVIDENCE into a versioned SPEC row, next
// to the guarantee's own text, WITH a ledger seq citation, and publish the new figure in README.
// That edit IS the claim, in a diff, where a reader finds it. There is no gesture that inflates this
// number quietly - the cheap move is to leave a row unmarked, and leaving it unmarked is the honest
// answer when nobody has looked.
//
// WHAT IT DOES NOT MEASURE, said so nobody signs with it:
//   - It does not check that the evidence is GOOD. It checks that a claim of evidence exists, is
//     versioned, and carries a pointer. A human still has to be right.
//   - "Exercised" is not "correct". G3's own history is the proof in reverse: exercised code can be
//     enforcing a number its record cannot verify.
//   - It says nothing about coverage of the market's conditions - only about this ledger, on this
//     machine, on a simulated account.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GuardianCore.Tests
{
    public class C_ProductionExerciseCountTests
    {
        /// <summary>Positive marker. Deliberately NOT a substring of the negative one below, so a
        /// row that says it has NOT been exercised can never be counted as evidence.</summary>
        private const string EvidenceMarker = "PRODUCTION EVIDENCE";

        /// <summary>The negative marker, kept only so the controls can prove the two never collide.</summary>
        private const string NotExercisedMarker = "NOT EXERCISED IN PRODUCTION";

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

        /// <summary>Every row of the SPEC section 15 table, as (id, whole line).</summary>
        private static List<(string Id, string Line)> GuaranteeRows()
        {
            var rows = new List<(string, string)>();
            foreach (var line in File.ReadAllLines(Path.Combine(RepoRoot(), "SPEC.md")))
            {
                var m = Regex.Match(line, @"^\|\s*(G\d+[a-z]?)\b(.*?)\|(.*)$");
                if (m.Success) rows.Add((m.Groups[1].Value, line));
            }
            return rows;
        }

        private static bool HasEvidence(string line) =>
            line.Contains(EvidenceMarker, StringComparison.Ordinal);

        // ---- controls ----------------------------------------------------------------------------

        [Fact]
        public void C_The_two_markers_can_never_be_confused_for_each_other()
        {
            // If EvidenceMarker were ever a substring of NotExercisedMarker, every row we measured as
            // NOT exercised would be counted as evidence and the number would read 26 while the table
            // said the opposite. It is the exact inversion this test exists to prevent, so it is
            // asserted rather than trusted to stay true.
            Assert.DoesNotContain(EvidenceMarker, NotExercisedMarker, StringComparison.Ordinal);

            foreach (var (id, line) in GuaranteeRows())
                Assert.False(HasEvidence(line) && line.Contains(NotExercisedMarker, StringComparison.Ordinal),
                    id + " claims production evidence AND says it has not been exercised in production. "
                       + "One of the two is wrong and a reader cannot tell which.");
        }

        [Fact]
        public void C_Every_claim_of_production_evidence_cites_where_it_was_seen()
        {
            // A marker with no pointer is an assertion wearing a measurement's clothes - the thing
            // this whole repository exists to refuse. Production claims here come from one place,
            // the hash-chained ledger, and a ledger entry has a sequence number.
            foreach (var (id, line) in GuaranteeRows().Where(r => HasEvidence(r.Line)))
                Assert.True(Regex.IsMatch(line, @"\bseq\s*\d"),
                    id + " is marked " + EvidenceMarker + " but cites no ledger seq. Say where it was "
                       + "seen, or take the marker off.");
        }

        [Fact]
        public void C_The_table_parses_and_the_sweep_is_not_blind()
        {
            var rows = GuaranteeRows();
            Assert.True(rows.Count > 0,
                "no guarantee rows found in SPEC.md section 15 - the table shape changed and this "
                + "test is now blind, which is worse than a wrong number");
            Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());
        }

        // ---- the number --------------------------------------------------------------------------

        [Fact]
        public void C_The_published_production_exercise_number_is_the_one_the_spec_table_supports()
        {
            var rows = GuaranteeRows();
            var total = rows.Count;
            var exercised = rows.Count(r => HasEvidence(r.Line));
            var expected = exercised + " of " + total + " named guarantees exercised in production";

            var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
            Assert.True(readme.Contains(expected, StringComparison.Ordinal),
                "README.md must publish exactly \"" + expected + "\". The SPEC table carries "
                + EvidenceMarker + " on " + exercised + " of " + total + " rows. Update the published "
                + "figure - do NOT mark a guarantee exercised to make this pass.");
        }

        [Fact]
        public void C_Every_guarantee_claimed_exercised_is_named_where_the_number_is_published()
        {
            // The exercised set is the SMALL side here, so naming it is cheap and it is the claim
            // actually being made. A bare "3 of 26" invites the reader to assume which three.
            var claimed = GuaranteeRows().Where(r => HasEvidence(r.Line)).Select(r => r.Id).ToList();
            var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));

            foreach (var id in claimed)
                Assert.True(readme.Contains(id, StringComparison.Ordinal),
                    "README.md publishes a production-exercise number but never names " + id
                    + ", which the SPEC table counts towards it.");
        }
    }
}
