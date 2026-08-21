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

## 2. Clock sources and suspend — SPEC §6.4 CONFIRMED, §17.2 ANSWERED

- **`Environment.TickCount64` does not exist** in the NT8 runtime. Only `TickCount:Int32`, which wraps
  every 24.9 days. Confirmed by reflection *inside the process*, not only on the bench.
- `Stopwatch.IsHighResolution` is `True`, frequency 10,000,000. It is the monotonic source the adapter uses.
- Awake, the three clocks track each other to within ~15 ms over 26 minutes of sampling.

### The suspend question, measured

Two **real S3 sleeps** happened during the session — genuine, not a blank screen: Windows logged
`Kernel-Power` 42 (entering sleep) and 107 (resumed) for both, with `Firmware S3 times` records, and
NinjaTrader logged them itself.

| # | Windows: sleep → resume | NT8's own log | probe sample interval (nominal 30,000 ms) |
|---|---|---|---|
| 1 | 22:16:58 → 22:17:03 (~5 s) | "entering a suspended state" 22:16:52 → "recovered" 22:17:24 | tick 27→28: **46,120 ms** |
| 2 | 22:25:38 → 22:25:43 (~5 s) | "entering a suspended state" 22:25:36 → "recovered" 22:25:59 | tick 44→45: **45,635 ms** |

**The answer: both monotonic sources keep counting through S3 sleep on this machine.**

| across | `wall − Stopwatch` | `wall − GetTickCount64` |
|---|---|---|
| sleep 1 | −6 ms → −47 ms (moved **41 ms**) | −10 ms → −15 ms |
| sleep 2 | −46 ms → −99 ms (moved **53 ms**) | −17 ms → −7 ms |
| whole 26-minute session | −99 ms cumulative | stayed inside ±18 ms |

Neither source lost the ~5 seconds it slept. Had `Stopwatch` stopped during suspend, `wall − Stopwatch`
would have jumped by about **+5,000 ms**; it moved by 41 and 53 ms, in the *other* direction, and those are
ordinary drift.

**What this changes in the spec.** §7.5 carried a parenthetical that a sleeping machine *stops* the
monotonic counter, so the seal would last longer in wall-clock terms and the divergence would be logged.
That is **wrong on this hardware for short S3 sleeps**, and it has been corrected: sleep neither extends the
seal nor raises a false `CLOCK_ANOMALY`. The observed worst case, **53 ms**, sits about 2,000× below the
`ClockDivergenceToleranceMs` of 120,000 — the tolerance is comfortable, not marginal.

**Limits of this measurement, stated:** two sleeps of about five seconds each, S3, on one machine.
Hibernation (S4) and multi-hour sleeps are **untested**, and a long sleep is exactly where a source that
counts *unbiased* time would diverge. The rule of §7.5 is unaffected either way — the seal is maintained
whenever the clocks disagree — but the number above should not be quoted for S4.

### The consequence nobody specified: the guardian is blind while suspended

The evaluation timer did not fire during either sleep: the interval that should have been 30 s came back at
46 s, so roughly **16 seconds of missed evaluation** per event beyond the nominal cadence. NinjaTrader also
tore down and rebuilt its connections on resume — its log shows `Connection lost` for both the Live and
Simulation providers, a `high latency: 35,968 ms` warning, and reconnection about 40 s after the resume.

During that window the account is disconnected, so Core's own rule (§10) blocks entries and the state is
`FAIL_CLOSED` for the right reason. Nothing needs fixing, but it belongs in writing: **a suspended machine
is an unwatched machine**, and the guard resumes watching a few tens of seconds after the lid opens, not
instantly.

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

## 5. Detect-and-cancel latency — STILL NOT MEASURED, and now the reason is known

Five orders were submitted during the session. **None of them reached Sim101.**

```
Cbi.Account.CreateOrder: account='<funded-acct>'  instrument='MNQ SEP26'  Buy  Market  qty 1   -> Rejected
... five of these, every one on account <funded-acct>, every one Rejected
error=OrderRejected  comment='Your account is not subscribed to the data feed associated with this contract'
```

All five went to account **`<funded-acct>`** over NinjaTrader's **`(Live)`** connection — a different account from
the one this work is scoped to — and the venue rejected every one of them for a missing data subscription on
`MNQ SEP26`. Nothing filled and no position was ever opened.

The probe watches `Sim101` and nothing else, by design, so it correctly recorded zero order events. That is
the guardian's account scoping working as intended on its first contact with reality: it did not observe,
touch, or act on an account it was not told to guard.

**To produce the number**, the order has to be entered *with Sim101 selected as the account* in the order
entry window or SuperDOM — the Simulation connection was up and healthy throughout (`Simulation: Primary
connection=Connected, Price feed=Connected`) — and on an instrument the simulated feed serves. A rejected
order will not do either: rejection happens before the order ever reaches a working state, so there is no
detect-and-cancel to time.

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

## 7. The two manual sessions, in order

Everything above came from one NinjaTrader session driven by hand. It is worth keeping the sequence,
because the first round is what told us where to look in the second.

**Round one, 22:03 → 22:15.** NT8 started normally: the AddOn compiled, loaded, walked
`SetDefaults → Configure → Active`, found `Account.All = [Backtest, Playback101, Sim101]`, subscribed, and
received `AccountItemUpdate` four seconds later carrying `CashValue = 100000` on Sim101. That settled §6 and,
incidentally, proved the subscription taken at `Configure` survives the connection being established — the
instance is not replaced, so the adapter is not left talking to a corpse.

In that round the probe recorded no order and no suspend, and two independent sources agreed with it:
NinjaTrader's own trace and log carried zero order activity, and the Windows System event log carried no
`Kernel-Power` event at all. The clock samples ran unbroken every 30 s with 13 ms of maximum divergence,
which is what an awake machine looks like. So the instrument was fine; the events had not happened yet.

**Round two, 22:16 → 22:29.** Both events happened, and both are in the record:

- Two real S3 sleeps, confirmed by Windows and by NinjaTrader independently, giving the measurement in §2.
- Five orders — none of which reached `Sim101`. They went to account `<funded-acct>` on the `(Live)` connection
  and were all rejected for a missing data subscription (§5).

That second point is the one worth carrying forward, and not only as a missing measurement: the probe was
told to watch `Sim101` and it watched `Sim101`. Orders on another account, on another connection, were
neither observed nor acted upon. Account scoping worked on its first contact with reality, which is exactly
the property a guardian has to have before it is allowed anywhere near a funded account.

**State of the machine, exactly as left:**

- `bin\Custom\AddOns\DeadmanGuardianProbe.cs` — the read-only probe, deployed and currently loaded.
- `bin\Custom\NinjaTrader.Custom.csproj` — one `<Compile>` line added for the probe; the untouched original
  is at [`backups/NinjaTrader.Custom.csproj.bak`](backups/NinjaTrader.Custom.csproj.bak).
- The adapter is **written and compile-checked but NOT installed**: `install.ps1` has not been run, so
  NinjaTrader's next start behaves exactly as it does today.
- NinjaTrader **running**, left alone deliberately so the probe keeps recording.
- No account traded by this work, no order placed by it, no connection configured, no NinjaTrader setting
  changed.

---

## Handoff — one measurement still open

**Place and cancel one order on Sim101**, with the account selector set to `Sim101` (not `<funded-acct>`) and an
instrument the simulated feed serves. Cancel it before it fills. That single order produces the detect-half
latency of §5. The probe only observes; it cannot place one itself.

Everything else Step 3 set out to verify is now measured and written down above.
