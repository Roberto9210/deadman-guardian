using System;
using System.Collections.Generic;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    /// <summary>G1: no config with a missing, unknown or invalid field ever arms.
    /// G2: personalLimit &gt;= firmLimit never arms, equality included.
    /// G12: the session boundary is DST-aware and the zone resolves by both paths.</summary>
    public class G1_G2_G12_ConfigTests
    {
        public static string Valid(string overrides = null)
        {
            var baseJson = @"{
              ""schemaVersion"": 1,
              ""accounts"": [""Sim101""],
              ""currency"": ""UsDollar"",
              ""firmDailyLossLimit"": ""1000.00"",
              ""personalDailyLossLimit"": ""600.00"",
              ""sessionResetTimeZone"": ""America/Chicago"",
              ""sessionResetLocalTime"": ""17:00"",
              ""ledgerPath"": ""guardian/ledger.jsonl"",
              ""statePath"": ""guardian/state.json"",
              ""pnlToleranceUsd"": ""5.00""
            }";
            return overrides ?? baseJson;
        }

        private static string WithField(string key, string rawJsonValue)
        {
            var json = Valid();
            var start = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            Assert.True(start >= 0, "key not in fixture: " + key);
            var colon = json.IndexOf(':', start);
            var end = json.IndexOfAny(new[] { ',', '}' }, colon);
            return json.Substring(0, colon + 1) + " " + rawJsonValue + json.Substring(end);
        }

        private static string WithoutField(string key)
        {
            var json = Valid();
            var start = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            var colon = json.IndexOf(':', start);
            var end = json.IndexOfAny(new[] { ',', '}' }, colon);

            var head = json.Substring(0, start);
            var tail = json.Substring(end + (json[end] == ',' ? 1 : 0));
            // Removing the last member would leave the previous member's comma dangling, which would
            // make the fixture invalid JSON and test the parser instead of the missing-key rule.
            if (json[end] == '}')
            {
                var lastComma = head.LastIndexOf(',');
                if (lastComma >= 0) head = head.Substring(0, lastComma) + head.Substring(lastComma + 1);
            }
            return head + tail;
        }

        [Fact]
        public void G1_the_reference_config_is_accepted()
        {
            var r = GuardianConfig.Parse(Valid());
            Assert.True(r.Ok, r.ToString());
            Assert.Equal(600m, r.Config.PersonalDailyLossLimit);
            Assert.Equal(1000m, r.Config.FirmDailyLossLimit);
            Assert.Equal(new TimeSpan(17, 0, 0), r.Config.SessionResetLocalTime);
            Assert.Single(r.Config.Accounts);
        }

        [Theory]
        [InlineData("schemaVersion")]
        [InlineData("accounts")]
        [InlineData("currency")]
        [InlineData("firmDailyLossLimit")]
        [InlineData("personalDailyLossLimit")]
        [InlineData("sessionResetTimeZone")]
        [InlineData("sessionResetLocalTime")]
        [InlineData("ledgerPath")]
        [InlineData("statePath")]
        [InlineData("pnlToleranceUsd")]
        public void G1_every_missing_field_is_a_rejection_and_never_a_default(string key)
        {
            var r = GuardianConfig.Parse(WithoutField(key));
            Assert.False(r.Ok);
            Assert.Contains(r.Reasons, x => x.Contains(key));
        }

        [Theory]
        // key, raw JSON value, expected fragment of the reason
        [InlineData("schemaVersion", "2", "unsupported schemaVersion")]
        [InlineData("schemaVersion", "\"1\"", "must be an integer")]
        [InlineData("accounts", "[]", "must not be empty")]
        [InlineData("accounts", "[\"Sim101\", \"Sim101\"]", "duplicates")]
        [InlineData("accounts", "\"Sim101\"", "must be an array")]
        [InlineData("accounts", "[\"\"]", "non-empty strings")]
        [InlineData("currency", "\"\"", "non-empty string")]
        [InlineData("firmDailyLossLimit", "1000", "decimal string, not a number")]
        [InlineData("firmDailyLossLimit", "\"0.00\"", "greater than 0")]
        [InlineData("firmDailyLossLimit", "\"-50.00\"", "greater than 0")]
        [InlineData("firmDailyLossLimit", "\"1000.005\"", "at most 2 places")]
        [InlineData("firmDailyLossLimit", "\"1,000.00\"", "at most 2 places")]
        [InlineData("personalDailyLossLimit", "\"abc\"", "at most 2 places")]
        [InlineData("personalDailyLossLimit", "\" 600.00 \"", "at most 2 places")]
        [InlineData("pnlToleranceUsd", "\"-1.00\"", "zero or greater")]
        [InlineData("sessionResetTimeZone", "\"Europe/Madrid\"", "unsupported time zone")]
        [InlineData("sessionResetTimeZone", "\"Central Standard Time\"", "unsupported time zone")]
        [InlineData("sessionResetLocalTime", "\"5pm\"", "must be HH:mm")]
        [InlineData("sessionResetLocalTime", "\"25:00\"", "must be HH:mm")]
        [InlineData("ledgerPath", "\"\"", "non-empty path")]
        [InlineData("statePath", "\"  \"", "non-empty path")]
        public void G1_every_invalid_value_is_a_rejection_with_a_reason(string key, string rawValue, string expected)
        {
            var r = GuardianConfig.Parse(WithField(key, rawValue));
            Assert.False(r.Ok);
            Assert.Contains(r.Reasons, x => x.Contains(expected));
        }

        [Fact]
        public void G1_an_unknown_key_is_a_rejection_because_a_typo_is_a_rule_that_is_not_active()
        {
            var json = Valid().Replace("\"pnlToleranceUsd\"", "\"pnlTolleranceUsd\"");
            var r = GuardianConfig.Parse(json);
            Assert.False(r.Ok);
            Assert.Contains(r.Reasons, x => x.Contains("unknown key 'pnlTolleranceUsd'"));
            Assert.Contains(r.Reasons, x => x.Contains("missing key 'pnlToleranceUsd'"));
        }

        [Fact]
        public void G1_all_reasons_are_reported_not_just_the_first()
        {
            var json = @"{ ""schemaVersion"": 1, ""accounts"": [] }";
            var r = GuardianConfig.Parse(json);
            Assert.False(r.Ok);
            Assert.True(r.Reasons.Count >= 8, "expected every missing key to be listed, got: " + r);
        }

        [Theory]
        [InlineData("garbage")]
        [InlineData("{ \"schemaVersion\": 1, }")]
        [InlineData("{ \"a\": 1 } trailing")]
        [InlineData("[]")]
        [InlineData("")]
        public void G1_unparseable_config_is_a_rejection(string text)
        {
            Assert.False(GuardianConfig.Parse(text).Ok);
        }

        [Fact]
        public void G1_duplicate_keys_are_rejected_because_the_file_is_ambiguous()
        {
            var json = Valid().Replace("\"currency\": \"UsDollar\",", "\"currency\": \"UsDollar\", \"currency\": \"Euro\",");
            var r = GuardianConfig.Parse(json);
            Assert.False(r.Ok);
            Assert.Contains(r.Reasons, x => x.Contains("duplicate key"));
        }

        [Theory]
        [InlineData("600.00", "600.00")]   // equal - the boundary case
        [InlineData("1500.00", "1000.00")] // looser than the firm
        public void G2_personal_limit_must_be_strictly_less_than_the_firm_limit(string personal, string firm)
        {
            var json = WithField("personalDailyLossLimit", "\"" + personal + "\"");
            var start = json.IndexOf("\"firmDailyLossLimit\"", StringComparison.Ordinal);
            var colon = json.IndexOf(':', start);
            var end = json.IndexOfAny(new[] { ',', '}' }, colon);
            json = json.Substring(0, colon + 1) + " \"" + firm + "\"" + json.Substring(end);

            var r = GuardianConfig.Parse(json);
            Assert.False(r.Ok);
            Assert.Contains(r.Reasons, x => x.Contains("STRICTLY LESS"));
        }

        [Fact]
        public void G2_one_cent_of_gap_is_enough()
        {
            var json = WithField("personalDailyLossLimit", "\"999.99\"");
            Assert.True(GuardianConfig.Parse(json).Ok);
        }

        // ---- G12: time zone and session boundary ----

        [Fact]
        public void G12_iana_id_resolves_directly_on_this_runtime()
        {
            Assert.True(TimeZoneMap.TryResolve("America/Chicago", out var zone, out var err), err);
            Assert.NotNull(zone);
        }

        [Fact]
        public void G12_fallback_path_resolves_when_iana_throws_as_it_does_on_net_framework()
        {
            // Simulates .NET Framework 4.8 inside NT8: IANA ids are not found, Windows ids are.
            // Verified on the development machine:
            //   FindSystemTimeZoneById("America/Chicago") -> TimeZoneNotFoundException
            Func<string, TimeZoneInfo> netFrameworkLookup = id =>
            {
                if (id.Contains("/") && id != "UTC") throw new TimeZoneNotFoundException(id + " not found (simulated .NET Framework)");
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            };

            Assert.True(TimeZoneMap.TryResolve("America/Chicago", out var viaFallback, out var err, netFrameworkLookup), err);
            Assert.Equal("Central Standard Time", viaFallback.Id);

            Assert.True(TimeZoneMap.TryResolve("America/Chicago", out var viaIana, out _));
            Assert.Equal(viaIana.GetUtcOffset(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)),
                         viaFallback.GetUtcOffset(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(viaIana.GetUtcOffset(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)),
                         viaFallback.GetUtcOffset(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
        }

        [Fact]
        public void G12_config_rejects_a_zone_outside_the_embedded_map_and_names_what_is_supported()
        {
            Assert.False(TimeZoneMap.TryResolve("Europe/Madrid", out _, out var err));
            Assert.Contains("America/Chicago", err);
            Assert.Contains("America/New_York", err);
        }

        [Theory]
        // 17:00 CT is 22:00Z under daylight time and 23:00Z under standard time.
        // Both pinned on the development machine under .NET Framework 4.8.9300.
        [InlineData("2026-03-09", "2026-03-09T22:00:00Z")]
        [InlineData("2026-11-02", "2026-11-02T23:00:00Z")]
        public void G12_session_boundary_is_dst_aware(string localDate, string expectedUtc)
        {
            var cfg = GuardianConfig.Parse(Valid()).Config;
            Assert.True(SessionCalendar.TryCreate(cfg, out var cal, out var err), err);

            var noonLocalUtc = DateTime.Parse(localDate + "T16:00:00Z").ToUniversalTime();
            var end = cal.SessionEndUtc(noonLocalUtc);
            Assert.Equal(DateTime.Parse(expectedUtc).ToUniversalTime(), end);
        }

        [Fact]
        public void G12_day_key_is_labelled_by_the_session_end_and_rolls_at_the_reset_time()
        {
            var cfg = GuardianConfig.Parse(Valid()).Config;
            SessionCalendar.TryCreate(cfg, out var cal, out _);

            // 2026-08-19 16:59 CT = 21:59Z, still Wednesday's session
            Assert.Equal("2026-08-19", cal.DayKey(DateTime.Parse("2026-08-19T21:59:00Z").ToUniversalTime()));
            // 2026-08-19 17:00 CT = 22:00Z, the new session that ends on Thursday
            Assert.Equal("2026-08-20", cal.DayKey(DateTime.Parse("2026-08-19T22:00:00Z").ToUniversalTime()));
            // Thursday 09:30 CT is still that same session
            Assert.Equal("2026-08-20", cal.DayKey(DateTime.Parse("2026-08-20T14:30:00Z").ToUniversalTime()));
        }
    }
}
