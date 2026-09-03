using System;
using System.Collections.Generic;
using System.Linq;
using GuardianCore;
using Xunit;

namespace GuardianCore.Tests
{
    /// <summary>Shared scaffolding: one guarded account, MES, a $600 personal limit under a $1000 firm
    /// limit, and a session that resets at 17:00 America/Chicago.</summary>
    public class Harness
    {
        public const string StatePath = "guardian/state.json";
        public const string LedgerPath = "guardian/ledger.jsonl";
        public const string Account = "Sim101";
        public const string Instrument = "MES 09-26";
        public const decimal PointValue = 5.00m;

        // 2026-08-19 15:00 America/Chicago = 20:00Z. The session ends at 17:00 CT = 22:00Z, two hours later.
        public static readonly DateTime Start = new DateTime(2026, 8, 19, 20, 0, 0, DateTimeKind.Utc);
        public static readonly DateTime SessionEnd = new DateTime(2026, 8, 19, 22, 0, 0, DateTimeKind.Utc);

        public FakeClock Clock = new FakeClock(Start);
        public FakeFileStore Store = new FakeFileStore();
        public FakeBroker Broker = new FakeBroker();
        public FakeAccountFeed Feed = new FakeAccountFeed(Account);
        public Guardian Guardian;
        public string RunId = "run-1";
        /// <summary>Identity of the GuardianCore build ON DISK, as a host would supply it. Null by
        /// default so every test that does not care exercises the absent-key path.</summary>
        public string BuildHash;

        public static string Config(string personalLimit = "600.00", string firmLimit = "1000.00",
                                   string accounts = "[\"Sim101\"]")
        {
            return "{" +
                   "\"schemaVersion\":1," +
                   "\"accounts\":" + accounts + "," +
                   "\"currency\":\"UsDollar\"," +
                   "\"firmDailyLossLimit\":\"" + firmLimit + "\"," +
                   "\"personalDailyLossLimit\":\"" + personalLimit + "\"," +
                   "\"sessionResetTimeZone\":\"America/Chicago\"," +
                   "\"sessionResetLocalTime\":\"17:00\"," +
                   "\"ledgerPath\":\"" + LedgerPath + "\"," +
                   "\"statePath\":\"" + StatePath + "\"," +
                   "\"pnlToleranceUsd\":\"5.00\"}";
        }

        /// <summary>Settable at any point in a test, so a test can make the observer start or stop
        /// throwing mid-run - which is the only way to check that a failure is counted and then
        /// published by a LATER append that must itself succeed.</summary>
        public Action<LedgerEntry> Observer;

        /// <summary>Set to run against a broker double that models the ORDER LIFECYCLE instead of the
        /// atomic FakeBroker. Only the LT-1 tests need it; everything else is unaffected, which is why
        /// this is an override rather than a change of type.</summary>
        public IBrokerActions BrokerOverride;

        public Guardian NewGuardian(string runId = null)
        {
            RunId = runId ?? RunId;
            Guardian = new Guardian(new GuardianOptions
            {
                Clock = Clock,
                Store = Store,
                Broker = BrokerOverride ?? (IBrokerActions)Broker,
                Feed = Feed,
                StatePath = StatePath,
                LedgerPath = LedgerPath,
                RunId = RunId,
                BuildHash = BuildHash,
                LedgerObserver = e => { var o = Observer; if (o != null) o(e); }
            });
            Guardian.Start();
            return Guardian;
        }

        public Guardian Armed(string personalLimit = "600.00")
        {
            NewGuardian();
            var result = Guardian.Arm(Config(personalLimit));
            Assert.True(result.Ok, result.ToString());
            Assert.Equal(StateKind.Armed, Guardian.Status.Kind);
            return Guardian;
        }

        /// <summary>A round trip that loses exactly <paramref name="dollars"/>, with the platform feed
        /// agreeing so that the cross-check of SPEC 5.4 passes and the breach rule is what is tested.</summary>
        public void LoseExactly(decimal dollars, string account = Account, int sequence = 1)
        {
            var points = dollars / PointValue;
            var entry = 5000.00m;
            var exit = entry - points;

            Feed.SetPnl(account, 0m, 0m);
            Guardian.OnExecution(new ExecutionRecord(account, Instrument, Clock.UtcNow, entry, 1, Side.Long, 0m, PointValue, "in" + sequence));
            Feed.SetPnl(account, -dollars, 0m);
            Guardian.OnExecution(new ExecutionRecord(account, Instrument, Clock.UtcNow, exit, 1, Side.Short, 0m, PointValue, "out" + sequence));
        }

        public List<JsonObject> LedgerEntries()
        {
            var ledger = new Ledger(Store, LedgerPath);
            return ledger.ReadAll().ToList();
        }

        public List<string> Events() =>
            LedgerEntries().Select(e => e.GetString("event")).ToList();

        public JsonObject LastEvent(string name) =>
            LedgerEntries().LastOrDefault(e => e.GetString("event") == name);

        public bool HasEvent(string name) => Events().Contains(name);

        public string StateOnDisk() => Store.GetRaw(StatePath);
    }
}
