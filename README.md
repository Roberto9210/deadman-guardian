# deadman-guardian

**A NinjaTrader 8 add-on that stops a prop-firm trader from breaking their own daily loss limit.**

## Status, before anything else

> **In testing. No release, no version tag, no users, no installer you should trust yet.**
> It has run inside real NinjaTrader on the `Sim101` simulator only. It has never been armed on a funded
> account. The soak is ongoing, and the numbers below are from the last run, not from a finished campaign.

| | |
|---|---|
| **Conformance statement, exact** | **25 of 26 named guarantees implemented**, 0 failing, 0 skipped. **The one that is not is `G8`** — *"Orders after lockout are cancelled and logged"* — unimplemented since **2026-08-27**, when `a916bba` landed the decision [A11](AMENDMENTS.md) had taken the day before, removing cancel-on-observation after it cancelled the guardian's own flatten and **four orders of the trader's** — rejected twelve times between them, and the event carries no quantity, so whether any of them was reducing the position is not in the record. `G8`'s text is deliberately **not** rewritten to match the code, because the gap is real and rewriting it deletes the gap from view — [A12](AMENDMENTS.md). Not "it works", not "all tests green". *(A test COUNT used to stand here and was removed on 2026-09-01 rather than updated: the number climbs on its own, says nothing about coverage, and is wrong again the following week. A claim that expires by itself is a trap for whoever reads it next. The guarantee count is stable and checkable, so it is the one that stays — and since **2026-09-02 it is COMPUTED from the SPEC §15 table** by `C_ConformanceCountTests`, not typed. It read 26 of 26 for six days after `G8` stopped being implemented, because a hand-written number can only ever go up.)* |
| **Session certificate** | **18 of 18 in scope; 19 defined; 1 excluded and named.** C15b (verification against a key we publish) is declared out of scope in v1 — [`CERT_CONFORMANCE.md`](CERT_CONFORMANCE.md) argues the exclusion instead of dropping it from the denominator |
| **Soak** | **6 of 6 scenarios passed, twice, on 2026-08-21** (runs 12:26:47Z and 12:28:13Z) against real NinjaTrader on `Sim101` — [nt/soak/REMOJO_REPORT.md](nt/soak/REMOJO_REPORT.md). **READ THE DATE: that evidence is from 2026-08-21 and it PREDATES the three behaviour findings that followed — LT-1 (26-ago), LT-2 (27-ago) and LT-4 (31-ago), the last of which proved the guardian stopped enforcing after its first verified flatten. It is kept rather than deleted because it happened, and it is not refreshed because IT CANNOT BE RE-RUN TODAY: one of its assertions describes behaviour LT-1 removed, so the suite is deliberately parked until that assertion is re-aimed and a human decides to restore its gate.** |
| **Earlier soak runs** | 5 of 6, twice, the same morning — published above the passing ones, not replaced by them. The failing scenario and its fix are in the report |
| **Release** | none. No tag, no binary, no package |
| **Users** | none |

The guarantees are named `G1`–`G23` (with `G13` split into `G13a`–`G13d`) in [**SPEC §15**](SPEC.md), and what
each one actually asserts is in the test file named after it. The eight details the spec did not settle were
decided fail-closed and written down in [`AMENDMENTS.md`](AMENDMENTS.md) rather than left to be found in the
code later.

**Read what it does not protect against before you read what it does:**
[**SPEC §17 — "What this does not protect against"**](SPEC.md), in full: deleting the add-on with NT8 closed,
manipulating the system clock across a restart, slippage and gaps between the breach and the fill, everything
outside its sight, and firm minimum-hold rules. A guarantee is worth what its stated exceptions are worth.

**A risk team can read all of it without installing anything.** The whole decision layer is
[`src/GuardianCore/`](src/GuardianCore/) — pure C#, no NinjaTrader, no network, no I/O except through four
injected ports — and the specification it was written against is in this repository, dated before the code.
`dotnet test` runs the whole suite with no platform, no account and no connection. Nothing here needs to touch
a broker to be audited.

---

