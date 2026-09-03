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

> **Nota 2026-09-02 — el nombre, no la decisión.** La constante se llama hoy
> `Constants.FlattenAttemptsBeforeHuman`, y el campo del evento `needsHuman` en vez de `exhausted`.
> **La decisión de A7 no cambió**: sigue siendo 3, sigue reintentando en el tick, sigue persistida, y
> sigue sin liberar nada. Se renombraron porque `Max…` y `exhausted` **afirmaban un tope que no
> existe** — medido el 2026-08-26: los intentos llegaron a **167**, el flag prendió en el **3**, y el
> guardián siguió **164 veces más**. Este párrafo ya lo decía en prosa; los identificadores decían lo
> contrario, y el ledger viaja más lejos que la prosa.

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

## A10 — user-facing text is single-source, like the hash function

**What the spec did not settle:** nothing about wording. It is about code, and this looked like an
editorial matter until it produced a defect.

**What happened.** The lockout explanation was drafted for the Log. Further down the same document, an
older draft of the same explanation survived for the status window — and it still contained an
overclaim that had been corrected one paragraph above it: a promise that the trader *"cannot trade
again until 17:00"*, which §17 explicitly denies. Two versions of one sentence, in one file, and the
wrong one had outlived its own correction because nobody re-read the paragraph below.

**Decision: the strings a user reads are a single source, consumed by every surface that shows them.**
The Log and the status window take the *same two strings*. Not similar ones, not adapted ones — the
same. A surface that needs different wording is a signal that the wording is wrong, not that it needs
a variant.

**Why, in one line:** it is exactly the rule already applied to `Hashing.Sha256Hex`, where the string
overload delegates to the byte overload so the assembly keeps **one** SHA-256 implementation, because
a second one is a second thing that can drift. Prose drifts faster than code and nothing compiles it,
so it needs the rule *more*, not less. The version that drifts will be the one a user reads on the
worst day they will have with this product.

**The surfaces that cannot obey it, declared rather than left tacit.** `install.ps1` is PowerShell:
it cannot reference `GuardianCore`, so its copy of any sentence is unavoidable. **A rule that cannot
be obeyed protects nothing** - it only moves the failure somewhere nobody is looking, which is exactly
what happened: wording removed from the status window went on greeting the reader from the installer's
closing text, the first thing anyone installing this reads.

So those surfaces are covered by a **check** instead of the rule. `Messages.Retired` lists wording this
product no longer shows, and a test walks every `.cs` and `.ps1` in the repository and goes red if one
survives. Documentation is exempt on purpose - SPEC, this file and the READMEs *discuss* retired
wording, and forbidding that would delete the record of the correction.

Retiring a phrase now means adding it to that list. The check found two live instances in
`install.ps1` on its very first run, one of them still being printed to a user.

**How it fails if ignored:** silently, and asymmetrically. Two copies do not diverge all at once — one
gets corrected and the other does not, so the surviving copy is by construction the *stale* one. The
next person who wants to "adapt the message a little for the window" reintroduces the defect without
knowing, which is why this is written as a rule rather than left as an anecdote about one commit.

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
| A10 single-source user-facing text | **pending** — raised after v0.4, alongside the lockout message work | yes: the failure mode is that the *stale* copy is the survivor |
| A11 post-lockout enforcement has two branches | **pending** — closes the "Unverified until Step 3" clause of §3.3 and amends §9.5 | yes: the branch without selective cancellation is permanent, not interim |

*Written during Step 2, 19 August 2026, against SPEC v0.3. Absorbed into SPEC v0.4 on 20 August 2026.*

---

## A11 — post-lockout enforcement has two branches, and cancelling on observation is not one of them

**2026-08-26. Closes the "Unverified until Step 3" clause of §3.3, and amends §9.5.**

**What changed.** `OnOrderObserved` no longer cancels anything. The blind account-wide sweep moved out of
`RunLockoutSteps` — which re-enters on every tick until the flatten verifies — into `EnterLockout`, where it
runs once. "Once" is now a property of where the code lives rather than a rule someone has to remember to
check: a flag gets forgotten, a call site does not.

**Why.** The live test of 2026-08-26 ran the production breach path on real fills for the first time. The
breach fired exactly on the limit, and the flatten never completed: the guardian observed its OWN flatten
order through `OrderUpdate`, saw itself `LOCKED`, and cancelled it **1 ms after the venue accepted it**. 167
loops, `FLATTEN_VERIFIED` zero, position never closed — and twelve `ORDER_REJECTED_LOCKED` that were `Sell`,
`SellShort` and `BuyToCover`: the trader's own exits. Full trace in `docs/live-test-findings-20260826.md`.

