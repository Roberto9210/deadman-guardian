# SPEC amendments — A1-A8 ABSORBED into SPEC v0.4, A9 applied to §17.5

**Status: A1-A8 are in the specification proper. A9 is applied to §17.5 and awaits absorption at v0.5.**
This file stays as the record of *why* each one exists and what it replaced; the rule it enforces is below.

Details Step 2 had to decide because SPEC v0.3 did not fix them. Every one of them was resolved
**fail-closed**, is implemented that way, and is listed here so the spec can absorb it in v0.4 rather than
having the code quietly become the specification.

The rule this file exists to enforce: when the code knows something the spec does not, the spec is behind,
and the gap is written down instead of forgotten.

---

## A1 — `IFileStore` needs `ReadLines`

**Spec**: §14 lists `Exists`, `ReadAllText`, `WriteAtomic`, `AppendLine`.
**Problem**: §11.3 requires `Ledger.Verify()`, which has to read the chain back line by line. Reading a
growing ledger through `ReadAllText` would mean holding the whole file in memory to check a hash chain.
**Decision**: added `IEnumerable<string> ReadLines(string path)` to the port.
**Fail-closed**: unchanged — a store that cannot read is a store that throws, and §11.5 turns that into
`FAIL_CLOSED`.

## A2 — `PlatformPnl` is gross-realized, and says so

**Spec**: §5.3 says the cross-check compares against "the platform's realized figure". NT8 exposes both
`AccountItem.RealizedProfitLoss` and `AccountItem.GrossRealizedProfitLoss` (both verified present), and
they differ by commissions.
**Problem**: Core tracks commissions separately, so comparing its gross number against a net platform
number would produce a permanent false disagreement — that is, a permanent `FAIL_CLOSED`.
**Decision**: `PlatformPnl.GrossRealized` is defined as realized **excluding** commissions; the adapter maps
it from `GrossRealizedProfitLoss`.
**Fail-closed**: unchanged — a null value is still an unknown, never a zero.

## A3 — `ExecutionRecord.PointValue`

**Spec**: §5.3 lists the execution fields Core consumes, and none of them turns a price difference into
money.
**Problem**: without the instrument's point value ($5 for MES), Core can compute "3.25 points" and nothing
else. NT8 has it on `Instrument.MasterInstrument.PointValue`, which is adapter territory.
**Decision**: the adapter supplies `PointValue` on every `ExecutionRecord`.
**Fail-closed**: a missing or non-positive point value marks the account `PnlStatus.InvalidPointValue`,
which is an unknown, which blocks entries. It is never defaulted to 1.

## A4 — the state and ledger paths are host-level, not config-level

**Spec**: §4 puts `ledgerPath` and `statePath` in the configuration; §6 requires the state to be read at
startup, before the configuration is trusted.
**Problem**: a circular dependency with a hole in it. If the paths came from the config, then deleting or
corrupting `config.json` would leave the guardian unable to find an in-force lockout — turning
"break the config" into a working bypass.
**Decision**: `Guardian` is constructed with both paths; the configuration must **declare the same paths**
or it is rejected with a reason.
**Fail-closed**: the lockout stays readable no matter what happens to the config file.

## A5 — `configSnapshot` is stored as canonical text

**Spec**: §7.1 shows `configSnapshot` as a nested JSON object.
**Problem**: the seal hash must be reproducible byte-for-byte. Storing a parsed object and re-serialising it
to check the hash makes the hash depend on the serialiser's behaviour rather than on the configuration.
**Decision**: the snapshot is the **canonical text** that was hashed. Re-checking is a string comparison
against a fresh SHA-256.
**Fail-closed**: any difference is `SEAL_MISMATCH`, which is a lockout (§7.4).

## A6 — a clock unknown cannot be cleared by the tick that detected it

**Spec**: §10 says `FAIL_CLOSED` "clears by itself the moment the unknown resolves — but it clears *through*
a re-computation".
**Problem**: found by G13a. A clock anomaly set `FAIL_CLOSED`, and the same evaluation then cleared it,
because the P&L happened to be computable — the P&L is not what was in doubt. The guard would have logged
the attack and then carried on as if nothing had happened.
**Decision**: for a clock unknown, the re-computation is the **next coherent observation of the clock**. The
tick that detects an anomaly cannot clear it.
**Fail-closed**: strictly more closed than before.

