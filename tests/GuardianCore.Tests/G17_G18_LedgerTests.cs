using System;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    /// <summary>G17: the chain verifies, and any edited line is found by seq.
    /// G18: an unwritable ledger is an unknown (the throw the caller turns into FAIL_CLOSED).</summary>
    public class G17_G18_LedgerTests
    {
        private const string Path = "ledger.jsonl";

        private static (FakeFileStore store, Ledger ledger) NewLedger()
        {
            var store = new FakeFileStore();
            return (store, new Ledger(store, Path));
        }

        private static Ledger WithThreeEntries(FakeFileStore store)
        {
            var l = new Ledger(store, Path);
            var t = new DateTime(2026, 8, 19, 18, 0, 0, DateTimeKind.Utc);
            l.Append(Ev.GuardianStarted, t, JsonValue.Obj().Set("version", "0.1.0"));
            l.Append(Ev.Armed, t.AddSeconds(1), JsonValue.Obj().Set("dayKey", "2026-08-19").SetMoney("personalLimit", 600m));
            l.Append(Ev.PnlCheckpoint, t.AddSeconds(2), JsonValue.Obj().SetMoney("dayLoss", 120.50m));
            return l;
        }

        [Fact]
        public void G17_empty_ledger_verifies()
        {
            var (_, ledger) = NewLedger();
            Assert.True(ledger.Verify().Ok);
        }

        [Fact]
        public void G17_chain_verifies_and_links_to_genesis()
        {
            var store = new FakeFileStore();
            var ledger = WithThreeEntries(store);

            Assert.True(ledger.Verify().Ok);
            var entries = ledger.ReadAll().ToList();
            Assert.Equal(3, entries.Count);
            Assert.Equal(Hashing.Genesis, entries[0].GetString("prev"));
            Assert.Equal(entries[0].GetString("hash"), entries[1].GetString("prev"));
            Assert.Equal(entries[1].GetString("hash"), entries[2].GetString("prev"));
            Assert.All(entries, e => Assert.Equal(64, e.GetString("hash").Length));
        }

        [Fact]
        public void G17_money_is_a_string_with_two_decimals_never_a_number()
        {
            var store = new FakeFileStore();
            WithThreeEntries(store);
            var raw = store.GetRaw(Path);
            Assert.Contains("\"personalLimit\":\"600.00\"", raw);
            Assert.Contains("\"dayLoss\":\"120.50\"", raw);
        }

        [Fact]
        public void G17_keys_are_canonically_ordered()
        {
            var store = new FakeFileStore();
            WithThreeEntries(store);
            var first = store.GetRaw(Path).Split('\n')[0];
            // ordinal sort: event, hash, payload, prev, schemaVersion, seq, tsUtc
            var order = new[] { "\"event\"", "\"hash\"", "\"payload\"", "\"prev\"", "\"schemaVersion\"", "\"seq\"", "\"tsUtc\"" };
            var positions = order.Select(k => first.IndexOf(k, StringComparison.Ordinal)).ToList();
            Assert.All(positions, p => Assert.True(p >= 0));
            Assert.Equal(positions.OrderBy(p => p).ToList(), positions);
        }

        [Theory]
        [InlineData("\"event\":\"ARMED\"", "\"event\":\"DISARMED\"")]      // event renamed
        [InlineData("\"600.00\"", "\"6000.00\"")]                            // the limit loosened after the fact
        [InlineData("\"dayKey\":\"2026-08-19\"", "\"dayKey\":\"2026-08-20\"")] // day moved
        public void G17_editing_any_field_of_an_entry_is_caught_at_that_seq(string from, string to)
        {
            var store = new FakeFileStore();
            var ledger = WithThreeEntries(store);
            Assert.True(ledger.Verify().Ok);

            var raw = store.GetRaw(Path);
            Assert.Contains(from, raw);
            store.PutRaw(Path, raw.Replace(from, to));

            var result = new Ledger(store, Path).Verify();
            Assert.False(result.Ok);
            Assert.Equal(2, result.BrokenSeq);   // the tampered entry is the second one
        }

        [Fact]
        public void G17_recomputing_the_hash_after_an_edit_still_breaks_the_next_link()
        {
            // The sophisticated tamper: edit the entry AND fix its own hash. The chain still breaks,
            // because entry 3 carries the old hash of entry 2 as its prev.
            var store = new FakeFileStore();
            WithThreeEntries(store);
            var lines = store.GetRaw(Path).Split('\n').Where(l => l.Length > 0).ToArray();

            JsonParser.TryParse(lines[1], out var v, out _);
            var o = (JsonObject)v;
            var payload = (JsonObject)o["payload"];
            payload.SetMoney("personalLimit", 6000m);
            var copy = JsonValue.Obj();
            foreach (var k in o.Keys) if (k != "hash") copy.Set(k, o[k]);
            var newHash = Hashing.Sha256Hex(copy.ToCanonical());
            lines[1] = copy.Set("hash", newHash).ToCanonical();
            store.PutRaw(Path, string.Join("\n", lines) + "\n");

            var result = new Ledger(store, Path).Verify();
            Assert.False(result.Ok);
            Assert.Equal(3, result.BrokenSeq);
            Assert.Contains("prev", result.Reason);
        }

        [Fact]
        public void G17_deleting_an_entry_is_caught()
        {
            var store = new FakeFileStore();
            WithThreeEntries(store);
            var lines = store.GetRaw(Path).Split('\n').Where(l => l.Length > 0).ToList();
            lines.RemoveAt(1);
            store.PutRaw(Path, string.Join("\n", lines) + "\n");

            var result = new Ledger(store, Path).Verify();
            Assert.False(result.Ok);
            Assert.Equal(2, result.BrokenSeq);
        }

        [Fact]
        public void G17_truncated_line_is_caught()
        {
            var store = new FakeFileStore();
            WithThreeEntries(store);
            var raw = store.GetRaw(Path);
            store.PutRaw(Path, raw.Substring(0, raw.Length - 25));

            var result = new Ledger(store, Path).Verify();
            Assert.False(result.Ok);
            Assert.Equal(3, result.BrokenSeq);
        }

        [Fact]
        public void G17_appending_after_a_reload_continues_the_same_chain()
        {
            var store = new FakeFileStore();
            var first = WithThreeEntries(store);
            var head = first.Head;

            var reopened = new Ledger(store, Path);
            Assert.Equal(head, reopened.Head);
            Assert.Equal(3, reopened.LastSeq);

            reopened.Append(Ev.Disarmed, new DateTime(2026, 8, 19, 22, 0, 0, DateTimeKind.Utc), JsonValue.Obj());
            Assert.True(reopened.Verify().Ok);
            Assert.Equal(4, reopened.LastSeq);
        }

        [Fact]
        public void G18_unwritable_ledger_throws_so_the_caller_can_fail_closed()
        {
            var store = new FakeFileStore { FailOnAppend = true };
            var ledger = new Ledger(store, Path);
            Assert.Throws<InvalidOperationException>(() =>
                ledger.Append(Ev.Armed, DateTime.UtcNow, JsonValue.Obj()));
        }
    }
}
