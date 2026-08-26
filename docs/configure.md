# Configuring it

Everything here is about two numbers and one decision. The rest of the file is plumbing.

---

## The two numbers

Open `Documents\NinjaTrader 8\deadman-guardian\config.example.json`, save it next to itself as
**`config.json`**, and fill in these two:

```jsonc
"firmDailyLossLimit":     "1000.00",   // the number your firm fails you at
"personalDailyLossLimit":  "600.00",   // the number YOU stop at. Must be smaller.
```

**The firm limit** is whatever your funding agreement says. Look it up; do not remember it. If your firm
publishes a daily loss limit of $1,000 on your account size, that is the number.

**Your personal limit** is the one that does the work, and it has to be strictly smaller. If they are equal
the guardian refuses to arm, because there is nothing between you and the firm's limit — by the time you
hit yours you have already hit theirs, and the flatten happens after the fact rather than before it.

How much smaller is your call, but the gap is what you are buying. A guardian at $600 under a $1,000 firm
limit leaves you $400 of room for the slippage, the gap, and the trade you did not expect to be filled on.
A guardian at $990 leaves you ten dollars and a good feeling.

There are no defaults for these. A limit somebody else typed is a default, and this tool does not ship one.

---

## The decision

**Arming is a deliberate act, once a day, and it cannot be undone until the session rolls.**

You press **Arm** in the little window. From that moment until **17:00 America/Chicago**:

- the configuration is **sealed**. Every change is rejected — including one that makes the limit *stricter*.
  There is nothing to argue about at 14:30 because there is nothing you can change.
- if your day's loss reaches your personal limit, everything closes and no new entry is accepted for the
  rest of the session.
- every attempt to loosen it is written down.

That is the whole product. If that sounds like too much, it is doing what it is for.

---

## The rest of the file

```jsonc
{
  "schemaVersion": 1,                          // leave it
  "accounts": ["Sim101"],                      // the NinjaTrader account names to watch
  "currency": "UsDollar",                      // must match the account's denomination
  "sessionResetTimeZone": "America/Chicago",   // when your trading day rolls over
  "sessionResetLocalTime": "17:00",            // 17:00 CT is the CME roll most firms use
  "ledgerPath": "…\\deadman-guardian\\ledger.jsonl",
  "statePath":  "…\\deadman-guardian\\state.json",
  "pnlToleranceUsd": "5.00"                    // see below
}
```

**`accounts`** — exactly **one**. A config listing more than one account is refused at arm, with the
reason in the rejection: the platform adapter watches a single account, so a second one would be
guarded only in part — its post-lockout orders never cancelled, an open position invisible until
something is realised. Accepting that config and honouring half of it would be worse than refusing.
The refusal is deliberate and reversible if multi-account support is ever actually built
(2026-08-22, M16 in [error_espejo.md](error_espejo.md)).

The sum-across-accounts rule below still describes Core's internal arithmetic, which handles the
plural on purpose — defence in depth for that future day. Losses are **summed** and never netted: if one account is up $500
and another down $700, your day's loss is $700, not $200. Your firm fails each account on its own number,
so the guardian counts them that way.

**`sessionResetTimeZone`** — only `America/Chicago`, `America/New_York` and `UTC` are supported. It is a
short list on purpose: NinjaTrader's runtime cannot resolve IANA time zone names at all, so the guardian
carries its own small translation table, and a table that guesses would be worse than a table that refuses.
An id outside it is rejected with a message naming what is supported.

**`pnlToleranceUsd`** — the guardian computes your day's P&L from your fills, and separately reads
NinjaTrader's own figure. If the two disagree by more than this, it does **not** pick the friendlier one: it
stops allowing entries and says the accounting is in doubt. Five dollars is a sane starting point. Setting
it large to "stop the noise" defeats the check.

---

## What you will see

| the window says | what it means |
|---|---|
| **NOT PROTECTED** (grey) | disarmed, or no config yet. Nothing is being watched. The reason is printed underneath |
| **ARMED** (green) | watching. You can trade |
| **LOCKED** (red) | your limit was reached. Everything was closed, new orders are cancelled on sight, and there is a countdown to when the seal expires |
| **NOT PROTECTED** (orange) | it does not know something it needs to know — the account went away, the P&L stopped adding up, the clock moved. Entries are blocked until it knows again. The reason is printed |

That last one is the one people misread. Orange is not a crash. It is the guardian refusing to say "fine"
when it cannot tell.

---

## Two things it will not do

**It will not let you CHANGE the limit while armed — in either direction.** Not from the window, not by
editing `config.json`, not by editing the state file, not by restarting NinjaTrader, and not by changing your
system clock.

Read that as written, because the earlier wording said "raise" and that was wrong in the dangerous direction.
The guardian compares the config file's hash against the sealed one; **any** difference is detected. A
*stricter* limit is rejected exactly like a looser one, and a change made while sealed produces
`CONFIG_TAMPERED` and a lockout regardless of which way it moved.

That is deliberate — SPEC §7.2 — because a seal you can edit "just this once, and only downward" is a seal
with a negotiation in it. But it has a consequence the operator has to know in advance:

> **While a seal is in force, `config.json` is not touched for ANY reason — not even to put back a previous
> version.** There is no deliberate disarm: the only way a seal is released is by expiring at your session
> reset time. If you armed with the wrong limit, you live with it until the session ends. Restoring the old
> file before then does not undo anything; it is recorded as tampering, and the record is permanent.

The reason the record says "tampering" rather than "changed" is that the guardian cannot know your intent,
and the safe reading of an edit under seal is the hostile one. That is correct for enforcement and it means
the ledger can carry an accusation against an honest restore. Do the restore after expiry.

**It will not bound your loss.** It bounds your *exposure* and removes your discretion. Between the moment
the limit is reached and the moment the position is actually closed, the market keeps moving: measured on
the simulator, that gap was about 300 milliseconds, almost all of it the venue's and the platform's rather
than the guardian's. A gap or a fast market can take you past your limit anyway. Anyone selling you
otherwise is selling you something else.