Sibling projects: [deadman](https://github.com/Roberto9210/deadman) (the execution-safety library) and
[honest-strategy-search](https://github.com/Roberto9210/honest-strategy-search) (the research method).
Same discipline in all three: the specification is written first, and the unknown is a state, not a guess.

## Where this is

| Step | State |
|---|---|
| 0 — environment | done: NinjaTrader 8.1.8.2 and .NET SDK 8.0.424 present |
| 1 — [`SPEC.md`](SPEC.md) | done, v0.4 (v0.1 written before any C#; amendments absorbed after Step 2) |
| 2 — GuardianCore + tests | done: 25 of 26 named guarantees implemented (`G8` open — A12) |
| 3 — NtAdapter + platform verification | done: installed in real NT8, armed on `Sim101`, enforcement proven end-to-end against a real order — [nt/STEP3_FINDINGS.md](nt/STEP3_FINDINGS.md) |
| 4 — beta | not started |

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
  Certificate.cs         the session certificate emitter: it counts, it never invents, it cannot send
tests/GuardianCore.Tests/  one file per guarantee group, fakes for all four ports

nt/addon/                the NinjaTrader adapter: subscribes, reports, cancels. It holds no decisions —
                         SPEC §3.2 makes a conditional about money or state inside the adapter a rejected change
nt/soak/                 the soak: an automated attacker, not a demo
nt/probe/evidence/       what the platform actually did, logged at the time
```

## What Step 2 found

Two real bugs, both caught by the tests that exist precisely to catch them, both now fixed:

1. **A clock anomaly cleared itself.** The forward-jump defence set `FAIL_CLOSED` and the same evaluation
   cleared it again, because the P&L was computable — the P&L was never what was in doubt. A clock unknown
   now needs the *next* coherent observation to clear (amendment A6).
2. **A truncated state file threw.** The JSON reader raised `IndexOutOfRangeException` on a torn file, which
   would have escaped into NT8's thread. Malformed input now always resolves to "unparseable", which is an
   unknown, which fails closed (found by G19).

## What the soak found

The soak is an attacker: it breaches the limit, edits the sealed config, hand-edits the state, kills the
process mid-lockout, submits orders while locked, and pushes the clock past expiry. Its first two runs came
back **5 of 6** — and the failing scenario was the one that matters most, an order surviving while `LOCKED`.
The cause was a defect in the soak's own cancel path, not in the guardian, and the fix is in the report along
with the runs that failed. They are published above the passing runs, in order, not replaced by them.

**Read that paragraph with its date on: it is from 2026-08-21.** "An order surviving while `LOCKED`" was a
failure then, because the guardian cancelled on sight then. **Since 2026-08-26 it is the expected behaviour**
— see the enforcement bullets above — so that scenario has been re-aimed to assert what the product now does,
and the soak's gate stays parked until a human decides to restore it.

## Documentation

| | |
|---|---|
| [SPEC.md](SPEC.md) | the specification, v0.4 — written before the code |
| [AMENDMENTS.md](AMENDMENTS.md) | every detail the spec did not settle, and how it was decided |
| [CERT_CONFORMANCE.md](CERT_CONFORMANCE.md) | the session certificate: 18 of 18 in scope, 19 defined, the excluded one named and argued |
| [nt/STEP3_FINDINGS.md](nt/STEP3_FINDINGS.md) | what NinjaTrader actually does, measured inside the process |
| [nt/soak/REMOJO_REPORT.md](nt/soak/REMOJO_REPORT.md) | every soak run, failures first |
| [docs/install.md](docs/install.md) | the real procedure, including the two ways it failed first |
| [docs/configure.md](docs/configure.md) | the two numbers and the one decision, in trader language |
| [docs/troubleshooting.md](docs/troubleshooting.md) | every failure we actually hit, with its symptom |
| [docs/uninstall.md](docs/uninstall.md) | putting the platform back |

## Running the tests

```bash
dotnet test
```

No NinjaTrader needed, no network, no disk unless a test asks for it: Core is a pure function of
configuration, persisted state, the event stream and the clock, and every test drives it through the four
ports of SPEC §14 with fakes.

## What the platform actually does, measured

Every claim in [SPEC](SPEC.md) that depended on NinjaTrader behaving a certain way was checked inside the
running process, not on a bench. The results are in [nt/STEP3_FINDINGS.md](nt/STEP3_FINDINGS.md):

- **There is no pre-submit hook.** 2,912 types scanned at runtime, zero events that could veto an order
  before submission. Nothing here can stop an order from reaching the venue, and this README will not claim otherwise.
- **NOTHING IS CANCELLED WHILE LOCKED, and this is the sentence to read twice.** Entering the lockout
  sweeps the resting orders **once** — that sweep is the only call to `CancelAllOrders` in the codebase,
  and its only caller is `EnterLockout`. After that, an order the guardian sees is **neither cancelled
  nor recorded**: the observation path returns without acting. **So an order sent after the lockout
  reaches the venue and can fill.** What the guardian bounds is how long the position that order opens
  survives — the next cycle's flatten closes it — not whether the order goes out. Any wording of ours
  that says orders are "cancelled on sight" describes a mechanism this build does not have.
- **It is weaker than it used to be, deliberately.** Until 2026-08-26 the guardian did cancel every order
  it saw while locked, and a live test showed the price: it cancelled **its own flatten orders** and the
  trader's own exits — twelve of them, inside six seconds, all `Sell` / `SellShort` / `BuyToCover`.
  Cancelling wrongly can trap someone in a sinking position, which is unbounded and caused by us; not
  cancelling costs one order's worth of exposure for one cycle. **The `14.4 ms` figure this README used to
  publish measured that removed mechanism, so it is not a claim about this build**, and
  `ordersRejectedWhileLocked` is `0` in every certificate issued since — truthfully, because nothing
  rejects anything. There has been **no `ORDER_REJECTED_LOCKED` since 2026-08-26 23:44Z**, and there
  cannot be: the event has no writer left in the source.
- **`America/Chicago` throws inside NinjaTrader.** The runtime resolves Windows time zone ids only, so the
  embedded IANA map is what makes the session boundary work at all.
- **Sleep does not stop the monotonic clock.** Two real S3 suspends moved wall-vs-monotonic by 41 and 53 ms,
  where a stopped counter would have moved 5,000 — so a suspend raises no false alarm.
- **The seal's expiry falls back to the wall clock across a restart.** Inside one process the monotonic
  counter decides, and the clock-forward scenario passes for that reason. After a restart there is no
  monotonic evidence left to appeal to. "Moving the clock does not help" is true *without an intervening
  restart*, and any copy that omits that qualifier is overclaiming — SPEC §17.2.

## Honest comparison with what NinjaTrader already has

NT8 carries risk plumbing, and none of it is a trader-set daily loss lockout. `Cbi.Risk` /
`InstrumentRisk` is a named template of per-instrument **margins and size caps** — no daily-loss concept
in it at all. The daily-loss-shaped fields (`AccountItem.DailyLossLimit`, `WeeklyLossLimit`,
`DailyProfitTrigger`, `TrailingMaxDrawdown`) are values **the venue reports** and NT8 mirrors, and the
matching `Account` members — along with `IsAutoLiquidationEnabled` and `LiquidationState` — do not appear
anywhere in the published [Account class reference](https://developer.ninjatrader.com/docs/desktop/account_class).
They are public in IL and undocumented in the API.

This add-on **ignores all of them**: a public setter is not an enforcement contract, and a guardian built
on an undocumented member stops protecting silently when the member changes. See [SPEC §3.4](SPEC.md).

And the part that costs a sale: **where a venue-side self-set daily loss limit exists, it is stronger than
this add-on.** Tradovate exposes one, and firms that use it document that once set it cannot be overridden
even by their own staff. A venue-side limit does not depend on your machine being on, on NT8 running, or on
this code being installed. Use it if you have it. This add-on is for when you do not — or when you want a
stricter personal limit on top of it, with a local auditable record of every attempt to loosen it.

## Licence

[MIT](LICENSE).
