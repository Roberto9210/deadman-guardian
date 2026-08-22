# The two test bots — A, the disaster; B, the prudent one

**Neither of these is a strategy, and neither is trying to make money.** They are instruments for
measuring the guardian, and each one measures the opposite thing.

|  | Bot A — *el desastroso* | Bot B — *el prudente* |
|---|---|---|
| purpose | provoke the guardian | be ignored by the guardian |
| how it trades | churns market in / market out; loses the spread every round trip, on purpose | one contract, fixed cadence, real stop at the venue, symmetric target |
| the claim it tests | a lockout is a standing state, not one flatten | no false positives: an armed guardian does not interfere with a session that behaves |
| what it produces | when the guardian fired, what it did to each post-lockout order, and the ledger | clean sessions a certificate can count |
| ends the day | locked out, flat, by design | self-stopped or at the session boundary, flat, guardian still `ARMED` |

## Why these exist at all

`nt/soak/SoakSandbox.cs` says it in its own header:

> *"P&L is SYNTHETIC. Making a simulated account lose exactly $600 needs fillable orders, and fillable
> orders are what this suite refuses to send."*

The soak's 6/6 proves that **GuardianCore's rules are right** over injected `ExecutionRecord`s. It has
never proved that the guardian fires on **NinjaTrader's own accounting of real fills**, nor exercised
the SPEC §5.4 cross-check between Core's arithmetic and `AccountItem.GrossRealizedProfitLoss` with both
sides real. These two files are the first in the repository allowed to send a fillable order, and that
is the entire reason they are separate, opt-in, and loud about it.

The same hole exists on the clean side. A clean day today is clean *vacuously* — nobody traded. Bot B
makes a clean day mean "the account traded all session and the guardian never had to act".

## What Bot A actually measures, and the number that is not flattering

`nt/addon/DeadmanGuardianAddOn.cs` records that NT8 offers **no pre-submit veto** — established by a
runtime scan of 2,912 types, zero candidate events — so enforcement is **detect-and-cancel**. That has
a window, and the honest thing is to measure it rather than write around it. So the provocation phase
alternates two probe kinds and reports them in separate columns:

- **a resting LIMIT**, far from the market. The guardian *can* cancel this before it fills. Reported:
  how many were cancelled, and the submit→cancelled latency.
- **a MARKET order**, which on a simulator fills at once. The guardian *cannot* stop it. Reported: how
  many filled anyway, how long until the position was flattened, and whether the guardian stayed
  `LOCKED` and flattened it again on the next attempt.

A market order reaching a fill after the lockout is **not** a guardian failure. It is the documented
consequence of detect-and-cancel, and pretending otherwise would make the certificate claim something
false. What is under test is the next column: **the exposure did not survive**, repeatedly, and the
state never left `LOCKED`.

## The rails, all of them refusing in code

Shared in [`BotGuardrails.cs`](BotGuardrails.cs), and stricter than the soak's because the blast radius
is bigger:

- **`Sim101` only.** Exactly one ordinal-name match in `Account.All`, `Provider` **proven** to be
  `Simulator`, and `Connected` — checked before a single order object is constructed. Any failure
  aborts the run. Same shape as `DeadmanGuardianSoak.VerifyAccount`, deliberately not a second dialect.
- **1 contract per order, 1 net contract, a hard per-session order budget.** `SessionBudget` refuses;
  no cap is a comment asking the bot to behave.
- **A gate file per bot**, burned *before* the first send, so a crash or a restart cannot replay a run.
- **A and B never run together.** Not tidiness: when A's guardian locks out it calls the real
  `Flatten` and `CancelAllOrders`, and both are **account-wide** — they do not know who placed what. B
  running at that moment would record an intervention its own guardian never made, forging a false
  positive in the one number B exists to produce. Each bot aborts if the other's gate exists.
- **Automatic shutdown**: at the session boundary (the same `SessionCalendar` the guardian uses, so
  they cannot drift), and on `State.Terminated`. Own orders cancelled, own position flattened.
- **Its own guardian, over its own state and ledger**, with a small limit — production's files are
  never opened for writing. The ports underneath are the **real** ones.
- Orders are filed as `OrderEntry.Automated` through the non-obsolete `CreateOrder` overload: a bot
  filing its orders as `Manual` would be lying in the platform log these runs are going to cite.

Bot A **does** flatten, unlike the soak's `ScopedNtBroker`, which refuses to. The difference is
deliberate: on this account every open contract is the bot's own, and the one outcome Bot A may never
produce is a position left standing. That is exactly why the account check above is absolute.

## Bot B's three margins and its self-stop

1. **Size** — one contract, capped in code.
2. **A real stop at the venue** — a `StopMarket` order sent to NinjaTrader, not a stop kept in the
   process, because a stop that lives in the bot dies with the bot. The **target** is managed in
   process on purpose: a target that fails to fire costs an opportunity, and an opportunity is not a
   risk event. Risk goes to the venue; convenience stays home.
