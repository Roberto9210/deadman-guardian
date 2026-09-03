# deadman-guardian — SPEC v0.4

**Status: written before a single line of C#.** Nothing in this document was derived from code that already
exists, because none does. Where it names a NinjaTrader 8 API, that API was verified by reflection against
the installed assemblies (`NinjaTrader.Core` 8.1.8.2) — not quoted from memory. Where it names a .NET
behaviour, that behaviour was executed on this machine under .NET Framework 4.8.9300, the runtime NT8 uses.
Anything unverified is marked as such.

*Changes in v0.2 (both are corrections of real defects in v0.1, not style): the clock defence now covers the
forward direction, which is the direction that actually breaks the seal (§6.4, §7.5, G13); and the time-zone
rule no longer specifies something that cannot work inside NT8 (§5.1). New §17 states what this does not
protect against.*

*Changes in v0.3: `SEAL_EXPIRED` no longer claims a wall-clock trigger it stopped having in v0.2 — the
catalogue (§12) and the transition table (§8) now agree with §7.5, and the event records which basis decided
it. P&L cadence is pinned in §5.6 as two Core constants with their reasons, replacing an undefined "every N
seconds", and states explicitly that a breach is never decided on the ledger's rhythm.*

*Changes in v0.4: the eight details Step 2 had to decide because this document did not fix them are now
**absorbed into the sections they belong to** (A1-A8; [`AMENDMENTS.md`](AMENDMENTS.md) keeps the reasoning
that produced each one). Two were sharpened by Roberto on approving Step 2: §5.3 now states exactly what the
P&L cross-check compares and, more importantly, **what it does not**; and §5.7 states that the contract's
point value comes from platform metadata and may never be typed by the trader, with the reason. Code and
spec are level again: nothing in `GuardianCore` knows a rule this document does not.*

**What this is:** an add-on that stops a prop-firm trader from breaking their own daily loss limit, by
closing everything and refusing to let them re-enter until the session rolls.

**What it is not:** a strategy, a signal source, an optimiser, or a service. See §13.

---

## 1. Threat model, said first

The adversary is not the market. It is the account holder, twenty minutes after the third losing trade,
with a plausible reason why today is different. Every rule below exists because of one of these:

| # | Threat | Answer |
|---|---|---|
| T1 | Revenge trading past the limit | Hard lockout: flatten, cancel, and cancel every new order until the session rolls (§9) |
| T2 | Moving the limit mid-session ("just $200 more") | Commitment seal: config frozen until 17:00 CT, every attempt logged (§7) |
| T3 | Removing the protection when it bites | Account-level AddOn, not a chart indicator (§3.3); removal is visible in the ledger |
| T4 | Restarting NT8 to clear the state | Seal, state and lockout live on disk and are re-read on startup (§6, §8) |
| T5 | Hand-editing the config, state or ledger files | Hash seal + hash-chained ledger: edits are detected, and detection means lockout (§7.4) |
| T6 | Silent failure — the guard believes it is watching and is not | Unknown state is a state: block entries, never guess (§10) |
| T7 | Orders placed after lockout from a DOM, chart, or a running strategy | Enforcement is continuous, not a one-shot flatten (§9.5). Since 2026-08-26 it is *literally* a repeated flatten where the adapter cannot cancel one order by id: the order fills and the next cycle closes it, so the trader pays one round trip. The property — no position can be built past the lockout — is unchanged; the mechanism and the timing are |
| T8 | Winding the system clock **forward** so the seal looks expired | Expiry is measured on a monotonic clock, not on the wall clock (§6.4, §7.5) — the seal is *maintained* when they disagree |
| T9 | Winding the system clock **back** (to fake an earlier session, or after a forward jump) | Backward observations are detected and logged; they never release the seal (§6.4) |

**Not defended against** — stated here and in full in §17, rather than buried: a user who deletes the add-on
with NT8 closed, kills NT8, trades from another platform, or wipes the state directory. Those are *detected
and recorded where possible*, never *prevented*. A guardian that claimed to be unbypassable by its own owner
would be lying — the value is that bypassing it requires a deliberate, premeditated act that leaves a trace,
instead of a moment of weakness at 14:30.

## 2. The one number that matters

> **Hitting the limit and flattening does not guarantee the day ends above the limit.** Between the breach
> and the fill there is slippage, and a gapping market can take the account further down. This add-on
> *bounds exposure and removes the trader's discretion*; it does not bound the loss. Any marketing claim to
> the contrary is a lie and must never appear in this repository.

## 3. Architecture: two layers, one rule

### 3.1 GuardianCore — pure C#, zero NinjaTrader

A library with **no reference to any NinjaTrader assembly**, no file dialogs, no timers of its own, no
`DateTime.Now`. It contains all the thinking:

- day P&L accounting, including commissions (§5)
- the state machine (§8)
- the commitment seal (§7)
- the hash-chained ledger (§11)
- configuration validation (§4)

Everything it needs from the world arrives through four interfaces (§14): `IClock`, `IFileStore`,
`IBrokerActions`, `IAccountFeed`. Given the same inputs it produces the same decisions, which is what makes
it 100% testable without NinjaTrader running.

Target: `netstandard2.0` — loadable both by NT8 (.NET Framework 4.8.1, verified installed) and by a modern
`dotnet test` runner.

### 3.2 NtAdapter — thin, and dumb on purpose

Translation only:

- NT8 events → Core inputs: `Account.ExecutionUpdate`, `Account.OrderUpdate`, `Account.PositionUpdate`,
  `Account.AccountItemUpdate`, `Account.AccountStatusUpdate` *(all five verified to exist on
  `NinjaTrader.Cbi.Account`)*.
- Core decisions → NT8 calls: `Account.Cancel(IEnumerable<Order>)`, `Account.CancelAllOrders(Instrument)`,
  `Account.Flatten(ICollection<Instrument>)`, `Account.FlattenEverything()` *(all four verified)*.
- Account discovery: `Account.All` *(verified static collection)*; identity by `Account.Name`, currency by
  `Account.Denomination`, liveness by `Account.ConnectionStatus` *(verified)*.

**The rule, and the only one that matters for review: no decision may live in the adapter.** No thresholds,
no comparisons against the limit, no "if". If a conditional about money or state appears in NtAdapter, the
change is rejected. The adapter may only pass facts down and execute orders from above.

### 3.3 Why an AddOn and not an indicator

NinjaTrader indicators are attached to a chart. Close the chart, change the instrument, or reload
NinjaScript, and the indicator instance dies with it — a protection that disappears exactly when the trader
is most motivated to make it disappear, and often without them even intending it. The competing products
that ship as indicators carry that hole by construction.

`NinjaTrader.NinjaScript.AddOnBase` *(verified to exist in `NinjaTrader.Core`)* is loaded once at
application level, independent of any chart or instrument, and lives for the whole NT8 session. That is the
only shape in which the promise "you cannot trade past your limit today" can be made honestly.

**Verified on 2026-08-26, and this clause is now CLOSED.** It asked whether an AddOn can veto an order
*before* it reaches the broker, and anticipated two outcomes: a pre-submit hook exists (an addition), or it
does not (and cancelling is the backstop). NT8 still exposes no pre-submit hook to third-party AddOns — but
the live test returned a **third** answer, which neither branch contemplated:

> **The backstop was the weapon.** Cancelling on observation cancelled the guardian's OWN flatten orders,
> 1 ms after the venue accepted them, and cancelled the trader's exits along with them. 167 loops,
> `FLATTEN_VERIFIED` zero, position never closed (`docs/live-test-findings-20260826.md`).

