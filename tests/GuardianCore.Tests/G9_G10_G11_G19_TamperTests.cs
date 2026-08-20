using System;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    /// <summary>G9: a hand-edited sealed config is detected and locks out.
    /// G10: editing the config file while sealed does not take effect.
    /// G11: every config change while sealed is rejected, including a stricter one.
    /// G19: a torn or unknown state file is FAIL_CLOSED, never DISARMED.</summary>
    public class G9_G10_G11_G19_TamperTests : Harness
    {
        [Fact]
        public void G9_editing_the_sealed_config_in_the_state_file_is_detected_and_locks_out()
        {
            Armed("600.00");
            Assert.Contains("\"personalDailyLossLimit\\\":\\\"600.00", StateOnDisk());

            // The trader raises their own limit inside the sealed snapshot, hoping nobody re-hashes it.
            var tampered = StateOnDisk().Replace("personalDailyLossLimit\\\":\\\"600.00", "personalDailyLossLimit\\\":\\\"900.00");
            Assert.NotEqual(StateOnDisk(), tampered);
            Store.PutRaw(StatePath, tampered);

            var restarted = NewGuardian("run-2");

            Assert.Equal(StateKind.Locked, restarted.Status.Kind);
            Assert.False(restarted.Status.EntriesAllowed);
            var mismatch = LastEvent(Ev.SealMismatch);
            Assert.NotNull(mismatch);
            var payload = (JsonObject)mismatch["payload"];
            Assert.NotEqual(payload.GetString("expectedHash"), payload.GetString("actualHash"));
        }

        [Fact]
        public void G9_flipping_a_single_byte_of_the_snapshot_is_enough_to_be_caught()
        {
            Armed("600.00");
            var raw = StateOnDisk();
            var idx = raw.IndexOf("UsDollar", StringComparison.Ordinal);
            Assert.True(idx > 0);
            Store.PutRaw(StatePath, raw.Substring(0, idx) + "UsDollaR" + raw.Substring(idx + 8));

            var restarted = NewGuardian("run-2");
            Assert.Equal(StateKind.Locked, restarted.Status.Kind);
            Assert.True(HasEvent(Ev.SealMismatch));
        }

        [Fact]
        public void G9_an_untouched_seal_verifies_across_a_restart()
        {
            Armed("600.00");
            var restarted = NewGuardian("run-2");

            Assert.Equal(StateKind.Armed, restarted.Status.Kind);
            Assert.True(HasEvent(Ev.SealVerified));
            Assert.False(HasEvent(Ev.SealMismatch));
        }

        [Fact]
        public void G10_editing_the_config_file_while_sealed_locks_out_and_the_sealed_values_stay_in_force()
        {
            Armed("600.00");
            var sealedHash = Guardian.Status.SealHash;

            // A new config file appears on disk with a looser limit.
            Guardian.OnConfigFileObserved(Config("950.00"));

            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);
            var tampered = LastEvent(Ev.ConfigTampered);
            Assert.NotNull(tampered);
            var payload = (JsonObject)tampered["payload"];
            Assert.Equal(sealedHash, payload.GetString("sealedHash"));
            Assert.NotEqual(sealedHash, payload.GetString("onDiskHash"));
            Assert.Contains("personalDailyLossLimit", ((JsonArray)payload["changedKeys"]).Items.Select(i => ((JsonString)i).Value));

            // The seal - not the edited file - is what is still in force.
            Assert.Equal(sealedHash, Guardian.Status.SealHash);
        }

        [Fact]
        public void G10_an_identical_config_file_is_not_tampering()
        {
            Armed("600.00");
            Guardian.OnConfigFileObserved(Config("600.00"));

            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
            Assert.False(HasEvent(Ev.ConfigTampered));
        }

        [Fact]
        public void G10_even_reordered_whitespace_is_not_tampering_because_the_hash_is_canonical()
        {
            Armed("600.00");
            var pretty = Config("600.00").Replace(",", ",\n  ").Replace("{", "{\n  ");
            Guardian.OnConfigFileObserved(pretty);

            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
            Assert.False(HasEvent(Ev.ConfigTampered));
        }

        [Fact]
        public void G11_a_looser_config_change_is_rejected_while_sealed()
        {
            Armed("600.00");
            var result = Guardian.TryChangeConfig(Config("900.00"));

            Assert.False(result.Ok);
            Assert.Contains("sealed", result.ToString());
            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
            Assert.True(HasEvent(Ev.ConfigChangeRejected));
        }

        [Fact]
        public void G11_a_STRICTER_config_change_is_rejected_too()
        {
            // The point of a commitment device: there is nothing to debate at 14:30, in either direction.
            Armed("600.00");
            var result = Guardian.TryChangeConfig(Config("300.00"));

            Assert.False(result.Ok);
            var rejected = LastEvent(Ev.ConfigChangeRejected);
            Assert.NotNull(rejected);
            var payload = (JsonObject)rejected["payload"];
            Assert.Contains("personalDailyLossLimit", ((JsonArray)payload["changedKeys"]).Items.Select(i => ((JsonString)i).Value));
            Assert.Equal("600.00", ExtractSealedLimit());
        }

        [Fact]
        public void G11_arming_again_while_sealed_is_a_change_attempt_and_is_rejected()
        {
            Armed("600.00");
            var result = Guardian.Arm(Config("900.00"));

            Assert.False(result.Ok);
            Assert.True(HasEvent(Ev.ConfigChangeRejected));
            Assert.Equal("600.00", ExtractSealedLimit());
        }

        [Fact]
        public void G11_every_rejected_attempt_is_recorded_so_the_pattern_is_visible_later()
        {
            Armed("600.00");
            Guardian.TryChangeConfig(Config("700.00"));
            Guardian.TryChangeConfig(Config("800.00"));
            Guardian.TryChangeConfig(Config("900.00"));

            Assert.Equal(3, Events().Count(e => e == Ev.ConfigChangeRejected));
        }

        [Fact]
        public void G11_an_unparseable_change_is_still_recorded_as_an_attempt()
        {
            Armed("600.00");
            var result = Guardian.TryChangeConfig("{ not json");

            Assert.False(result.Ok);
            var rejected = LastEvent(Ev.ConfigChangeRejected);
            Assert.Equal("unparseable", ((JsonObject)rejected["payload"]).GetString("offeredHash"));
        }

        [Theory]
        [InlineData("{ \"schemaVersion\": 1, ", "truncated json")]
        [InlineData("{ \"schemaVersion\": 99, \"state\": \"ARMED\", \"lastSeenUtc\": \"2026-08-19T20:00:00.000Z\", \"lastMonotonicMs\": 1 }", "unknown schema")]
        [InlineData("{ \"schemaVersion\": 1, \"state\": \"SOMETHING\", \"lastSeenUtc\": \"2026-08-19T20:00:00.000Z\", \"lastMonotonicMs\": 1 }", "unknown state name")]
        [InlineData("{ \"schemaVersion\": 1, \"state\": \"ARMED\", \"lastMonotonicMs\": 1 }", "no timestamp")]
        [InlineData("", "empty file")]
        public void G19_a_corrupt_state_file_is_fail_closed_and_never_disarmed(string content, string why)
        {
            Store.PutRaw(StatePath, content);
            var guardian = NewGuardian("run-2");

            Assert.True(guardian.Status.Kind == StateKind.FailClosed, why + " should fail closed, got " + guardian.Status.Kind);
            Assert.False(guardian.Status.EntriesAllowed);
            Assert.NotEqual(StateKind.Disarmed, guardian.Status.Kind);
            Assert.True(HasEvent(Ev.StateCorrupt));
        }

        [Fact]
        public void G19_a_missing_state_file_on_a_fresh_install_is_disarmed_not_fail_closed()
        {
            // Nothing to protect yet, and nothing was lost: this is the one benign case.
            var guardian = NewGuardian();
            Assert.Equal(StateKind.Disarmed, guardian.Status.Kind);
        }

        [Fact]
        public void G19_deleting_the_state_file_after_arming_does_not_resurrect_a_dead_seal()
        {
            Armed("600.00");
            LoseExactly(600.00m);
            Assert.Equal(StateKind.Locked, Guardian.Status.Kind);

            // Wiping state is the SPEC 17.4 case: not prevented, but it cannot produce a protected
            // session either - the guardian comes back with no seal and refuses to pretend otherwise.
            Store.Delete(StatePath);
            var guardian = NewGuardian("run-3");

            Assert.Equal(StateKind.Disarmed, guardian.Status.Kind);
            Assert.Null(guardian.Status.SealHash);
            // and the hole is visible in the ledger, which was not deleted
            Assert.True(HasEvent(Ev.LimitBreached));
        }

        [Fact]
        public void G19_a_broken_ledger_chain_is_fail_closed()
        {
            Armed("600.00");
            var raw = Store.GetRaw(LedgerPath);
            Store.PutRaw(LedgerPath, raw.Replace("\"ARMED\"", "\"DISARMED\""));

            var guardian = NewGuardian("run-2");

            Assert.Equal(StateKind.FailClosed, guardian.Status.Kind);
            Assert.True(HasEvent(Ev.LedgerVerifyFailed));
        }

        private string ExtractSealedLimit()
        {
            var raw = StateOnDisk();
            var marker = "personalDailyLossLimit\\\":\\\"";
            var idx = raw.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(idx > 0, "sealed limit not found in state");
            return raw.Substring(idx + marker.Length, 6);
        }
    }
}