3. **A self-stop far below the guardian's limit** — `SelfStopUsd` is 30% of the sandbox limit. The
   guardian is never asked to do its job because the bot does its own first. That ordering *is* the
   claim.

A session is **clean** only if all five hold, and they were fixed before the first run so they cannot
be relaxed afterwards: the guardian never left `ARMED`; no intervention event in the ledger; every
cancel was the bot's own; the chain verifies; it shut down holding nothing.

## Market data: without it, none of this happens

**The simulation engine needs fresh `Last` prices to fill anything.** Without updates from a real-time
source it fills nothing, and the order comes back `Rejected` with
`There is no market data available to drive the simulation engine`. No fills means no losses, no losses
means the guardian never reaches its limit, and the whole demonstration does not occur.

The free path, and the one these bots are meant to run on, is NinjaTrader's built-in **Simulated Data
Feed**: Tools > Options > General > Preferences > **Multi-provider** checkbox, then Control Center >
Connections > **Simulated Data Feed** > Connect. No download, no subscription, no account. For Bot A
also turn on Control Center > Tools > Options > Trading > Simulator > **Enforce immediate fills**,
which bypasses the fill-probability model so "it did not fill" stops being a failure mode.

> ### The Simulated Data Feed is useless for validating a real signal
>
> NinjaTrader's own documentation says it plainly: this connection *"is a random internally generated
> market and has **NO correlation to real market data**"*. For Bot A and Bot B that is fine, and for
> Bot A it is arguably better — a bot built to lose by paying the spread cannot be rescued by a real
> trend it never sees.
>
> **But it means no forward test of a real strategy can ever run on it.** In particular the
> turn-of-month candidate in `honest-strategy-search` (`factory/botc_potencia_f4.md`) proposes running
> F4 forward on `Sim101` as slow, clean evidence. On invented prices that evidence is worth exactly
> nothing, and it would look identical to the real thing in every report. Anyone doing that walk-forward
> needs real market data, and needs to say which feed produced it.

Market Replay is **not** an alternative here. Its data is real, but the Playback connection trades the
**`Playback101`** account (`Provider = Playback`), not `Sim101` — `BotSafety.VerifyAccount` aborts
before an order object exists, and the guardian's config does not watch that account either. Widening
the account rail to get market data would be trading the safety property for convenience, which is the
one trade this repository does not make.

## What you will see when it fires — read this BEFORE it happens

The lockout is the one moment this product exists for, and on screen it looks alarming if nobody
warned you. Here is the whole thing, in order.

**1. Your orders get cancelled and your positions get closed.** That is the guardian. Expected.

**2. NinjaTrader switches off every strategy you had running on that account**, and writes this in the
Control Center Log:

```
Category = "Default"     Message = "Disabling NinjaScript strategy"
```

Your strategy flips from **Enabled** to **Disabled** on its own. **This is not an error and nothing is
broken.** NinjaTrader disables any strategy whose position was closed from outside it, deliberately, so
the strategy's idea of its position cannot drift from the account's. The guardian closing your
positions triggers exactly that rule. It is the platform behaving correctly in response to the guardian
behaving correctly — and today neither of them says so, which is why this paragraph exists.

**3. You cannot enter again until the session reset**, 17:00 in the configured time zone. Re-enabling
the strategy will not help: the guardian is still `LOCKED` and will cancel and flatten again. That
repetition is the point — a flatten is one action, a lockout is a standing state.

**What would be a real problem**, as opposed to the above: the guardian NOT locking after the limit was
breached, a position still open minutes after the lockout, or the state leaving `LOCKED` before the
session reset. Those are failures. A strategy switching itself off is not.

## Running them

Both bots compile as NinjaScript AddOns. `dotnet build` of the repo does **not** cover them — they
reference NinjaTrader assemblies — so they are checked against the real
`NinjaTrader.Core` / `NinjaTrader.Gui` / `GuardianCore` DLLs with a throwaway `net48` project before
deployment. Last check: **0 errors, 0 warnings**.

```
# 1. NinjaTrader must be CLOSED - install.ps1 refuses while it runs, the files are locked
.\nt\install.ps1 -WithBots

# 2. open NinjaTrader, then New > NinjaScript Editor > F5.
#    NT8 compiles NinjaScript on demand; a restart does not compile (STEP3_FINDINGS.md section 6)

# 3. arm the run you want - ONE of the two, never both:
#    a bot with no gate file does nothing at all
New-Item -ItemType File "$env:USERPROFILE\Documents\NinjaTrader 8\deadman-guardian-bots\botA.GO"

# 4. restart NinjaTrader. Configure sees the gate, the bot starts 45s later and burns the gate
```

Reports land in `Documents\NinjaTrader 8\deadman-guardian-bots\BOTA_REPORT.md` and `BOTB_REPORT.md`,
appended one dated section per run, earlier runs never rewritten — the soak report's rule. Each run's
sandbox state and ledger stay in `deadman-guardian-bots\runs\bot<A|B>-<timestamp>\`.

To remove everything: `.\nt\install.ps1 -Uninstall`.
