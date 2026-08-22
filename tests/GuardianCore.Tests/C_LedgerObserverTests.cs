// The ledger observer: best-effort, never load-bearing, and never invisible.
//
// It exists so the adapter can speak at LIMIT_BREACHED and at FLATTEN_VERIFIED. That makes "the
// guardian explains what happened" a product claim resting on a callback, and a callback that fails
// silently is the defect this repository keeps catching. So the failures are counted, published on
// the next tick, and dumped again into GUARDIAN_STOPPED - because the window a tick can miss is
// exactly the one that matters: a trader closing the platform seconds after a lockout.

using System;
using System.Collections.Generic;
using System.Linq;
using GuardianCore;

namespace GuardianCore.Tests
{
    public class C_LedgerObserverTests
    {
        private static Ledger NewLedger(out FakeFileStore store)
        {
            store = new FakeFileStore();
            return new Ledger(store, "ledger.jsonl");
        }

        private static List<string> Events(FakeFileStore store, string path = "ledger.jsonl")
        {
            return new Ledger(store, path).ReadAll()
                .Select(o => o.GetString("event"))
                .Where(e => e != null).ToList();
        }

        [Fact]
        public void The_observer_sees_every_appended_entry()
        {
            FakeFileStore store;
            var seen = new List<string>();
            var ledger = NewLedger(out store);
            ledger.Observer = e => seen.Add(e.Event);

            ledger.Append("ONE", DateTime.UtcNow, JsonValue.Obj());
            ledger.Append("TWO", DateTime.UtcNow, JsonValue.Obj());

            Assert.Equal(new[] { "ONE", "TWO" }, seen);
        }

        /// <summary>The append has already happened when the observer runs. Its exception cannot undo
        /// it, cannot escape, and above all cannot stop a lockout.</summary>
        [Fact]
        public void An_observer_that_throws_cannot_break_the_append_or_the_chain()
        {
            FakeFileStore store;
            var ledger = NewLedger(out store);
            ledger.Observer = e => throw new InvalidOperationException("observer is broken");

            ledger.Append("ONE", DateTime.UtcNow, JsonValue.Obj());
            ledger.Append("TWO", DateTime.UtcNow, JsonValue.Obj());

            Assert.Equal(new[] { "ONE", "TWO" }, Events(store));
            Assert.True(new Ledger(store, "ledger.jsonl").Verify().Ok);
        }

        /// <summary>Swallowing it is what would make this the same animal again. It is counted.</summary>
        [Fact]
        public void Failures_are_counted_not_swallowed()
        {
            FakeFileStore store;
            var ledger = NewLedger(out store);
            ledger.Observer = e => throw new InvalidOperationException("boom");

            ledger.Append("ONE", DateTime.UtcNow, JsonValue.Obj());
            ledger.Append("TWO", DateTime.UtcNow, JsonValue.Obj());

            Assert.Equal(2, ledger.ObserverFailures);
            Assert.Equal(2, ledger.TakeObserverFailures());
            Assert.Equal(0, ledger.ObserverFailures);      // read once, then reset
        }

        /// <summary>An observer that appends is a bug in the observer. The ledger must not recurse
        /// because of it, and must not corrupt its own chain either.</summary>
        [Fact]
        public void An_observer_that_appends_cannot_recurse()
        {
            FakeFileStore store;
            var ledger = NewLedger(out store);
            var calls = 0;
            ledger.Observer = e =>
            {
                calls++;
                if (calls < 5) ledger.Append("FROM_OBSERVER", DateTime.UtcNow, JsonValue.Obj());
            };

            ledger.Append("ONE", DateTime.UtcNow, JsonValue.Obj());

            Assert.Equal(1, calls);                        // the nested append notified nobody
            Assert.Equal(new[] { "ONE", "FROM_OBSERVER" }, Events(store));
            Assert.True(new Ledger(store, "ledger.jsonl").Verify().Ok);
        }

        [Fact]
        public void No_observer_at_all_is_the_normal_case_and_costs_nothing()
        {
            FakeFileStore store;
            var ledger = NewLedger(out store);

            ledger.Append("ONE", DateTime.UtcNow, JsonValue.Obj());

            Assert.Equal(0, ledger.ObserverFailures);
            Assert.Equal(new[] { "ONE" }, Events(store));
        }

        // ---------------------------------------------------------------- publication

        /// <summary>Published on the NEXT tick, never from inside the notification - recording a
        /// failure by appending would put recursion in the lockout's critical path.</summary>
        [Fact]
        public void The_guardian_publishes_the_count_on_its_next_tick()
        {
            var h = new Harness();
            h.Observer = e => throw new InvalidOperationException("boom");

            h.Armed("600.00");                              // several appends, all of them failing
            h.Observer = null;                              // stop failing so the report itself lands
            h.Guardian.Tick();

            var notify = h.Events().Count(e => e == Ev.NotifyFailed);
            Assert.Equal(1, notify);
        }

        /// <summary>And again on Stop(), because the window a tick can miss is the one that matters:
        /// a trader closing the platform seconds after a lockout.</summary>
        [Fact]
        public void Stop_dumps_whatever_the_last_tick_did_not_carry()
        {
            // No tick between the failures and the close - which is the whole point. An armed
            // guardian with nothing to report appends nothing on a tick, so a trader who shuts the
            // platform right after a lockout can leave failures that no tick will ever carry.
            var h = new Harness();
            h.Observer = e => throw new InvalidOperationException("boom");
            h.Armed("600.00");                              // CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED
            h.Observer = null;                              // so GUARDIAN_STOPPED itself lands
            h.Guardian.Stop();

            var stopped = new Ledger(h.Store, Harness.LedgerPath).ReadAll()
                .Last(o => o.GetString("event") == Ev.GuardianStopped);
            var payload = (JsonObject)stopped["payload"];

            Assert.NotNull(payload.GetInt("notifyFailures"));
            Assert.True(payload.GetInt("notifyFailures") > 0);
        }

        [Fact]
        public void A_clean_run_reports_zero_rather_than_omitting_the_field()
        {
            var h = new Harness();
            h.Armed("600.00");
            h.Guardian.Stop();

            var stopped = new Ledger(h.Store, Harness.LedgerPath).ReadAll()
                .Last(o => o.GetString("event") == Ev.GuardianStopped);
            var payload = (JsonObject)stopped["payload"];

            Assert.Equal(0, payload.GetInt("notifyFailures"));
        }
    }
}
