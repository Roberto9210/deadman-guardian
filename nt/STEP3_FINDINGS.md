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

## 5. Detect-and-cancel latency — instrumented programmatically, waiting on one compile

Placing the order by hand put it on the wrong account: all five manual orders went to account `<funded-acct>`
on the `(Live)` connection and were rejected for a missing data subscription on `MNQ SEP26`. The probe
watches `Sim101` and only `Sim101`, so it correctly recorded nothing — account scoping working on its
first contact with reality, which is the property this thing must have before it goes near a funded
account.

So the order is now placed **in code**, which removes the account selector from the problem entirely.
[`probe/DeadmanGuardianLatencyProbe.cs`](probe/DeadmanGuardianLatencyProbe.cs) is a one-shot AddOn whose
limits are enforced by the code rather than by intention:

- the account must be named exactly `Sim101` **and** report `Provider == Simulator`; any other value
  aborts before anything is sent. `Account.All` and the provider of every account are logged either way.
- the connection must already be `Connected`. The probe never connects anything and says what is missing
  if it is not.
- **one order, ever**: it runs only if a gate file exists, and deletes that file *before* submitting, so
  neither a crash nor a restart can produce a second one.
- **limit orders only** — the word `OrderType.Market` does not appear anywhere in the file — 1 contract,
  `TimeInForce.Day`, priced at a tenth of the market and re-checked to be below half of it before
  sending, so it cannot fill.
- it watches the order to a working state, cancels it immediately, and times four legs:

| leg | what it covers |
|---|---|
| submit → working observed | our call out, the venue's accept, NT8 raising the event |
| **working observed → cancel issued** | **the guardian's own reaction — the only part this design controls, and the number §9.5 is about** |
| cancel issued → cancelled confirmed | the venue's round trip back |
| submit → cancelled confirmed | the whole cycle |

It is deployed, listed in the csproj, compile-checked against the real NinjaTrader assemblies, and armed.
It needs one compile to run — see §6.

## 6. Deployment mechanics — CORRECTED: NinjaTrader does not compile on startup

An earlier version of this document concluded that listing the file in the csproj was what mattered.
That was built on five runs whose one consistent variable I could not isolate, and further testing
showed the conclusion was incomplete. What is actually true:

**NinjaTrader compiles NinjaScript on demand, from the NinjaScript Editor — `F5` — not at startup.**
The platform's own trace names the binding directly: `NinjaScriptEditorHotKeys: … Compile='F5'`.

Three experiments settled it, after the file and its `<Compile>` entry were both in place:

| experiment | result |
|---|---|
| restart NT8 normally, wait 4 minutes | `NinjaTrader.Custom.dll` untouched (still the 21:38 build), new AddOn absent, **no compile attempted** — nothing about compilation appears in the log at all |
| delete `NinjaTrader.Custom.dll` and restart | NT8 **restored a stock copy dated 10 August**, 1,283,072 bytes, rather than building one. The previously working probe disappeared with it, confirming the restored assembly is the shipped default |
| build `NinjaTrader.Custom.csproj` from the command line | impossible as written: its three `<ProjectReference>` targets (`NinjaTrader.Core`, `NinjaTrader.Gui`, `Infralution.Localization.Wpf`) **do not exist on disk**. The editor substitutes the loaded assemblies at compile time, in process |

So the install procedure is: **copy the files, add the `<Compile>` entries, then compile once from the
NinjaScript Editor.** [`install.ps1`](install.ps1) does the first two and now says so for the third; the
compile is a human action by construction, and no amount of scripting removes it.

This also explains run 1 of the earlier table, which had looked inexplicable: that session compiled
because something triggered a compile in it, not because a restart is enough. Restarts are not enough.

**A note on what this cost.** Forcing the issue by deleting the compiled assembly left the platform on a
stock DLL for a few minutes, with the probe gone. The build that had been running was backed up first and
restored immediately afterwards, and NinjaTrader is running on it again. Nothing was lost, but the lesson
is worth keeping: on someone else's trading platform, the artifact you delete to "force a rebuild" may be
the one the vendor simply hands back.

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

## Handoff — one keystroke

**Compile NinjaScript once**: in NinjaTrader, `New → NinjaScript Editor`, then **`F5`**.

That builds the two new files into `NinjaTrader.Custom.dll`. The latency probe is already armed — its gate
file is in place — so it fires roughly thirty seconds later, or on the next start, and writes
`Documents\NinjaTrader 8\deadman-guardian-probe\latency_report.md`. It verifies `Sim101` is the simulator
before sending anything, sends exactly one unfillable limit order, cancels it, and consumes its own gate so
it can never run twice.

If the editor reports a compile error, nothing is lost: run `install.ps1 -Uninstall`, or delete the two
files from `bin\Custom\AddOns\` and restore `NinjaTrader.Custom.csproj` from the backup beside it.

Everything else Step 3 set out to verify is measured and written down above.