The spec asked to be verified, and the verification came back with an answer that was not among the ones it
expected. So §9.5 is amended, and enforcement after the lockout has **two branches, both permanent**:

- **WITH selective cancellation** (a port that can cancel one order by id): orders that INCREASE exposure are
  cancelled on observation. Orders that reduce it, and the guardian's own flatten orders, are never touched.
- **WITHOUT it** — an older adapter, a test double, any other adapter: post-lockout protection is **by
  repeated flatten, one cycle later**. The order fills, the guardian sees the position, and closes it.

The second branch is not an interim excuse: it stays true after selective cancellation lands, because it is
what the guardian must do wherever that capability is absent. It is the foundation, not a detour.

**The cost of the second branch, stated rather than buried:** the trader pays one round trip on an order that
would previously have been cancelled before filling. That is a real cost, and it is the smaller one — the
alternative cancelled their exits and trapped them in the position (§17).

### 3.4 What NinjaTrader already provides, and why this add-on does not lean on it

Investigated before writing the adapter, because building on top of something that already works would be
waste, and building on top of something that only *looks* like it works would be worse.

**What is actually there** (reflection over `NinjaTrader.Core` 8.1.8.2, cross-checked against the
first-party [Account Class reference](https://developer.ninjatrader.com/docs/desktop/account_class)):

| Member | What it is | Documented in the NinjaScript API? |
|---|---|---|
| `Cbi.Risk` + `Cbi.InstrumentRisk` | A **named risk template**, savable per account: `InitialMargin`, `MaintenanceMargin`, `BuyIntradayMargin`, `SellIntradayMargin`, `MaxOrderSize`, `MaxPositionSize`, `IsEnabledForTrading`. Per instrument, and **no daily-loss concept anywhere in it** | **No** |
| `Account.DailyLossLimit` (double), and `AccountItem.DailyLossLimit`, `WeeklyLossLimit`, `DailyProfitTrigger`, `WeeklyProfitTrigger`, `TrailingMaxDrawdown` | Account-level values **reported by the venue**. The set matches the risk fields Tradovate-style venues expose, which is where firms such as TradeDay tell traders to set them | **No** |
| `Account.IsAutoLiquidationEnabled`, `Account.LiquidationState` (`Fail`, `ValidationFail`, `Disabled`, `Enabled`, `Excluded`), `AccountLiquidationChanged` | The **venue's** auto-liquidation feature, whose state NT8 mirrors | **No** |
| `OrderState.AcceptedByRisk` | An order state meaning **the venue's risk system accepted the order** — i.e. the risk check happens at or after submission, not before it | The state enum is documented; this member's semantics are not |

All four rows have public getters *and setters* in IL. None of them appears in the published Account class
reference, whose complete member list is `Cancel`, `CancelAllOrders`, `Change`, `CreateOrder`, `Flatten`,
`Get`, `Submit`, `All`, `Connection`, `Denomination`, `Executions`, `Name`, `Orders`, `Positions`,
`Strategies`.

**Decision: ignore them. The guardian neither leans on them nor competes with them.**

1. A public setter is not an enforcement contract. Nothing published says that assigning
   `Account.DailyLossLimit` *does* anything beyond writing NT8's local mirror of a number the venue
   reported — and for a safety product, "probably enforces" is indistinguishable from "does not".
2. Undocumented members carry no stability guarantee. If a future NT8 renames or removes one, a guardian
   built on it stops protecting **silently**, which is the exact failure mode §10 exists to prevent.
3. `Risk`/`InstrumentRisk` is not the same feature under another name: margins and size caps are not a
   daily loss limit, and no amount of configuring them produces one.
4. `AcceptedByRisk` confirms the shape of the platform rather than offering a hook: risk lives at the
   venue, after submission. It is consistent with the verified absence of any pre-submit event (§3.3).

*(v2 candidate, not v1: **reading** `AccountItem.DailyLossLimit` as a sanity check against the
`firmDailyLossLimit` the trader typed. Read-only, never enforcement, and only if the field turns out to be
populated consistently across venues. §13 keeps v1 from reading the firm's numbers at all.)*

**What must be said out loud, in the README as well as here**: where a **venue-side** self-set daily loss
limit exists — Tradovate exposes one, and firms that use it document that once set it cannot be overridden
even by their own staff — that limit is **stronger than this add-on**, because it lives inside the venue and
does not depend on the trader's machine being on, or on NT8 running, or on this code being installed. This
add-on is for the case where no such venue-side limit exists, or where a trader wants a stricter personal
limit layered on top of it with a local auditable record. Recommending it over a venue-side limit that is
available would be selling the weaker mechanism.

## 4. Configuration: no defaults, ever

A field that is missing, empty, unparseable, or out of range does **not** fall back to a plausible value.
There is no plausible value for someone else's risk limit. The add-on refuses to arm and says so.

```jsonc
{
  "schemaVersion": 1,              // int, required, must equal 1
  "accounts": ["Sim101"],          // non-empty array of NT8 account names, no duplicates
  "currency": "UsDollar",          // must match Account.Denomination of every guarded account
  "firmDailyLossLimit": "1000.00", // decimal string > 0 — the number the FIRM will fail you at
  "personalDailyLossLimit": "600.00", // decimal string > 0, STRICTLY LESS than firmDailyLossLimit
  "sessionResetTimeZone": "America/Chicago", // IANA id; must be in the embedded map of §5.1
  "sessionResetLocalTime": "17:00",          // HH:mm in that zone
  "ledgerPath": "…/guardian/ledger.jsonl",   // absolute, writable
  "statePath":  "…/guardian/state.json",     // absolute, writable
  "pnlToleranceUsd": "5.00"        // decimal string >= 0, see §5.4
}
```

Validation rules, all fail-closed:

1. Any unknown key ⇒ **reject**. (A typo'd key is a rule the user thinks is active and isn't.)
2. `schemaVersion` unknown ⇒ **reject**. Never "best effort" on a schema we do not understand.
3. `personalDailyLossLimit >= firmDailyLossLimit` ⇒ **reject**. The whole product is the gap between those
   two numbers; without it there is nothing to protect.
4. `accounts` empty, or naming an account absent from `Account.All` at arming time ⇒ **reject**.
5. Any guarded account whose `Denomination` differs from `currency` ⇒ **reject**. Cross-currency arithmetic
   is a guess.
5b. `sessionResetTimeZone` not present in the embedded IANA→Windows map (§5.1) ⇒ **reject**, with a message
   naming the ids that are supported. Never fall back to the machine's local zone: a guardian that resets at
   the wrong hour is worse than none.
6. Ledger or state path not writable ⇒ **reject**. A guardian that cannot record cannot protect (§11.5).
7. Money is parsed as `decimal`, never `double`, and rejected if it has more than 2 decimal places.

**Rejection is not a lockout.** A guardian that never armed does not block anything — it displays
`NOT PROTECTED` and the reason, loudly. It must never be possible to believe you are protected when you are
not. Once *armed*, the fail-closed rules of §10 apply instead.

## 5. Day P&L accounting

### 5.1 The trading day

`[sessionResetLocalTime` on day D, `sessionResetLocalTime` on day D+1`)` in `sessionResetTimeZone`,
DST-aware via `TimeZoneInfo`. Default configuration is 17:00 America/Chicago, matching the CME session roll
that prop firms use.

**The IANA trap, and why the config still uses IANA ids.** GuardianCore runs inside NT8 on .NET Framework
4.8.1, where `TimeZoneInfo.FindSystemTimeZoneById` accepts **only Windows ids**. Verified on this machine,
under .NET Framework 4.8.9300:

```
FindSystemTimeZoneById("America/Chicago")      -> TimeZoneNotFoundException
FindSystemTimeZoneById("Central Standard Time") -> OK, (UTC-06:00) Central Time (US & Canada)
```

IANA ids only resolve on .NET 6+. A test suite run under `dotnet test` on a modern runtime would therefore
pass with `"America/Chicago"` while the same configuration is rejected every time inside real NT8 — a green
suite hiding a product that never arms. That failure mode is the reason this paragraph exists.

**Rule**: the config keeps IANA ids (portable, unambiguous, what a trader will copy from anywhere), and Core
carries a **minimal embedded map**, no dependency, no `TimeZoneConverter` package:

| Config value (IANA) | Windows id used at runtime |
|---|---|
| `America/Chicago` | `Central Standard Time` |
| `America/New_York` | `Eastern Standard Time` |
| `UTC` | `UTC` |

Resolution order: try the IANA id directly (works on .NET 6+ test runners), and on `TimeZoneNotFoundException`
fall back to the mapped Windows id. Both paths must produce the same `TimeZoneInfo`, and G12 asserts it.
An id outside the map is rejected at config time (§4 rule 5b) — the map grows by commit, never by guessing.

DST correctness was verified on the same runtime with the Windows id: `2026-03-09 17:00` CT → `22:00Z`
(daylight time), `2026-11-02 17:00` CT → `23:00Z` (standard time). G12 pins both dates.

**Step 3 obligation**: the mapping must be re-verified *inside the NT8 process*, not only under
`dotnet test`. Until that check exists, the fallback path is "verified on this machine's .NET Framework",
which is the same runtime but not the same host.

### 5.2 The number

```
dayPnL(account) = realizedSinceDayStart + unrealizedOpen − commissionsAndFeesSinceDayStart
dayLoss(account) = max(0, −dayPnL(account))
totalDayLoss     = Σ dayLoss over guarded accounts        // no netting between accounts
```

Losses are summed, never netted against another account's profit: a firm fails each account on its own
number, and netting would let a winning account mask a losing one into a breach.

### 5.3 Sources, and exactly what the cross-check covers

- **Primary (authoritative): executions.** Core accumulates from `Execution` objects —
  `Price`, `Quantity`, `MarketPosition`, `Commission`, `Time`, `Instrument` *(all verified members of
  `NinjaTrader.Cbi.Execution`)*. Commission is included at the moment it is reported; it is never estimated.
- **Cross-check: the platform's own figures**, via `Account.Get(AccountItem, currency)` *(verified)*.

The cross-check is **gross against gross**, and its scope is written out here because a check whose
boundaries are vague is a check people over-trust:

| Quantity | Core's source | Platform's source | Cross-checked? |
|---|---|---|---|
| Realized P&L, **excluding** commissions | its own execution accounting (§5.2) | `AccountItem.GrossRealizedProfitLoss` | **yes** — this is the whole of the comparison |
| Commissions and fees | `Execution.Commission`, accumulated per account | `AccountItem.RealizedProfitLoss` carries them netted, and is **not read** | **no** |
| Unrealized P&L | none — Core has no market data | `AccountItem.UnrealizedProfitLoss` | **no** — single-sourced from the platform |

Gross against gross is deliberate: Core tracks commissions separately, so comparing its gross figure against
a net platform figure would produce a permanent difference, and therefore a permanent `FAIL_CLOSED` (A2).

**What that leaves uncovered, said plainly.** Commissions are verified by *Core alone*. If NT8 reports a
commission on `Execution.Commission` that does not match what the broker actually charges, nothing in this
design notices — the number is taken as given. Unrealized P&L is the same shape of exposure from the other
side: it comes from one source only, so a platform that mis-values an open position mis-values it here too.
Both are blind spots by construction, not oversights, and both are inside the guarded number.

*(v2 candidate, deliberately not implemented in v1: a second comparison of Core's net realized
(gross minus commissions) against `AccountItem.RealizedProfitLoss`, which would close the commission blind
spot. Left out because it doubles the tolerance surface — two comparisons are two ways to manufacture a
false `FAIL_CLOSED` — and because Step 2 is approved and frozen.)*

### 5.4 Disagreement is an unknown, not a tie-break

If `|coreGrossRealized − platformGrossRealized| > pnlToleranceUsd`, the add-on does **not** pick the friendlier
number and does not average them. It logs `PNL_DISAGREEMENT` with both values and enters `FAIL_CLOSED`
(§10). Two sources that disagree mean the accounting is wrong, and a wrong accounting is exactly how a
trader ends the day past a limit that was "being watched".

### 5.5 Unrealized needs a price

An open position with no current market data has no computable unrealized P&L. That is an unknown ⇒
`FAIL_CLOSED`. It is never treated as zero.

### 5.6 Cadence: deciding is not the same as recording

Two different rhythms, two Core constants, neither of them config — these are engineering limits, not risk
preferences the trader gets to tune.

| Constant | Value | Why that value |
|---|---|---|
| `PnlEvaluationIntervalMs` | `1_000` | How stale the guard's view may get when nothing is happening. Evaluation is normally driven by NT8 events (`ExecutionUpdate`, `PositionUpdate`, `AccountItemUpdate`), which arrive far faster than this; the timer is a **floor**, so that a silent feed still produces a re-evaluation instead of an indefinitely stale "all fine". One second bounds the blind window without turning the guard into a busy loop. |
| `PnlCheckpointIntervalMs` | `300_000` (5 min) | How often the routine P&L heartbeat is **written to the ledger**. §11.4 keeps one ledger file forever with no rotation, so the file has to stay something a human can still read: 5 minutes is ~78 lines per session and ~20k lines per year. At the evaluation cadence it would be 4,700 lines *per day*, and an audit trail nobody can read is not an audit trail. |

Evaluation happens on every NT8 event and at least every `PnlEvaluationIntervalMs`. **Every breach decision
is taken on an evaluation, never on a checkpoint** — the ledger cadence must never be able to delay a
lockout. A checkpoint is additionally written on every state transition, so the P&L that accompanied a
transition is always on the record even if it fell between two heartbeats. `LIMIT_BREACHED` carries its own
P&L payload and does not depend on a checkpoint existing.

### 5.7 The point value comes from the platform, never from the trader

Turning "3.25 points" into "$16.25" needs the contract's point value ($5.00 for MES). Core cannot know it,
so the adapter reads it from NT8 instrument metadata — `Instrument.MasterInstrument.PointValue` — and puts
it on every `ExecutionRecord` (§14).

**It is not a configuration field and must never become one.** The reason is not tidiness: a trader who can
type the point value can type `2.50` instead of `5.00` for MES, and the guardian would then compute half of
every loss and let the real loss run to twice the limit before tripping. A configuration field that silently
doubles the effective limit is a bypass with a friendly name. Nor is it a risk preference the way the
personal limit is — it is a fact about the contract, and facts come from the platform (A3).

Fail-closed: a missing, zero or negative point value marks the account `INVALID_POINT_VALUE`, which is an
unknown, which blocks entries (§10). It is never defaulted to 1.

## 6. Persistence

Three files, all local:

| File | Content | Written |
|---|---|---|
| `state.json` | current state, day key, seal, lockout flag, last-seen clock, P&L checkpoint | atomically, before any broker action |
| `ledger.jsonl` | append-only hash chain (§11) | append + flush + fsync before the action it describes |
| `config.json` | the user's configuration (§4) | by the user; read-only to the add-on |

**The state and ledger paths are host-level, not config-level** (A4). The add-on is constructed with both,
and a configuration must *declare the same two paths* or it is rejected. If the paths came from the
configuration alone there would be a circular dependency with a hole in it: the state has to be read at
startup, before any configuration is trusted, so a trader who deleted or corrupted `config.json` would leave
the guardian unable to find a lockout that is still in force. "Break the config" must not be a way out.

The seal's `configSnapshot` is stored as the **canonical text** that was hashed, not as a nested object
(A5): re-serialising a parsed object to re-check a hash makes the hash depend on the serialiser instead of
on the configuration.

1. **Atomic writes**: write temp file in the same directory, flush, fsync, then `File.Replace`/rename. A
   torn state file must be impossible; an unreadable one is handled by rule 3.
2. **Startup order**: read state → verify ledger chain → verify seal → decide state. In that order, because
   each step's trust depends on the previous one.
3. **Unreadable, missing-when-expected, or schema-unknown state ⇒ `FAIL_CLOSED`**, never `DISARMED`. This
   is the rule that makes "kill the process mid-flatten" safe: the state file already said `LOCKED` before
   the first broker call, so the restart resumes locked (§9.1).
4. **Two clocks, because one of them is the attacker's.** `IClock` exposes both `UtcNow` (wall clock, used
   for timestamps and the session boundary) and `MonotonicMs` (a counter that only moves forward and that
   the trader cannot set). State carries `lastSeenUtc`, plus the pair `(wallAtArm, monoAtArm)` written at
   arming and `(wallLastTick, monoLastTick)` updated on every tick.

   - **Forward jump — the one that matters.** Setting the clock to 17:01 to make the seal look expired is
     the cheap bypass, and v0.1 did not cover it. In-session it is detectable: if
     `Δwall − Δmono > ClockDivergenceToleranceMs`, the wall clock moved without time passing. Log
     `CLOCK_ANOMALY`, enter `FAIL_CLOSED`, and **the seal is maintained** (§7.5).
   - **Backward jump.** `now < lastSeenUtc − ClockDivergenceToleranceMs` at startup or at any tick: log
     `CLOCK_SUSPECT` (across a restart there is no monotonic continuity, so manipulation cannot be *proved*,
     only recorded) or `CLOCK_ANOMALY` when monotonic continuity does exist. Either way the seal is
     maintained and the state goes `FAIL_CLOSED`.
   - **Every backward observation is written to the ledger, always**, even when it changes nothing. This is
     the trace: a premeditated bypass that later corrects the clock leaves a non-monotonic `tsUtc` sequence
     in an append-only hash-chained file, which is exactly what a dispute needs.

   `ClockDivergenceToleranceMs = 120_000` is a Core constant, not config: it absorbs NTP steps and
   scheduler drift, and it is not a risk preference the trader gets to tune.

   **Implementation note, verified rather than assumed.** `Environment.TickCount64` **does not exist on
   .NET Framework 4.8** — reflection over `System.Environment` on this machine returns only
   `TickCount : Int32`, which wraps every 24.9 days and is therefore unusable as a monotonic source. The
   adapter backs `MonotonicMs` with `System.Diagnostics.Stopwatch.GetTimestamp()` scaled by `Frequency`
   (present in both `netstandard2.0` and .NET Framework 4.8, high-resolution confirmed on this machine).
   `kernel32!GetTickCount64` via P/Invoke also works here and is the documented alternative, rejected for
   v1 only because it adds native interop for no gain.

   **Known gap, by construction**: monotonic counters restart with the process. A trader who closes NT8,
   moves the clock, and reopens it presents a coherent pair to a Core that has no memory of real elapsed
   time. See §17.2.

## 7. Commitment mode (the seal)

### 7.1 What arming does

Arming is a deliberate act by the trader, once per trading day. On arming, Core:

1. canonicalises the config (§11.2 rules), computes `sealHash = SHA-256(canonical config bytes)`;
2. writes the seal into `state.json`:

```jsonc
{
  "sealVersion": 1,
  "sealHash": "<64 hex>",
  "configSnapshot": { /* the exact config that was sealed, verbatim */ },
  "armedAtUtc": "2026-08-19T18:20:00Z",
  "expiresAtUtc": "2026-08-19T22:00:00Z",   // next sessionResetLocalTime in sessionResetTimeZone
  "dayKey": "2026-08-19",                    // the trading day this seal governs
  "ledgerHeadHash": "<64 hex>"               // ties the seal to a point in the ledger
}
```

3. logs `ARMED` and `SEAL_CREATED`.

### 7.2 While sealed

**Every configuration change is rejected until `expiresAtUtc`.** Not "loosening is rejected" — *every*
change, including one that looks stricter. A change that is safe today is a debate at 14:30 tomorrow, and
the point of a commitment device is that there is nothing to debate. Each attempt is logged as
`CONFIG_CHANGE_REJECTED` with the hash of what was offered, so the pattern of attempts is visible later.

*(v2 candidate, deliberately deferred: allow strictly-tightening changes. Deferred because it needs a
provable "strictly tighter" comparison across every field, and v1 does not need it.)*

### 7.3 Expiry

At `expiresAtUtc` the seal expires on its own: log `SEAL_EXPIRED`, clear the lockout, transition to
`DISARMED`. The trader must arm again for the next day — deliberately, as an act.

Expiry is never early. How it is decided is §7.5.

### 7.4 Tamper detection

At startup and on every config read: recompute `SHA-256` over `configSnapshot` and compare to `sealHash`;
compare `configSnapshot` to the config file on disk.

- Mismatch between snapshot and `sealHash` ⇒ the state file was edited ⇒ log `SEAL_MISMATCH` ⇒ **`LOCKED`**.
- Mismatch between the config file and `configSnapshot` while sealed ⇒ log `CONFIG_TAMPERED` ⇒ **`LOCKED`**,
  and the sealed snapshot — not the edited file — remains in force until expiry.

Tampering resolves to lockout, never to "reload the new config". Detected manipulation is treated as an
attempt to trade past the limit, because that is what it is.

### 7.5 How expiry is decided (the clock cannot open the seal)

`sealDuration = expiresAtUtc − armedAtUtc` is fixed at arming and stored. Expiry is then evaluated as:

1. **With monotonic continuity** (same process run since arming — the in-session case): the seal expires when
   `MonotonicMs − monoAtArm >= sealDuration`. **The wall clock does not participate in this decision.**
   Moving the system time to 17:01 therefore does nothing to the seal; it only produces `CLOCK_ANOMALY` and
   `FAIL_CLOSED` (§6.4), which blocks entries rather than releasing them.
2. **Without monotonic continuity** (NT8 was restarted since arming): expiry falls back to the wall clock,
   `UtcNow >= expiresAtUtc`, because no better evidence exists. This is the documented gap of §17.2.
3. In both paths, if the wall clock and the monotonic counter disagree beyond tolerance, **the seal is
   maintained**. There is no combination of clock values that releases a seal early. The asymmetry is
   deliberate: an unnecessary extra hour of protection costs the trader nothing they should want, and an
   hour of protection lost too early is the entire failure this product exists to prevent.

**Measured in Step 3, and it corrects what this section used to assume.** The worry was that a sleeping
machine would stop the monotonic counter, so a suspend would look like a forward jump of the wall clock and
raise a false anomaly. Across two real S3 sleeps inside the target platform, `Stopwatch` and
`GetTickCount64` **both kept counting**: `wall − Stopwatch` moved by 41 ms and 53 ms, where a stopped
counter would have moved by about 5,000 ms. Sleep therefore neither extends the seal nor trips
`CLOCK_ANOMALY`, and the 120,000 ms tolerance sits some 2,000× above the worst observed divergence. Details
and limits — two ~5 s sleeps, S3 only, hibernation untested — in
[`nt/STEP3_FINDINGS.md`](nt/STEP3_FINDINGS.md) §2.

The rule stands unchanged regardless, because it never depended on that assumption: whenever the two clocks
disagree, the seal is maintained. What did change is that a divergence large enough to matter is now known
to be evidence of something other than sleep.

A separate consequence, also measured: **the guardian does not evaluate while the machine is suspended**,
and NinjaTrader rebuilds its connections on resume, so entries stay blocked for a few tens of seconds after
the lid opens — fail-closed, for the right reason (§10).

## 8. State machine

| State | Meaning | Entries allowed? |
|---|---|---|
| `DISARMED` | not protecting; no seal in force | yes (add-on does nothing) |
| `ARMED` | protecting; seal in force; P&L known and within limit | yes |
| `LOCKED` | limit breached, or tamper detected; lockout in force until seal expiry | **no** |
| `FAIL_CLOSED` | state unknown (P&L, account, clock, ledger) | **no** |

| From | To | Trigger | Side effects |
|---|---|---|---|
| `DISARMED` | `ARMED` | valid config + explicit arm | seal written, `ARMED` logged |
| `ARMED` | `LOCKED` | `totalDayLoss >= personalDailyLossLimit` | §9 sequence |
| `ARMED` | `FAIL_CLOSED` | any unknown of §10 | reason logged |
| `ARMED` | `LOCKED` | tamper detected (§7.4) | `SEAL_MISMATCH` / `CONFIG_TAMPERED` |
| `FAIL_CLOSED` | `ARMED` | unknown resolved **and** re-computed P&L within limit | `FAIL_CLOSED_CLEARED` logged |
| `FAIL_CLOSED` | `LOCKED` | unknown resolved **and** P&L already past limit | §9 sequence |
| `LOCKED` | `DISARMED` | seal duration elapsed per §7.5 (monotonic in session, wall clock after a restart) | `SEAL_EXPIRED`, `LOCKOUT_CLEARED` |
| any | `FAIL_CLOSED` | ledger unwritable / chain broken / clock anomaly | §11.5, §6.4 |

`LOCKED` has no manual exit. Not a button, not a config key, not a hotkey. The only exit is time.

**`LOCKED` outranks `FAIL_CLOSED`** (A8). The table's "any -> `FAIL_CLOSED`" row does not apply to a locked
guardian: an unknown that arrives after a breach is recorded, and the lockout stands. Read the other way
round, an unknown would move the guardian into the *weaker* of the two states — one that clears by itself —
which is a bypass anyone could trigger by unplugging the data feed.

## 9. The lockout sequence

Ordered, idempotent, and resumable — it must survive being killed at any point.

1. **Persist first.** Write `LOCKED` to `state.json` and append `LIMIT_BREACHED` to the ledger **before any
   broker call**. If the process dies here, the restart reads `LOCKED` and resumes at step 2. The reverse
   order (act, then record) would let a crash mid-flatten come back as "armed and fine".
2. **Cancel** all working orders on every guarded account (`Account.Cancel` / `CancelAllOrders`); log
   `ORDERS_CANCELLED` with the count.
3. **Flatten** every guarded account (`Account.Flatten` / `FlattenEverything`); log `FLATTEN_REQUESTED`.
4. **Verify**, do not assume: re-read positions and working orders. Flat and empty ⇒ `FLATTEN_VERIFIED`.
   Not flat after `MaxFlattenAttempts` = **3** attempts ⇒ `LOCKOUT_INCOMPLETE` (loud UI, stays `LOCKED`,
   keeps retrying). The add-on never reports success it has not observed. The retry cadence is the tick
   (§5.6), not a spin loop: the tick is already the rhythm at which the guardian is allowed to act, and a
   busy retry loop inside NT8's thread is a worse failure than a slow one. The attempt count is persisted,
   so it survives a restart. **Exhausting the attempts releases nothing** — it makes the lockout louder,
   not shorter (A7).

   > **Nota de nombres, 2026-09-02 — anotación, no reescritura.** El símbolo se llama hoy
   > `Constants.FlattenAttemptsBeforeHuman` y el campo del evento es `needsHuman`, no `exhausted`.
   > **La CONDUCTA que este paso describe no cambió**: ya decía *"keeps retrying"* y *"exhausting the
   > attempts releases nothing"*, que era y sigue siendo correcto — de hecho esta especificación fue
   > lo único que nunca afirmó un tope. Lo que se renombró fueron los dos identificadores que sí lo
   > afirmaban. Se anota para que grepear el nombre viejo no dé cero.
5. **Keep enforcing.** — **NOT IMPLEMENTED since 2026-08-27 (`a916bba`).** A single flatten is not a
   lockout; the DOM, a chart, and a running strategy can all still submit. What this step specified for
   those orders is `G8`, and `G8` is not implemented: they reach the broker and can fill. What the
   guardian does instead is keep attempting the flatten and verify it (step 4), for the rest of the day.

   > **Nota, 2026-09-03 — anotación, no reescritura.** Este paso afirmaba en presente la conducta que
   > `a916bba` retiró el **2026-08-27**, después de que cancelar a ciegas cancelara el flatten propio del
   > guardián y cuatro órdenes del trader (**A11**). Es la **misma afirmación que `G8`**, escrita otra vez
   > en prosa, y **siguió en pie siete días después** de que la enmienda diera la familia por cerrada:
   > la marcó un test (`C_RetractedPhraseTests`), no una lectura.
   >
   > **La cláusula afirmativa NO se restituye aquí ni entre comillas**, y eso es deliberado: repetirla
   > volvería a poner la oración retirada en un documento vivo, que es exactamente lo que el test
   > prohíbe. Su texto exacto está en el diff de este commit y la garantía intacta está en `§15 G8`,
   > que **no** se reescribe (**A12**) porque el hueco es real. **El paso no se borra**: se marca, para
   > que quien lea la secuencia vea que falta un escalón y cuál es.

**Measured in Step 3**, on `Sim101` with one resting limit order: **14.4 ms** from observing a live order to
the cancel being submitted, and **315.9 ms** for the whole submit-to-cancelled cycle, of which 301 ms are
venue and platform legs that no add-on can shrink. The order was cancelled while still `Accepted`, before it
ever reached `Working`. One sample, on a simulated venue, timing a single cancel and not the full lockout —
the caveats are in [`nt/STEP3_FINDINGS.md`](nt/STEP3_FINDINGS.md) §5. It is also the arithmetic behind §2:
a 14 ms reaction does not help with the 300 ms the market gets anyway.

Steps 2–4 are idempotent: running them twice is harmless, which is what makes resumption safe.

## 10. Unknown state ⇒ fail-closed

Any of these means Core does not know the truth, and therefore blocks entries:

- P&L not computable (no market data for an open position, §5.5)
- P&L sources disagree beyond tolerance (§5.4)
- a guarded account is absent from `Account.All`, disconnected, or its denomination changed
- clock anomaly (§6.4)
- ledger not writable or chain verification fails (§11.5)
- state or seal unreadable, or of an unknown schema version (§6.3)

**What "blocks entries" means, per state — stated because the two states enforce differently and
the difference is deliberate:**

- In `LOCKED`, the guardian ACTS: an observed order on a guarded account is cancelled, positions were
  flattened, and both keep happening for the rest of the day.
- In `FAIL_CLOSED`, the guardian performs **no broker action at all** — no cancel, no flatten.
  `EntriesAllowed = false` is a declaration consumed by the window and by anything polite enough to
  ask; it physically stops nothing. This is deliberate and it is the right choice: cancelling on an
  unknown would let a false alarm kill a protective stop, which is the mirror error — the guardian
  causing the loss it exists to prevent. Fills that happen anyway ARE observed and accounted; the
  moment the unknown resolves and the figures are trustworthy again, the ordinary breach path takes
  over with everything that happened in between already counted. While the unknown PERSISTS, the
  limit is not enforced — the guardian has no number it can honestly enforce, and enforcing on an
  untrusted number is the same mirror error by other means.

`FAIL_CLOSED` is not a lockout: it clears by itself the moment the unknown resolves — but it clears
*through* a re-computation, never by assumption. **For a clock unknown the re-computation is the next
coherent observation of the clock**: the tick that detects an anomaly may not clear it (A6). Step 2 found
this the hard way — a clock anomaly set `FAIL_CLOSED` and the same evaluation cleared it again because the
P&L happened to be computable, which was never what was in doubt. It is logged on entry and on exit, with the reason, so
"the guard was blind for 40 minutes" is a thing you can find out afterwards.

## 11. Ledger

### 11.1 Shape

Append-only JSONL, one event per line, same scheme as deadman:

```jsonc
{"seq":1,"tsUtc":"2026-08-19T18:20:00.123Z","event":"ARMED","schemaVersion":1,
 "payload":{…},"prev":"genesis","hash":"<64 hex>"}
```

`hash = SHA-256(canonical JSON of the entry without the hash field)`, full 64 hex characters. First entry
carries `prev: "genesis"`.

### 11.2 Canonicalisation (so the hash is reproducible)

Keys sorted lexicographically; UTF-8, no BOM; no insignificant whitespace; timestamps ISO-8601 UTC with
milliseconds and `Z`; **money as decimal strings with exactly 2 decimals** (`"600.00"`), never as JSON
numbers — a float in a money field is a rounding bug waiting for a bad day.

### 11.3 Verification

`Ledger.Verify()` returns `Ok` or the `seq` of the first broken link. Any human can re-run it; the tests
in Step 2 will do it after every scenario.

### 11.4 Rotation

One file, growing, with `DAY_OPENED` / `DAY_CLOSED` markers. No rotation in v1 — a rotating log is a log
with a seam, and seams are where evidence goes missing. Revisit when a file gets large enough to matter.

### 11.5 If the ledger cannot be written

`FAIL_CLOSED`. A guardian that cannot record what it did is not a guardian, and the moment when the disk is
full must not be the moment when the limit stops being enforced.

## 12. Event catalogue v1

Versioned with `schemaVersion`. Adding an event is a minor version; changing a payload field is a major one.

| Event | Fired when | Key payload |
|---|---|---|
| `GUARDIAN_STARTED` | add-on loads | version, machine, ntVersion |
| `STATE_RESTORED` | state read at startup | state, dayKey, sealHash |
| `STATE_CORRUPT` | state unreadable / unknown schema | error, rawLength |
| `CONFIG_LOADED` | config read and valid | configHash |
| `CONFIG_REJECTED` | validation failed | reasons[] (all of them, not the first) |
| `ARMED` | arming succeeded | dayKey, personalLimit, firmLimit, accounts[] |
| `SEAL_CREATED` | seal written | sealHash, expiresAtUtc, ledgerHeadHash |
| `SEAL_VERIFIED` | seal re-checked at startup | sealHash |
| `SEAL_MISMATCH` | state edited by hand | expectedHash, actualHash |
| `CONFIG_TAMPERED` | config file differs from sealed snapshot | sealedHash, onDiskHash, changedKeys[] |
| `CONFIG_CHANGE_REJECTED` | change attempted while sealed | offeredHash, changedKeys[], minutesToExpiry |
| `DAY_OPENED` / `DAY_CLOSED` | session boundary crossed | dayKey |
| `PNL_CHECKPOINT` | every `PnlCheckpointIntervalMs`, and on every state transition (§5.6) | dayPnL, dayLoss, perAccount{}, trigger (`interval` \| `transition`) |
| `PNL_DISAGREEMENT` | sources differ > tolerance | coreValue, platformValue, delta |
| `PNL_UNCOMPUTABLE` | missing price for an open position | account, instrument |
| `ACCOUNT_UNKNOWN` | guarded account missing / disconnected | account, connectionStatus |
| `CLOCK_ANOMALY` | wall/monotonic divergence beyond tolerance, in either direction, with monotonic continuity | direction, lastSeenUtc, nowUtc, deltaWallMs, deltaMonoMs, sealMaintained |
| `CLOCK_SUSPECT` | wall clock observed going backwards without monotonic continuity (e.g. across a restart) — recorded, not provable | lastSeenUtc, nowUtc, deltaSeconds, sealMaintained |
| `FAIL_CLOSED_ENTERED` / `FAIL_CLOSED_CLEARED` | §10 | reason |
| `LIMIT_BREACHED` | `totalDayLoss >= personalDailyLossLimit` | dayLoss, limit, perAccount{} |
| `ORDERS_CANCELLED` | step 9.2 | account, count, orderIds[] |
| `FLATTEN_REQUESTED` | step 9.3 | account, instruments[] |
| `FLATTEN_VERIFIED` | positions confirmed flat | account, attempts |
| `LOCKOUT_INCOMPLETE` | not flat after N attempts | account, remainingPositions[], attempts |
| `ORDER_REJECTED_LOCKED` | order seen while `LOCKED` | account, orderId, instrument, action |
| `SEAL_EXPIRED` | seal duration elapsed (§7.5): `MonotonicMs − monoAtArm >= sealDuration` in session, `UtcNow >= expiresAtUtc` after a restart | dayKey, basis (`monotonic` \| `wallclock`), sealDurationMs, elapsedMs |
| `LOCKOUT_CLEARED` | lockout released at expiry | dayKey, lockedDurationMinutes |
| `DISARMED` | back to idle | dayKey |
| `LEDGER_VERIFY_FAILED` | chain broken | brokenSeq |
| `GUARDIAN_STOPPED` | NT8 shutdown / add-on unload | state at exit |

## 13. What v1 does not do

- **No signals, no analysis, no entries.** The only orders it ever sends are cancels and flattens.
- **No network.** No telemetry, no cloud sync, no licence check, no auto-update, no crash reporting. The
  add-on opens no socket. This is a testable property and Step 2 will assert it.
- **No firm API.** It does not read the firm's dashboard; the firm limit is a number the trader types in.
- **Only the daily loss limit.** Not trailing drawdown, not the consistency rule, not max position size, not
  news-trading windows, and not firm minimum-hold rules (§17.5). Those are v2 candidates and must not be
  implied anywhere in the UI.
- **One platform.** NinjaTrader 8 only. Trading the same account from another platform is outside its sight
  (and the ledger will show the guard's view diverging, which is the honest failure mode).
- **No guarantee about the final number** — see §2.

## 14. Seams (the interfaces Step 2 tests against)

```csharp
interface IClock          { DateTime UtcNow { get; }      // wall clock: timestamps, session boundary
                            long MonotonicMs { get; } }   // only moves forward; the trader cannot set it
                                                          // adapter: Stopwatch.GetTimestamp()/Frequency
                                                          // (Environment.TickCount64 does NOT exist on .NET FW 4.8)
interface IFileStore      { bool Exists(string p); string ReadAllText(string p);
                            void WriteAtomic(string p, string contents); void AppendLine(string p, string line);
                            IEnumerable<string> ReadLines(string p); }  // A1: Verify() reads the chain back
interface IBrokerActions  { void CancelAllOrders(string account); void Flatten(string account);
                            IReadOnlyList<PositionSnapshot> GetPositions(string account);
                            IReadOnlyList<OrderSnapshot> GetWorkingOrders(string account); }
interface IAccountFeed    { IReadOnlyList<string> KnownAccounts { get; }
                            AccountState GetState(string account);   // connection, denomination
                            PlatformPnl GetPlatformPnl(string account); }
```

Core is a pure function of (config, persisted state, event stream, clock). Every test in Step 2 drives it
through these four, with fakes; no NinjaTrader, no disk unless the test wants disk, no network ever.

## 15. Test obligations for Step 2

Each is a named guarantee; the conformance statement in the README will say how many are implemented, in
the deadman style ("N of M", never a rounded-up "all").

> ### CONVENTION, added 2026-09-02 after G8 — **EFFECT, NEVER INTENT**
>
> **A guarantee written in terms of the trader's INTENT is unimplementable by construction, and no
> amount of care at implementation time can rescue it. Only guarantees written in terms of the EFFECT
> ON THE POSITION are implementable.**
>
> The guardian **can never know what the trader meant.** It observes an order and a position; it does
> not observe a purpose. A bot knows its own intent — `nt/bots/DeadmanBotA.cs` classifies its orders
> with `opening = action == Buy || action == SellShort` and is right, **because it wrote them.** The
> same line applied to a stranger's order is wrong: a trader who is short and exits through the DOM
> sends `Buy`, and that rule calls their exit an entry and cancels it. **That is not a hypothetical;
> it is the 2026-08-26 incident, arrived at from the other side.**
>
> | ❌ intent — unimplementable | ✅ effect — implementable |
> |---|---|
> | *"new orders are cancelled"* | *"orders that INCREASE net exposure are cancelled"* |
> | *"entries are blocked"* | *"an order whose direction matches the sign of the position is refused"* |
> | *"the trader cannot open a position"* | *"net exposure does not increase while locked"* |
>
> **`G8` was not a drafting slip. It was written in the wrong language**, and the wrong language has no
> implementation at any price. Rewriting it is therefore a **change of scope**, not a correction of
> wording — which is why it is marked NOT IMPLEMENTED and left standing (A12).
>
> **The near-miss worth naming, so nobody walks into it:** `GuardianStatus.EntriesAllowed` and the
> phrase *"blocks new entries"* are intent vocabulary and appear throughout this document. They are
> **safe today for one reason only — nothing acts on an order because of them.** The flag reports the
> guardian's own state; it never classifies somebody else's order. **The day anyone makes it act, it
> becomes G8.**
>
> **The safety was conditional in a second way, not seen when the paragraph above was written
> (added 2026-09-03).** The condition is **the absence of a reader.** *"Blocks new entries"* was safe
> here because nothing acts on it — and **publishing it destroys the condition that made it safe.**
> **The same sentence is harmless in a code file and false on a front page.** It was copied out of
> this document onto the public site, where the reader is somebody deciding whether to install and
> cannot read the `if` next to it; for them it is not internal vocabulary but a promise that their
> next order will not go through. It stood in two places and was taken down on 2026-09-03
> (site `51e1d96`). So the near-miss has a second half: not only *the day anyone makes it act* —
> also **the day anyone quotes it where the code is not there to contradict it.**

| # | Guarantee | Shape of the test |
|---|---|---|
| G1 | Config with any missing/unknown/invalid field never arms | table of broken configs, each asserted rejected with its reason |
| G2 | `personalLimit >= firmLimit` never arms | boundary cases including equality |
| G3 | Day P&L includes commissions and matches a hand-computed fixture | fixed execution stream, expected decimal |
| G4 | Losses are summed across accounts, never netted | two accounts, one +$500, one −$700 |
| G5 | Breach at exactly the limit trips (`>=`, not `>`) | P&L stream landing exactly on it |
| G6 | Lockout persists state **before** the first broker call | fake store records call order |
| G7 | Process killed mid-flatten resumes `LOCKED`, not `ARMED` | fake broker throws mid-sequence; new Core instance from the same store |
| G8 **NOT IMPLEMENTED** | Orders after lockout are cancelled and logged | order events while `LOCKED` — **NOT IMPLEMENTED since 2026-08-27 (`a916bba`), see AMENDMENTS A12.** The text of this guarantee is deliberately NOT rewritten to match the code: it describes what a daily-loss guardian ought to do, the gap is real, and rewriting it would delete the gap from view. The three `G8_*` tests assert the OPPOSITE and pass, because A11 removed cancel-on-observation after it cancelled the guardian's own flatten. Conformance is therefore **25 of 26**, computed from this table by `C_ConformanceCountTests` |
| G9 | Hand-edited sealed config is detected and locks out | flip a byte in `configSnapshot`; expect `SEAL_MISMATCH` + `LOCKED` |
| G10 | Hand-edited config file while sealed does not take effect | edited file ignored, `CONFIG_TAMPERED`, sealed values still enforced |
| G11 | Any config change while sealed is rejected and logged | including a stricter one |
| G12 | Seal expires exactly at 17:00 CT, DST-aware, and the zone resolves by **both** paths | 2026-03-09 and 2026-11-02; IANA id and mapped Windows id must yield the same `TimeZoneInfo`; an unmapped id is rejected at config time |
| G13a | Clock jumped **forward** in session ⇒ `CLOCK_ANOMALY` + `FAIL_CLOSED` + **seal maintained** | wall clock jumps past `expiresAtUtc` while `MonotonicMs` barely moves; assert still sealed and entries blocked |
| G13b | Clock wound **backwards** ⇒ logged, seal maintained, entries blocked | with monotonic continuity ⇒ `CLOCK_ANOMALY`; without it (restart) ⇒ `CLOCK_SUSPECT`; both appear in the ledger |
| G13c | Seal expiry in session is measured on the monotonic clock | monotonic reaches `sealDuration` while the wall clock lags ⇒ expires; wall clock reaches it while monotonic lags ⇒ does not |
| G13d | Sleep-like divergence does not pin `FAIL_CLOSED` forever | large one-off divergence, then normal ticks and a valid P&L ⇒ `FAIL_CLOSED_CLEARED` |
| G14 | P&L sources disagreeing ⇒ `FAIL_CLOSED`, no tie-break | platform vs core mismatch |
| G15 | Missing price for an open position ⇒ `FAIL_CLOSED`, never zero | feed returns no quote |
| G16 | Unknown/disconnected account ⇒ `FAIL_CLOSED` | account vanishes from the feed |
| G17 | Ledger chain verifies; any edited line is found by `seq` | tamper each field in turn |
| G18 | Unwritable ledger ⇒ `FAIL_CLOSED` | store throws on append |
| G19 | Torn/corrupt state file ⇒ `FAIL_CLOSED`, never `DISARMED` | truncated JSON, unknown schemaVersion |
| G20 | Lockout has no manual exit before expiry | every public entry point tried while `LOCKED` |
| G21 | Money never touches `double` in Core | assertion over the public surface |
| G22 | Core references no NinjaTrader assembly | build/reflection assertion in the test suite |
| G23 | A missing, zero or negative point value is an unknown that blocks entries, and is never defaulted to 1 (§5.7) | table of non-positive values, each rejected at the book with a reason and surfaced as `INVALID_POINT_VALUE`; a broken account contributes **no** figure to the day loss; at the guardian level, entries are blocked and `PNL_UNCOMPUTABLE` is logged. The decisive fixture makes the two readings straddle the limit — 120 points on 1 MES at $5.00 is exactly the $600.00 limit, at a substituted 1.0 it is $120.00 — and asserts no checkpoint ever carried the substituted figure and no breach was decided on it. Plus two controls, so the guarantee cannot be satisfied by blocking everything: a usable point value clears the unknown, and produces the real money figure |

## 16. Versioning

`SPEC.md` carries a version; `schemaVersion` lives independently in config, state/seal, and ledger entries.
An unknown version of anything is an unknown, and unknowns fail closed (§10). This document is amended by
commit, never in place: what the spec said when the code was written stays readable in `git log`.

## 17. What this does not protect against

Published here, and repeated in the README when the repository opens, for the same reason deadman ships the
two tests that document its own limits: a guarantee is only worth what its stated exceptions are worth. A
product in this category that publishes no limits is either not thinking or not telling.

### 17.1 Deleting the add-on before the session

A trader who closes NT8, removes `deadman-guardian` from the AddOns folder (or the compiled assembly), and
reopens the platform is not protected, and nothing in this design can prevent that. NT8 loads add-ons from
disk at startup; whatever is not there cannot run.

What remains: the ledger is append-only and hash-chained, so the *absence* is visible. A day with a
`GUARDIAN_STOPPED` and no `GUARDIAN_STARTED`, or a gap where a trading day has no `DAY_OPENED`, is a hole in
the record with a shape. **Premeditation wins; it just does not get to look like an accident.** That is the
honest claim, and it is the whole claim: this tool converts an impulse into a decision that must be taken
cold, in advance, and that leaves a mark.

### 17.2 Manipulating the system clock

Partially defended, and the boundary is exact:

- **In session** (the add-on has been running since arming): defended. Expiry is measured on a monotonic
  counter the trader cannot set, wall/monotonic divergence is detected, and the seal is maintained in every
  ambiguous case (§7.5). Moving the clock forward blocks trading rather than unlocking it.
- **Across a restart**: **not defended**. Monotonic continuity dies with the process. Someone who closes
  NT8, sets the clock past `expiresAtUtc`, and reopens it gets a released seal, because Core has no evidence
  that time did not pass.
- What remains, again, is the trace: every backward observation is logged (`CLOCK_SUSPECT`), and correcting
  the clock afterwards leaves a non-monotonic `tsUtc` sequence inside a hash-chained file that cannot be
  quietly repaired.

Closing this gap properly requires a time source outside the machine, and v1 opens no sockets (§13). It is
listed as a v2 question, not as a solved problem.

### 17.3 Slippage and gaps

Covered in full in §2 and repeated here because it is the limit most likely to be discovered on a bad day
rather than read in advance: the guard removes discretion and exposure; it does not bound the number. A
market that gaps through the limit between the breach and the fill produces a loss larger than the limit,
and no add-on running inside a trading platform can prevent that.

### 17.4 Everything else outside its sight

Trading the same firm account from another platform, from a phone app, or from a second NT8 installation.
Firm rules other than the daily loss limit (§13). A user with disk access who deletes the state directory
between sessions — detected on the next start as a missing state, which fails closed, but not prevented.

### 17.5 Firm minimum-hold rules

Some firms require a trade to stay open for a minimum time. Two state it per-trade, and their wording
differs in ways that matter:

- **Elite Trader Funding**, inside the clause prohibiting HFT — *"...strategies that leverage technology to
  gain advantages execution speeds by engaging in a high number of transactions in a short amount of time in
  an abusive manner. All trades executed by you on ETF's platform shall have a minimum duration of ten (10)
  seconds—no exceptions."* The stated purpose is anti-HFT; the sentence itself is absolute.
- **Top One Futures**, as its own numbered rule *"Minimum Trade Duration (10-Second Rule)"* — *"All trades
  must remain open longer than 10 seconds."*, *"10.00 seconds = violation, 10.01 seconds = acceptable."*,
  *"Closing any portion of a trade before 10 seconds violates this rule."* It then adds a tolerance:
  *"Occasional minor violations are tolerable"*, escalating only when such trades approach half of total or
  withdrawable profit.

A third shape exists and is **not** the same thing: MyFundedFutures, Funded Futures Family and Lucid Trading
apply a proportional test over a population of trades — broadly, a majority of trades and of profit must come
from positions held beyond a threshold (10 seconds; Lucid measures at 5). A single early exit cannot breach
those. Only a per-trade rule can be breached by one lockout.

**The gap, stated plainly.** If the trader's personal limit is reached within ten seconds of a position
opening, the lockout sequence (§9) flattens it, and under a per-trade minimum that flatten is itself the
violation. The guard would have caused a breach the trader did not commit. Note Top One's partial-close
clause: a partial flatten breaches too, so there is no smaller action that avoids it. This is a defect of
the *combination*, not of either rule alone, which is why it is written here rather than in a footnote.

**v1 behaviour: flatten anyway, and say so.** The guard does not check position age before flattening.
Reducing exposure outranks a duration rule, for the same reason exits are fail-open everywhere else in this
design: the alternative is holding a position already past the trader's limit and hoping a timer expires
before the loss grows. `LOCKOUT_STARTED` records the age of each position at the moment of the flatten, so
the ledger shows whether a given lockout could have tripped a minimum-hold rule — the least a tool can do
about a limit it has chosen not to enforce.

**How large this actually is.** Smaller than it first looks, and the size should not be overstated to make
the disclosure sound braver. A lockout is a once-a-day event at most, and it fires on a limit the trader set
— so a flatten inside the first ten seconds of a position requires the limit to be hit almost immediately
after entry. Top One explicitly tolerates *"Occasional minor violations are tolerable"*, and ETF's rule sits inside an
anti-HFT clause aimed at *"a high number of transactions"*. Neither of those readings is a permission, and
neither is written down by the firm as applying to risk tools. Treat the exposure as real but rare.

**Compatibility consequence.** Recorded per firm in the compatibility table, not buried here. At Top One
Futures the point is currently moot — they prohibit automated tools outright — and becomes live only if that
prohibition is ever lifted. At ETF it is a limit to disclose in the approval request rather than after it.

**v2 candidate — min-hold awareness, with its difficulty said out loud.** A configurable per-firm minimum
hold, where a breach detected inside the window defers the flatten until the window closes. Listed as a
candidate, not a plan, because the obvious implementation is worse than the problem: **deliberately holding
a losing position to satisfy a duration rule is its own risk**, and a fast market can take more in those
seconds than the violation would have cost. Any v2 shipping this owes a bounded answer to "what if the loss
doubles inside the deferral window", and — harder — must not quietly become a mechanism that keeps a trader
in a trade past their own limit, which is the exact behaviour this product exists to remove. Until both are
answered, flattening immediately is the honest default.

---

*v0.4 — 20 August 2026. Amendments A1-A8 absorbed after Step 2 was approved.
§17.5 added 21 August 2026 (amendment A9), after a second layer of firm research found per-trade
minimum-duration rules at two firms.
v0.3 — 19 August 2026. Written before the code. v0.1 approved with two corrections, both defects rather
than style: the clock defence covered only the harmless direction, and the time-zone rule specified
something that cannot work inside the target runtime.*
