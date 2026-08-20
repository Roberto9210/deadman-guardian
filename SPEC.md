# deadman-guardian — SPEC v0.3

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
| T7 | Orders placed after lockout from a DOM, chart, or a running strategy | Enforcement is continuous, not a one-shot flatten (§9.5) |
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

**Unverified until Step 3:** whether an AddOn can veto an order *before* it reaches the broker. NT8 exposes
no documented pre-submit hook to third-party AddOns, so §9.5 specifies enforcement as
*detect-and-cancel-immediately*, not *prevent*. If a pre-submit hook is found, it is an addition, not a
replacement — and the cancel path stays as the backstop.

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

### 5.3 Sources

- **Primary (authoritative): executions.** Core accumulates from `Execution` objects —
  `Price`, `Quantity`, `MarketPosition`, `Commission`, `Time`, `Instrument` *(all verified members of
  `NinjaTrader.Cbi.Execution`)*. Commission is included at the moment it is reported; it is never estimated.
- **Cross-check: the platform's own figures.** `Account.Get(AccountItem.RealizedProfitLoss, currency)` and
  `Account.Get(AccountItem.UnrealizedProfitLoss, currency)` *(verified enum members)*.

### 5.4 Disagreement is an unknown, not a tie-break

If `|coreRealized − platformRealized| > pnlToleranceUsd`, the add-on does **not** pick the friendlier
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

## 6. Persistence

Three files, all local, all under paths given in the config:

| File | Content | Written |
|---|---|---|
| `state.json` | current state, day key, seal, lockout flag, last-seen clock, P&L checkpoint | atomically, before any broker action |
| `ledger.jsonl` | append-only hash chain (§11) | append + flush + fsync before the action it describes |
| `config.json` | the user's configuration (§4) | by the user; read-only to the add-on |

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

A consequence worth stating: a machine that sleeps or hibernates stops the monotonic counter, so the seal
lasts *longer* in wall-clock terms than it otherwise would. That is the safe direction, and it is left as
is. The wall/monotonic divergence it produces is logged, and clears like any other unknown (§10) once P&L is
re-verified — it must not pin the guard in `FAIL_CLOSED` forever.

*(Verification pending in Step 3: whether the monotonic source advances across sleep on the target machine.
The rule above is correct either way; only the size of the logged divergence changes.)*

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

## 9. The lockout sequence

Ordered, idempotent, and resumable — it must survive being killed at any point.

1. **Persist first.** Write `LOCKED` to `state.json` and append `LIMIT_BREACHED` to the ledger **before any
   broker call**. If the process dies here, the restart reads `LOCKED` and resumes at step 2. The reverse
   order (act, then record) would let a crash mid-flatten come back as "armed and fine".
2. **Cancel** all working orders on every guarded account (`Account.Cancel` / `CancelAllOrders`); log
   `ORDERS_CANCELLED` with the count.
3. **Flatten** every guarded account (`Account.Flatten` / `FlattenEverything`); log `FLATTEN_REQUESTED`.
4. **Verify**, do not assume: re-read positions and working orders. Flat and empty ⇒ `FLATTEN_VERIFIED`.
   Not flat after *N* attempts with backoff ⇒ `LOCKOUT_INCOMPLETE` (loud UI, stays `LOCKED`, keeps
   retrying). The add-on never reports success it has not observed.
5. **Keep enforcing.** While `LOCKED`, every new order on a guarded account is cancelled on sight and logged
   `ORDER_REJECTED_LOCKED`. A single flatten is not a lockout; the DOM, a chart, and a running strategy can
   all still submit.

Steps 2–4 are idempotent: running them twice is harmless, which is what makes resumption safe.

## 10. Unknown state ⇒ fail-closed

Any of these means Core does not know the truth, and therefore blocks entries:

- P&L not computable (no market data for an open position, §5.5)
- P&L sources disagree beyond tolerance (§5.4)
- a guarded account is absent from `Account.All`, disconnected, or its denomination changed
- clock anomaly (§6.4)
- ledger not writable or chain verification fails (§11.5)
- state or seal unreadable, or of an unknown schema version (§6.3)

`FAIL_CLOSED` is not a lockout: it clears by itself the moment the unknown resolves — but it clears
*through* a re-computation, never by assumption. It is logged on entry and on exit, with the reason, so
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
  news-trading windows. Those are v2 candidates and must not be implied anywhere in the UI.
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
                            void WriteAtomic(string p, string contents); void AppendLine(string p, string line); }
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

| # | Guarantee | Shape of the test |
|---|---|---|
| G1 | Config with any missing/unknown/invalid field never arms | table of broken configs, each asserted rejected with its reason |
| G2 | `personalLimit >= firmLimit` never arms | boundary cases including equality |
| G3 | Day P&L includes commissions and matches a hand-computed fixture | fixed execution stream, expected decimal |
| G4 | Losses are summed across accounts, never netted | two accounts, one +$500, one −$700 |
| G5 | Breach at exactly the limit trips (`>=`, not `>`) | P&L stream landing exactly on it |
| G6 | Lockout persists state **before** the first broker call | fake store records call order |
| G7 | Process killed mid-flatten resumes `LOCKED`, not `ARMED` | fake broker throws mid-sequence; new Core instance from the same store |
| G8 | Orders after lockout are cancelled and logged | order events while `LOCKED` |
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

---

*v0.3 — 19 August 2026. Written before the code. v0.1 approved with two corrections, both defects rather
than style: the clock defence covered only the harmless direction, and the time-zone rule specified
something that cannot work inside the target runtime.*