§3.3 had anticipated two outcomes and got a third. **The spec asked to be verified, and the verification
came back with an answer that was not among the ones it expected: the backstop was the weapon.** This is the
closing of a clause the spec itself left open, not the loosening of a promise.

**The capability the port lacks.** `IBrokerActions` offers `CancelAllOrders(account)` and nothing finer.
There is no way to cancel ONE order, so "cancel only what increases exposure" cannot be expressed at all.
This amendment is bounded by a missing capability, not by a missing decision.

**Restoration condition.** When the port can cancel a single order by id — planned as an optional interface
the adapter may implement and the guardian probes with `as`, so adding it breaks no already-compiled adapter
in the window between deploying a new DLL and the F5 that recompiles it — orders that INCREASE exposure are
cancelled on observation again. Orders that reduce it, and the guardian's own flatten orders, stay
untouchable.

**What does not come back, ever.** Blind account-wide cancellation on observation. No capability makes it
safe: it is the mechanism that trapped a trader in a position.

**Why the branch without selective cancellation is permanent, not interim.** Wherever that capability is
absent — an older adapter, a test double, another adapter entirely — the guardian must do exactly what it
does today. The minimum is the foundation the complete option stands on, not a detour around it.

**The doctrine this belongs to**, and the half whose absence caused it:

| | |
|---|---|
| on WORDS | every message asserts exactly what its own code established |
| on ACTS | the guardian never acts on the account on a premise it could not verify |

Cancelling is **acting** on the account, not refusing to act, so the fail-closed instinct never reached it.
And the worst cases are not symmetric, which is the whole argument: cancelling wrongly means the trader
cannot exit a sinking position — unbounded loss, caused by the guardian — while not cancelling wrongly means
one order opens exposure and the next cycle's flatten closes it, bounded by one cycle.

**How it fails if ignored:** silently, and in the direction that costs money.

---

### Nota de corrección — 2026-09-01. El texto de arriba NO se modificó.

Esta enmienda está fechada y es un documento de firma, así que se le anota lo que le falta en lugar de
reescribirla. **Dos cosas, y la segunda es la que importa.**

**1 · Desde esta decisión, todo cuelga de UN SOLO freno, y eso no está dicho acá.** A11 nombra las dos
ramas —el barrido previene un FILL, el aplanado DESHACE uno— y elige correctamente entre ellas. Lo que
no dice es que, al quitar la primera, **la que queda queda sola**. Los `LOCKOUT_INCOMPLETE` eran una
molestia mientras había un segundo freno detrás; **desde el 2026-08-26 son el modo de falla del
producto**. Quitar un freno redundante **asciende en silencio los defectos del que queda**, y ese
ascenso no dejó rastro en ningún commit ni en ninguna enmienda — hasta esta nota.

**2 · El argumento de cierre de esta enmienda tenía n=1 cuando se escribió, y sigue teniendo n=1 hoy.**
La última línea de arriba dice, textual:

> *"not cancelling wrongly means one order opens exposure and **the next cycle's flatten closes it,
> bounded by one cycle**."*

Medido el 2026-09-01 sobre el ledger de producción (8.034 entradas, 2026-08-21 → 2026-09-01):

| | |
|---|---|
| `FLATTEN_VERIFIED` en toda la vida del producto | **1** (seq 8002, 2026-08-31T14:10:30Z) |
| episodios que terminaron **sin** cerrar la posición | **1** (2026-08-26: 167 intentos, `exhausted: true`) |

**La cota no está demostrada: está afirmada.** Y el 26-ago no se degradó — **se agotó**.

Corresponde el matiz, y no achica el hallazgo: el fallo del 26-ago **es el que esta misma enmienda
arregla**, así que no es evidencia contra el código de hoy. Es evidencia de que **el mecanismo del que
el producto ahora depende por completo tiene una sola observación exitosa.**

> **Que esta casa firme decisiones en un documento cuyo argumento final es una afirmación con una
> observación es el hallazgo, y va escrito acá porque acá es donde se va a hacer la pregunta.**

Medición completa: `docs/aplanado-freno-unico-20260901.md`. **Ninguna decisión se cambia con esta
nota**; la enmienda sigue vigente tal como está.
