// C1, C6-C11, C14, C16 on the EMITTER side (CERT_SPEC v0.2.1 A.5).
//
// The verifier-side guarantees are proved in deadman-kit's tests/test_c_certificate.py,
// which was written first and which this emitter has to satisfy. What is proved here is
// what only the emitter can be asked: that it counts the same way the judge does, that it
// omits rather than invents, that the HTML adds nothing, and that no route exists to send
// a certificate anywhere.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    public class C_CertificateEmitterTests : Harness
    {
        /// <summary>A fixed salt so tests are deterministic. Real installations use 32 random
        /// bytes from a CSPRNG; what matters here is that it never reaches the document.</summary>
        private const string TestSalt = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";

        private static CertificateRequest Req(string alias = "roberto", string day = "2026-08-19",
                                              string prev = null, IReadOnlyList<AnchorRecord> anchors = null)
        {
            return new CertificateRequest
            {
                Alias = alias, DayKey = day, PreviousCertHash = prev,
                Anchors = anchors, IssuerVersion = "0.1.0", IssuerBuildHash = "test",
                AccountSalt = TestSalt,
            };
        }

        /// <summary>Reads the state the guardian actually persisted, the same way anything
        /// outside the process would have to. No test-only back door into the Guardian.</summary>
        private PersistedState StateFromDisk()
        {
            PersistedState state; string error;
            var raw = StateOnDisk();
            if (raw == null) return null;
            return PersistedState.TryParse(raw, out state, out error) ? state : null;
        }

        private CertificateResult Issue(CertificateRequest req = null, bool chainVerified = true)
        {
            return Certificate.Issue(LedgerEntries(), StateFromDisk(), req ?? Req(), chainVerified);
        }

        private void AccountDisappears() => Feed.Remove(Harness.Account);
        private void AccountReturns() =>
            Feed.SetState(Harness.Account, new AccountState(true, ConnectionState.Connected, "UsDollar"));

        private static JsonObject Parse(string json)
        {
            JsonValue v; string err;
            Assert.True(JsonParser.TryParse(json, out v, out err), err);
            return (JsonObject)v;
        }

        // ---------------------------------------------------------------- C1

        [Fact]
        public void C1_a_quiet_armed_day_produces_a_complete_certificate()
        {
            Armed("600.00");
            Guardian.Tick();

            var result = Issue();
            Assert.True(result.Ok, result.Reason);

            var doc = Parse(result.Json);
            Assert.Equal("guardian-core-v1", doc.GetString("ledgerDialect"));
            Assert.Equal(64, result.CertHash.Length);

            var claims = (JsonObject)doc["claims"];
            Assert.Equal(0L, claims.GetInt("lockoutsTriggered"));
            Assert.Equal("true", claims["limitRespected"].ToCanonical());
        }

        [Fact]
        public void C1_certhash_covers_the_document_without_certhash_and_signature()
        {
            // SPEC A.4.1, the rule the public verifier enforces. If this drifts, every
            // certificate this emitter produces is rejected in public - which is the point.
            Armed("600.00");
            var result = Issue();
            var doc = Parse(result.Json);

            var withoutHash = Parse(result.Json).Remove("certHash").Remove("signature");
            Assert.Equal(doc.GetString("certHash"), Hashing.Sha256Hex(withoutHash.ToCanonical()));
        }

        [Fact]
        public void C1_the_json_is_canonical_and_reparses_identically()
        {
            Armed("600.00");
            var json = Issue().Json;
            Assert.Equal(json, Parse(json).ToCanonical());
            Assert.DoesNotContain("\n", json);

            // Keys ordinal-sorted at the top level. NOT asserted by scanning for the
            // two-character sequence comma-space: the limitation strings legitimately
            // contain it inside their own text, and a naive substring check fails on
            // honest content. Canonical means no whitespace BETWEEN tokens, not inside them.
            var keys = Parse(json).Keys.ToList();
            Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal).ToList(), keys);
        }

        // ---------------------------------------------------------------- C6

        [Fact]
        public void C6_a_fail_closed_episode_includes_its_trigger()
        {
            Armed("600.00");
            AccountDisappears();                 // account disappears -> ACCOUNT_UNKNOWN, then fail-closed
            Guardian.Tick();
            Guardian.Tick();
            AccountReturns();
            Guardian.Tick();

            var claims = Certificate.Recompute(LedgerEntries(), 1, LedgerEntries().Count, true);
            Assert.Single(claims.FailClosedEpisodes);
            var ep = claims.FailClosedEpisodes[0];
            Assert.Equal(Ev.AccountUnknown, ep.TriggerEvent);
            Assert.True(ep.TriggerSeq < ep.FromSeq, "the trigger precedes the entry it caused");
            Assert.True(ep.Reasons[Ev.AccountUnknown] >= 1);
            Assert.False(ep.Open);
        }

        [Fact]
        public void C6_an_open_episode_falsifies_limit_respected_and_writes_no_closing_time()
        {
            Armed("600.00");
            AccountDisappears();
            Guardian.Tick();

            var entries = LedgerEntries();
            var claims = Certificate.Recompute(entries, 1, entries.Count, true);
            Assert.Single(claims.FailClosedEpisodes);
            Assert.True(claims.FailClosedEpisodes[0].Open);
            Assert.Null(claims.FailClosedEpisodes[0].ToUtc);
            Assert.False(claims.LimitRespected);

            var doc = Parse(Issue().Json);
            var ep = (JsonObject)((JsonArray)((JsonObject)doc["claims"])["failClosedEpisodes"]).Items[0];
            Assert.False(ep.Has("toUtc"));      // absent, not a plausible default
            Assert.Equal("true", ep["open"].ToCanonical());
        }

        // ---------------------------------------------------------------- C7

        [Fact]
        public void C7_no_alias_is_refused_rather_than_invented()
        {
            Armed("600.00");
            var result = Issue(Req(alias: null));
            Assert.False(result.Ok);
            Assert.Contains("CERT_ALIAS_MISSING", result.Reason);
        }

        [Fact]
        public void C7_without_a_seal_there_is_nothing_to_certify()
        {
            NewGuardian();                       // built, never armed: there is no seal
            Guardian.Tick();
            var result = Issue();
            Assert.False(result.Ok);
            Assert.Contains("CERT_NO_SEAL", result.Reason);
        }

        [Fact]
        public void C7_no_daykey_is_refused_rather_than_read_off_the_clock()
        {
            Armed("600.00");
            var result = Issue(Req(day: null));
            Assert.False(result.Ok);
            Assert.Contains("CERT_DAYKEY_MISSING", result.Reason);
        }

        [Fact]
        public void C7_previous_cert_hash_is_null_on_the_first_day_never_fabricated()
        {
            Armed("600.00");
            var doc = Parse(Issue().Json);
            Assert.Equal("null", doc["previousCertHash"].ToCanonical());
        }

        // ---------------------------------------------------------------- C9

        [Fact]
        public void C9_account_names_are_hashed_and_no_individual_trade_appears()
        {
            Armed("600.00");
            LoseExactly(600.00m);                // real fills, so there IS trade data to leak
            Guardian.Tick();

            var json = Issue().Json;
            Assert.DoesNotContain(Harness.Account, json);
            Assert.DoesNotContain("fillPrice", json);
            Assert.DoesNotContain("executionId", json);
            Assert.DoesNotContain("averageFillPrice", json);

            var accounts = (JsonArray)((JsonObject)Parse(json)["subject"])["accounts"];
            Assert.Single(accounts.Items);
            Assert.Equal(16, ((JsonString)accounts.Items[0]).Value.Length);
        }

        [Fact]
        public void C9_the_salt_never_appears_in_the_certificate_in_any_form()
        {
            // SPEC A.7. The salt is what makes the account hash unguessable; leaking it into the
            // document would undo the whole point in one line.
            Armed("600.00");
            var result = Issue();

            Assert.DoesNotContain(TestSalt, result.Json);
            Assert.DoesNotContain(TestSalt, result.Html);
            Assert.DoesNotContain(TestSalt.ToUpperInvariant(), result.Json.ToUpperInvariant());
            Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(TestSalt)), result.Json);
            Assert.DoesNotContain("salt", result.Json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("salt", result.Html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void C9_an_unsalted_hash_of_a_short_account_name_is_refused()
        {
            // The reason the salt exists: sha256("Sim101") is a dictionary lookup away.
            Armed("600.00");
            var req = Req();
            req.AccountSalt = null;
            var result = Certificate.Issue(LedgerEntries(), StateFromDisk(), req, true);
            Assert.False(result.Ok);
            Assert.Contains("CERT_SALT_MISSING", result.Reason);
        }

        [Fact]
        public void C9_the_same_account_hashes_differently_under_different_salts()
        {
            // The honest consequence of A.7, asserted rather than left implicit: the hash
            // identifies an account WITHIN a series, not ACROSS installations. That is the trade.
            var a = Certificate.HashAccount("salt-one", Harness.Account);
            var b = Certificate.HashAccount("salt-two", Harness.Account);
            var c = Certificate.HashAccount("salt-one", Harness.Account);

            Assert.NotEqual(a, b);
            Assert.Equal(a, c);                       // stable within one installation
            Assert.Equal(16, a.Length);
            Assert.NotEqual(Hashing.Sha256Hex(Harness.Account).Substring(0, 16), a);
        }

        [Fact]
        public void C9_the_salt_actually_changes_the_published_hash()
        {
            Armed("600.00");
            var one = Req(); one.AccountSalt = "aaaa" + TestSalt;
            var two = Req(); two.AccountSalt = "bbbb" + TestSalt;

            var h1 = ((JsonArray)((JsonObject)Parse(Certificate.Issue(LedgerEntries(), StateFromDisk(), one, true).Json)["subject"])["accounts"]).Items[0];
            var h2 = ((JsonArray)((JsonObject)Parse(Certificate.Issue(LedgerEntries(), StateFromDisk(), two, true).Json)["subject"])["accounts"]).Items[0];
            Assert.NotEqual(((JsonString)h1).Value, ((JsonString)h2).Value);
        }

        // ---------------------------------------------------------------- C10

        [Fact]
        public void C10_the_limitations_are_carried_verbatim()
        {
            Armed("600.00");
            var doc = Parse(Issue().Json);
            var lims = ((JsonArray)doc["limitations"]).Items.Cast<JsonString>().Select(s => s.Value).ToList();
            Assert.Equal(Certificate.Limitations.Length, lims.Count);
            foreach (var required in Certificate.Limitations) Assert.Contains(required, lims);
        }

        [Fact]
        public void C10_every_limitation_names_something_the_certificate_does_not_prove()
        {
            // Cheap guard against a future edit that turns these into marketing.
            foreach (var l in Certificate.Limitations)
                Assert.True(l.Contains("does not") || l.Contains("not an audit"),
                            "a limitation that does not limit anything: " + l);
        }

        // ---------------------------------------------------------------- C8

        [Fact]
        public void C8_the_html_adds_no_value_that_is_not_in_the_json()
        {
            Armed("600.00");
            AccountDisappears();
            Guardian.Tick();
            AccountReturns();
            Guardian.Tick();

            var result = Issue();
            var doc = Parse(result.Json);
            var html = result.Html;

            // Every value the page shows is a value the document holds.
            var claims = (JsonObject)doc["claims"];
            Assert.Contains(claims["lockoutsTriggered"].ToCanonical(), html);
            Assert.Contains(doc.GetString("certHash"), html);
            Assert.Contains(doc.GetString("trustLevel"), html);

            // And it carries the limitations and the way to contradict it.
            foreach (var l in Certificate.Limitations)
                Assert.Contains(l.Replace("'", "&#39;").Replace("\"", "&quot;").Substring(0, 30), html);
            Assert.Contains("python -m deadman.verify_certificate", html);
        }

        [Fact]
        public void C8_the_html_contains_no_verdict_words_of_its_own()
        {
            Armed("600.00");
            LoseExactly(600.00m);               // a day that DID breach
            Guardian.Tick();

            var html = Issue().Html.ToLowerInvariant();
            foreach (var word in new[] { "excellent", "congratul", "well done", "disciplined",
                                         "successful", "failure", "good day", "bad day", "score" })
                Assert.False(html.Contains(word), "the render editorialised: " + word);
        }

        [Fact]
        public void C8_the_render_is_a_pure_function_of_the_document()
        {
            Armed("600.00");
            var json = Issue().Json;
            var doc = Parse(json);
            Assert.Equal(Certificate.Render(doc), Certificate.Render(Parse(json)));
        }

        [Fact]
        public void C8_html_escapes_a_hostile_alias()
        {
            Armed("600.00");
            var result = Issue(Req(alias: "<script>alert(1)</script>"));
            Assert.True(result.Ok, result.Reason);
            Assert.DoesNotContain("<script>", result.Html);
            Assert.Contains("&lt;script&gt;", result.Html);
        }

        [Fact]
        public void C8_episodes_are_rendered_readably_not_as_a_json_blob()
        {
            Armed("600.00");
            AccountDisappears();
            Guardian.Tick();
            AccountReturns();
            Guardian.Tick();

            var html = Issue().Html;
            Assert.Contains("<h2>failClosedEpisodes</h2>", html);
            Assert.Contains("<th>trigger</th>", html);
            Assert.Contains(Ev.AccountUnknown, html);
            // the unreadable form is gone: no escaped JSON object in a cell
            Assert.DoesNotContain("&quot;fromSeq&quot;", html);
            Assert.DoesNotContain("&quot;reasons&quot;", html);
        }

        [Fact]
        public void C8_a_day_with_no_episodes_says_none_rather_than_showing_an_empty_table()
        {
            Armed("600.00");
            Guardian.Tick();
            var html = Issue().Html;
            Assert.Contains("<h2>failClosedEpisodes</h2>", html);
            Assert.Contains("<p>none</p>", html);
        }

        // ---------------------------------------------------------------- C14

        [Fact]
        public void C14_rejected_attempts_to_loosen_the_limit_reach_the_certificate()
        {
            Armed("600.00");
            Guardian.TryChangeConfig(Config("9000.00"));
            Guardian.TryChangeConfig(Config("1200.00"));

            var doc = Parse(Issue().Json);
            var commitment = (JsonObject)doc["commitment"];
            Assert.Equal(2L, commitment.GetInt("changeAttemptsWhileSealed"));
            Assert.Equal("600.00", commitment.GetString("personalDailyLossLimit"));
        }

        // ---------------------------------------------------------------- C16

        [Fact]
        public void C16_the_emitter_has_no_route_to_send_anything()
        {
            // Reflection over the whole core: no type it exposes can reach a socket, and the
            // emitter in particular neither writes nor transmits - it returns strings and the
            // trader decides what happens next.
            var assembly = typeof(Certificate).Assembly;
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
            Assert.DoesNotContain(referenced, n => n.IndexOf("System.Net", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(referenced, n => n.IndexOf("Http", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(referenced, n => n.IndexOf("Socket", StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var m in typeof(Certificate).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var names = new[] { m.ReturnType.FullName ?? "" }
                    .Concat(m.GetParameters().Select(p => p.ParameterType.FullName ?? ""));
                foreach (var n in names)
                {
                    Assert.False(n.IndexOf("System.Net", StringComparison.OrdinalIgnoreCase) >= 0,
                                 m.Name + " touches the network");
                    Assert.False(n.IndexOf("Stream", StringComparison.OrdinalIgnoreCase) >= 0,
                                 m.Name + " takes a stream: the emitter must not write anywhere itself");
                }
            }
        }

        [Fact]
        public void C16_nothing_in_the_guardian_calls_the_emitter_on_its_own()
        {
            // Section 3c: emission is a trader action, never a consequence of trading. If the
            // engine could issue a certificate by itself, the tool becomes a leash and gets
            // uninstalled on the first bad day.
            Armed("600.00");
            LoseExactly(600.00m);               // breach, lockout, the loudest path there is
            Guardian.Tick();
            Clock.Advance(TimeSpan.FromHours(3));
            Guardian.Tick();

            Assert.DoesNotContain(Events(), e => e.IndexOf("CERT", StringComparison.Ordinal) >= 0);
            Assert.DoesNotContain(Store.Operations, p => p.IndexOf("cert", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ---------------------------------------------------------------- the emitter vs the judge

        [Fact]
        public void The_emitter_counts_the_way_the_public_verifier_counts()
        {
            // Same arithmetic, independently written, on a deliberately messy session.
            Armed("600.00");
            Guardian.TryChangeConfig(Config("9000.00"));
            AccountDisappears();
            Guardian.Tick();
            AccountReturns();
            Guardian.Tick();
            LoseExactly(600.00m);
            Guardian.Tick();

            var entries = LedgerEntries();
            var claims = Certificate.Recompute(entries, 1, entries.Count, true);

            Assert.Equal(1, claims.LockoutsTriggered);
            Assert.Equal(1, claims.ChangeAttemptsWhileSealed);
            Assert.Single(claims.FailClosedEpisodes);
            Assert.False(claims.LimitRespected);          // it breached, so it is false

            // and the document says exactly that, with no softening
            var doc = Parse(Issue().Json);
            Assert.Equal("false", ((JsonObject)doc["claims"])["limitRespected"].ToCanonical());
        }

        [Fact]
        public void An_unverified_chain_can_never_yield_limit_respected()
        {
            Armed("600.00");
            Guardian.Tick();
            var entries = LedgerEntries();
            var claims = Certificate.Recompute(entries, 1, entries.Count, chainVerified: false);
            Assert.False(claims.LimitRespected);
        }

        [Fact]
        public void Anchors_supplied_raise_the_declared_level_to_l2_and_nothing_higher()
        {
            Armed("600.00");
            Guardian.Tick();
            var entries = LedgerEntries();
            var anchor = new[] { new AnchorRecord {
                Type = "tsa", Ref = "third-party", Seq = 1,
                Hash = entries[0].GetString("hash"), TsUtc = Clock.UtcNow } };

            Assert.Equal("L1", Parse(Issue().Json).GetString("trustLevel"));
            Assert.Equal("L2", Parse(Issue(Req(anchors: anchor)).Json).GetString("trustLevel"));
        }
    }
}
