# Step 3 — what was verified inside the real NinjaTrader process

**Date: 2026-08-20. NinjaTrader 8.1.8.2, .NET Framework 4.8.9300, Windows 10.0.22631. Sim101 only.**

The evidence is [`probe/evidence/probe_report.run1.md`](probe/evidence/probe_report.run1.md), written by
[`probe/DeadmanGuardianProbe.cs`](probe/DeadmanGuardianProbe.cs) from **inside** the NinjaTrader process
(pid 300), with its raw trace alongside it. The probe is read-only: it places no orders, cancels nothing,
flattens nothing and opens no socket.

---

## 1. Time zone resolution inside NT8 — SPEC §5.1 CONFIRMED, and it matters

Measured in-process, not in a test runner:

| id | result inside NT8 |
|---|---|
| `America/Chicago` | **TimeZoneNotFoundException** |
| `America/New_York` | **TimeZoneNotFoundException** |
| `UTC` | OK |
| `Central Standard Time` | OK, offset −05:00 at the time of the run |
| `Eastern Standard Time` | OK, offset −04:00 |

So the IANA→Windows map of §5.1 is not defensive programming, it is the only thing that makes the session
boundary work in the product's actual runtime. Without it the configuration would be rejected on every
start, while `dotnet test` stayed green — exactly the failure §5.1 was written to prevent.

DST pinned in-process on the two dates G12 asserts: **2026-03-09 17:00 CT → 22:00Z** (daylight),
**2026-11-02 17:00 CT → 23:00Z** (standard). Identical to the values the test suite pins.

## 2. Clock sources — SPEC §6.4 CONFIRMED

- **`Environment.TickCount64` does not exist** in the NT8 runtime. Only `TickCount:Int32`, which wraps
  every 24.9 days. Confirmed by reflection *inside the process*, not only on the bench.
- `Stopwatch.IsHighResolution` is `True`, frequency 10,000,000. It is the monotonic source the adapter uses.
- Over a 50-second window: wall − Stopwatch = **0 ms**, wall − `GetTickCount64` = **5 ms**. All three agree
  while the machine is awake, which is the baseline the divergence rule of §6.4 measures against.

**Still open — needs a human:** the suspend behaviour of §17.2. Sleeping or hibernating the machine with
NT8 running and reading the next `CLOCK_SAMPLE` rows answers it. §7.5 is correct either way; only the size
of the logged divergence changes.

## 3. AddOn lifecycle — SPEC §3.3 CONFIRMED, with one design consequence

```
02:38:14.337  SetDefaults
02:38:18.990  Configure          <- 4.6 s later
02:38:18.999  Account.All = [Backtest, Playback101, Sim101]
02:38:19.000  found Sim101  connection=null  denomination=UsDollar
02:38:19.001  subscribed to OrderUpdate, ExecutionUpdate, PositionUpdate, AccountItemUpdate
02:38:19.039  Active
02:39:09.272  Terminated
```

The AddOn is loaded once at application level and lives for the whole session, as §3.3 argued.

**The consequence the spec did not anticipate:** at `Configure`, `Sim101` exists and is denominated in
`UsDollar`, but **`Account.Connection` is null** — the connection is established later (the platform log
shows `Cbi.Connection.CreateAccount` for Sim101 several seconds afterwards). The adapter must therefore
treat "account present, not yet connected" as the normal startup state and wait for `AccountStatusUpdate`,
not as the `ACCOUNT_UNKNOWN` unknown of §10. Fail-closed is still correct — entries stay blocked until the
account is connected and P&L is computable — but the *reason* must be right, or every single start would
log a false `ACCOUNT_UNKNOWN`. To be carried into the adapter and, if it changes any rule, into the spec.

## 4. Pre-submit interception — SPEC §3.3 "unverified until Step 3" is now VERIFIED: there is none

Scanned **2,912 types** in `NinjaTrader.Core 8.1.8.2` at runtime for any event that could veto an order
before submission (`Submit*`, `Validat*`, `Approv*`, `Intercept*`, `Before*`). **Result: none.** The same
scan run out-of-process over the assembly file found none either.

Corroborating evidence from the platform's own vocabulary: `OrderState` contains `AcceptedByRisk`, i.e.
risk acceptance is something that happens **at the venue, after submission**. `Account` exposes `Submit`,
`Cancel`, `CancelAllOrders`, `Change`, `Flatten` — and no approval callback.

So §9.5 stands as written: enforcement is **detect-and-cancel**, never prevent, and the README may not
claim otherwise.

## 5. Detect-and-cancel latency — NOT YET MEASURED

Zero order events were observed, because no order was placed on Sim101 while the probe ran. The probe is
instrumented and deployed; the measurement needs one order (see the handoff below). What it will produce:
the *detect* half — NT8's event timestamp to the moment a decision could be taken. The *cancel* round-trip
needs the wired adapter of Stage B, which is allowed to cancel.

