# deadman-guardian

**A NinjaTrader 8 add-on that stops a prop-firm trader from breaking their own daily loss limit.**

Private repository. It goes public at beta, with a licence decided then.

Sibling projects: [deadman](https://github.com/Roberto9210/deadman) (the execution-safety library) and
[honest-strategy-search](https://github.com/Roberto9210/honest-strategy-search) (the research method).
Same discipline in all three: the specification is written first, and the unknown is a state, not a guess.

---

## Where this is

| Step | State |
|---|---|
| 0 — environment | done: NinjaTrader 8.1.8.2 and .NET SDK 8.0.424 present |
| 1 — [`SPEC.md`](SPEC.md) | done, v0.3, written before any C# |
| **2 — GuardianCore + tests** | **done: 130 tests, 25 of 25 named guarantees implemented** |
| 3 — NtAdapter | not started |

**Conformance statement, exact**: *25 of 25 named guarantees implemented, 130 collected test cases, all
passing, 0 skipped.* Not "it works". The guarantees are listed in [SPEC §15](SPEC.md); what each test
actually asserts is in the test file named after it.

Two of those guarantees are about what the code is *not*: G22 asserts by reflection that `GuardianCore`
references no NinjaTrader assembly and no network stack, and G21 asserts that no `double` or `float` appears
anywhere on its public surface, because money is `decimal` or it is a rounding bug waiting for a bad day.

## The two layers

```
src/GuardianCore/        pure C#, netstandard2.0, zero package dependencies, zero NinjaTrader
  Guardian.cs            the state machine, the lockout sequence, the fail-closed rules
  GuardianState.cs       the commitment seal and the persisted state
  PnlAccounting.cs       day P&L from executions, commissions included, unknowns kept as unknowns
  Ledger.cs              append-only, SHA-256 chained, Verify() returns the first broken seq
  GuardianConfig.cs      validation with no defaults: a missing field refuses to arm
  TimeZoneMap.cs         the IANA -> Windows map that makes the session boundary work inside NT8
  Json.cs, Money.cs, Hashing.cs, Ports.cs, TradingDay.cs
tests/GuardianCore.Tests/  130 tests, one file per guarantee group, fakes for all four ports
```

`NtAdapter` does not exist yet. When it does, it holds no decisions: SPEC §3.2 makes a conditional about
money or state inside the adapter a rejected change.

## What Step 2 found

Two real bugs, both caught by the tests that exist precisely to catch them, both now fixed:

1. **A clock anomaly cleared itself.** The forward-jump defence set `FAIL_CLOSED` and the same evaluation
   cleared it again, because the P&L was computable — the P&L was never what was in doubt. A clock unknown
   now needs the *next* coherent observation to clear (amendment A6).
2. **A truncated state file threw.** The JSON reader raised `IndexOutOfRangeException` on a torn file, which
   would have escaped into NT8's thread. Malformed input now always resolves to "unparseable", which is an
   unknown, which fails closed (found by G19).

Eight details the spec did not fix were decided fail-closed and written down in
[`AMENDMENTS.md`](AMENDMENTS.md) rather than left to be discovered in the code later.

## Running it

```bash
dotnet test
```

No NinjaTrader needed, no network, no disk unless a test asks for it: Core is a pure function of
configuration, persisted state, the event stream and the clock, and every test drives it through the four
ports of SPEC §14 with fakes.

## What it does not protect against

[SPEC §17](SPEC.md), in full and up front: deleting the add-on with NT8 closed (premeditation wins — the
ledger shows the hole), manipulating the system clock across a restart (defended in session, not across
one), and slippage or gaps between the breach and the fill (§2: this bounds exposure and removes
discretion, it does not bound the loss).

A guarantee is worth what its stated exceptions are worth.