## A7 — `MaxFlattenAttempts = 3`, and the retry cadence is the tick

**Spec**: §9.4 says "not flat after *N* attempts with backoff", without fixing N or the backoff.
**Decision**: `Constants.MaxFlattenAttempts = 3`, and the retry happens on the next tick rather than in a
spin loop — the tick already is the cadence at which the guardian is allowed to act, and a busy retry loop
inside NT8's thread is a worse failure than a slow one. The attempt count is persisted, so it survives a
restart.
**Fail-closed**: exhausting the attempts does **not** release anything. It logs `LOCKOUT_INCOMPLETE`,
stays `LOCKED`, and keeps retrying.

## A8 — `LOCKED` outranks `FAIL_CLOSED`

**Spec**: §8's transition table has "any → `FAIL_CLOSED`" for ledger, clock and state unknowns, and also has
`LOCKED` as the state a breach produces. It does not say what happens when both are true.
**Problem**: read literally, an unknown arriving after a breach would move the guardian out of `LOCKED`, and
`FAIL_CLOSED` is the *weaker* state — it clears on its own.
**Decision**: `LOCKED` is never downgraded. An unknown that arrives while locked is recorded and the
lockout stands; the only exit remains seal expiry.
**Fail-closed**: yes, and it removes a bypass that the literal reading would have allowed.

---

---

## A9 — firm minimum-hold rules are a known limit, not a feature

**Source**: not Step 2. This came from the second layer of prop-firm research (2026-08-21), which found
per-trade minimum-duration rules at two firms — Elite Trader Funding (*"a minimum duration of ten (10)
seconds—no exceptions"*, inside its HFT clause) and Top One Futures (*"All trades must remain open longer
than 10 seconds"*, with *"Closing any portion of a trade before 10 seconds violates this rule"*).

**Problem**: the spec had nothing to say about them. A lockout triggered inside that window flattens a
position younger than the firm's floor, so the guard would cause a rule violation the trader did not commit.
Top One's partial-close clause closes the obvious escape: no smaller action avoids it. Silence here would
have been the same failure as a plausible default — the code would have had a behaviour the spec did not
admit to.

**Decision**: **flatten anyway, record the age, publish the limit.** Reducing exposure outranks a duration
rule, consistent with exits being fail-open everywhere else in this design. `LOCKOUT_STARTED` carries the
position age so the ledger shows whether a lockout could have tripped a minimum-hold rule. The gap is in
§17.5 and per firm in the compatibility table, rather than left for a trader to find on a bad day.

**Not overstated**: a lockout is at most a daily event and fires on a limit the trader chose, so the overlap
requires the limit to be hit within seconds of entry. Top One tolerates *"Occasional minor violations are tolerable"*; ETF's
sentence sits inside a clause aimed at *"a high number of transactions"*. Neither is a permission, and
neither firm has written anything about risk tools — so the exposure is recorded as real but rare.

**Rejected**: deferring the flatten until the window closes. Holding a position already past the trader's
limit to satisfy a timer is its own risk and can cost more than the violation. It would also turn the guard
into something that keeps a trader in a trade past their own limit — the exact behaviour the product exists
to remove. Kept as a v2 candidate with both objections attached, so whoever picks it up inherits the reasons
instead of rediscovering them.

## Where each one landed in v0.4

*A9 is applied directly to §17.5 of v0.4 and will be folded into the version history at v0.5.*

| # | Absorbed into | Sharpened on approval |
|---|---|---|
| A1 `ReadLines` | §14 (the port signature) | — |
| A2 gross-vs-gross cross-check | §5.3, with a table of what **is** and **is not** compared | yes: commissions are verified by Core alone, unrealized is single-sourced; both stated as blind spots |
| A3 point value | §5.7, its own subsection | yes: it comes from platform metadata and may **never** be a config field, with the bypass it would create |
| A4 host-level paths | §6 | — |
| A5 canonical-text snapshot | §6 | — |
| A6 clock unknown may not self-clear | §10 | — |
| A7 `MaxFlattenAttempts` = 3, retry on the tick | §9 step 4 | — |
| A8 `LOCKED` outranks `FAIL_CLOSED` | §8 | — |

*Written during Step 2, 19 August 2026, against SPEC v0.3. Absorbed into SPEC v0.4 on 20 August 2026.*