## 6. Deployment mechanics — SETTLED

NT8 8.1.8.2 compiles `Documents\NinjaTrader 8\bin\Custom\NinjaTrader.Custom.csproj`: SDK-style,
`net48`, `x64`, WPF on, `LangVersion 13`, `EnableDefaultCompileItems=false` with an explicit
`<Compile Include>` list of 294 files.

Five runs, and the pattern is now clean enough to act on:

| run | csproj entry | outcome |
|---|---|---|
| 1 (automated start) | absent when checked before the compile | AddOn **ran** — unexplained, see below |
| 2 (automated start) | present | ran |
| 3 (automated start) | absent | **did not run** after 302 s |
| 4 (automated start) | present | did not run — NT8 never compiled at all, stuck on its logon window |
| 5 (**manual start by Roberto**) | present | **ran**, clean lifecycle, account events received |

**Procedure: the file must be listed in the csproj.** Two runs with the entry loaded the AddOn; the run
without it did not. Run 4 failed for an unrelated reason now identified (below), and run 1 stays
unexplained — I could not reproduce it and I am not going to invent a mechanism that makes the table
tidy. [`install.ps1`](install.ps1) does exactly this, backs up the project file first, and has an
`-Uninstall` switch that restores it.

**Why the automated runs kept stalling — identified.** NT8's startup window is a **logon screen**, and its
trace carries `LogonControl.LoginInternal.5: error creating demo account: Your account is not subscribed to
the data feed associated with this contract`. A session that cannot click cannot get past it, which is why
run 4 never reached a compile. The reliable path is a human start, as run 5 shows.

## 7. The manual run — what the evidence shows, and what it does not

Roberto reported doing all three handoff tasks. The evidence from the manual session
(`probe/evidence/probe_report.run5_manual.md`, `probe_trace.run5_manual.jsonl`) shows one of them.

**Done — NT8 started normally.** Session began 22:03:15 local. The AddOn compiled, loaded, walked
`SetDefaults → Configure → Active`, found `Account.All = [Backtest, Playback101, Sim101]`, subscribed, and
received `AccountItemUpdate` four seconds later carrying `CashValue = 100000` on Sim101. That settles §6
and, incidentally, proves the subscription taken at `Configure` survives the connection being established:
the instance is not replaced, so the adapter is not left talking to a corpse.

**No trace of the order.** Zero `ORDER_OBSERVED` and zero `EXECUTION_OBSERVED` in the probe — and, from a
source that owes the probe nothing, **zero order activity in NinjaTrader's own trace and log files for the
entire day**. The only lines matching "order" anywhere are hot-key configuration and a login error. Had an
order been submitted and cancelled on Sim101, NT8 would have recorded it whether or not the probe was
listening.

**No trace of the suspend.** The Windows System event log has **no `Kernel-Power` and no
`Power-Troubleshooter` events in the last three hours** — no sleep, no hibernate, no resume. Independently,
the probe's clock samples run unbroken every 30 s from 03:03:47Z to 03:11:17Z with a maximum wall-vs-monotonic
divergence of **13 ms**, which is what an awake machine looks like. A suspend would have left a gap and a
divergence the size of the sleep.

So the latency numbers and the suspend answer are **still missing** — not because the instrument failed but
because the two events never reached it. Both remain instrumented and waiting, and NinjaTrader is running
with the probe loaded right now, so an order placed on Sim101 in the next minutes is captured without
restarting anything.

**State of the machine, exactly as left:**

- `bin\Custom\AddOns\DeadmanGuardianProbe.cs` — the read-only probe, deployed and currently loaded.
- `bin\Custom\NinjaTrader.Custom.csproj` — one `<Compile>` line added for the probe; the untouched original
  is at [`backups/NinjaTrader.Custom.csproj.bak`](backups/NinjaTrader.Custom.csproj.bak).
- The adapter is **written and compile-checked but NOT installed**: `install.ps1` has not been run, so
  NinjaTrader's next start behaves exactly as it does today.
- NinjaTrader **running**, left alone deliberately so the probe keeps recording.
- No account traded, no order placed, no connection configured, no NinjaTrader setting changed.

---

## Handoff — the two that are still open

1. **Place and cancel one order on Sim101**, with NinjaTrader as it is right now. Any instrument, any size;
   cancel it before it fills. That single order produces the detect-half latency of §5. The probe only
   observes; it cannot place one itself.
2. **Sleep or hibernate the machine** with NinjaTrader running, resume, and leave it a minute. The next
   `CLOCK_SAMPLE` rows answer the suspend question of §17.2. Closing the lid or letting the screen blank is
   not enough — it has to be a real S3/S4 sleep, the kind Windows records as a `Kernel-Power` event.

Neither touches a live account.
