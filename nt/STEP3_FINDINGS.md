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

## 5. Detect-and-cancel latency — MEASURED

One resting limit order on `Sim101`, placed by
[`probe/DeadmanGuardianLatencyProbe.cs`](probe/DeadmanGuardianLatencyProbe.cs), watched to a live state and
cancelled. Raw output: [`probe/evidence/latency_report.md`](probe/evidence/latency_report.md).

| leg | what it covers | ms |
|---|---|---|
| submit → live state observed | our call out, the venue's accept, NT8 raising the event | 171.1 |
| **live state observed → cancel submitted** | **the guardian's own reaction** | **14.4** |
| cancel submitted → cancelled confirmed | the venue's round trip back | 130.4 |
| **submit → cancelled confirmed** | **the whole cycle** | **315.9** |

The order sequence, from the probe's own log:

```
55.277  submitting 1 LIMIT buy @ 766.75 on MES SEP26 / Sim101   (market was 7667.5)
55.339  Submitted
55.449  Accepted          <- detected here
55.459  CancelPending
55.462  CancelSubmitted
55.462  cancel issued
55.463  Working
55.593  Cancelled
```

**Read the 14.4 ms precisely.** `CancelPending` and `CancelSubmitted` appear *before* the "cancel issued"
line because `Account.Cancel()` raises them synchronously and re-enters the handler before the timestamp on
the next line runs. So 14.4 ms is "from seeing a live order to the cancel being submitted to the venue",
NinjaTrader's synchronous cancel path included — not a bare decision time, which is a fraction of it. That
is the honest boundary of what this design controls.

Worth noting in the sequence: the order was cancelled while still `Accepted`, and only reached `Working`
*after* the cancel was already in flight. The guardian got there first.

Independently, the read-only probe watched the same seven state transitions and timed the delivery lag from
NinjaTrader's own event stamp to the handler: **0 to 39 ms** (`Initialized` 26.6, `Submitted` 39.2,
`Accepted` 0.0, `Working` 1.0, `Cancelled` 24.0).

### What this number is not

- **It is not the lockout.** This times cancelling one resting order. The §9 sequence is cancel-all, then
  flatten, then verify, across every guarded account, and each of those is its own round trip.
- **It is not a real venue.** `Sim101` runs on NinjaTrader's Simulation connection
  (`TradovateOptions name='Simulation' brand='NinjaTrader'`). The two venue legs — 171 ms out, 130 ms back —
  are the simulator's, and a live venue will differ in both directions.
- **It is one sample**, not a distribution. No p95 is claimed because none was measured.

### What it supports

Even with a 14 ms reaction, the cycle took **316 ms end to end**, and 301 of those milliseconds belong to
legs no add-on can shrink. That is the quantitative version of §2: this bounds exposure and removes
discretion; it does not bound the loss. A market that moves during those 300 ms moves whether or not the
guardian is perfect.

### The safety checks, exercised for real

The probe's verification pass ran against a machine that had a live account connected, and the output shows
why the check exists:

```
Account.All = [Backtest/Simulator, Playback101/Playback, Sim101/Simulator, <funded-acct>/Provider31]
verified Provider=Simulator
SimulatorInitialCash=100000   denomination=UsDollar
connection options=TradovateOptions name='Simulation' brand='NinjaTrader'
verified ConnectionStatus=Connected
buy limit price = 766.75   (market reference 7667.5, tick size 0.25)
gate file deleted before submitting
```

The funded account `<funded-acct>` reports `Provider31`, not `Simulator`. Had the name matched, the provider check
would have aborted before anything was sent. The gate file was consumed before the order left, so the probe
cannot run a second time, and the order was priced at a tenth of the market so it could not fill.

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

## 8. Install attempt #1 — failed to compile, reverted, cause found

**2026-08-20 23:12–23:23. The platform is back exactly as it was; nothing was lost.**

`install.ps1` ran cleanly: two sources copied into `AddOns`, `GuardianCore.dll` copied into `bin\Custom`,
296 → 298 `<Compile>` entries, and the `<Reference Include="GuardianCore">` added. NinjaTrader started and
its log said

```
Vendor assembly 'GuardianCore' version='1.0.0.0' loaded.
```

so the reference resolved **at runtime**. Then `F5` in the NinjaScript Editor produced nothing: four compile
attempts at 23:16:12, 23:19:25, 23:19:38 and 23:19:47, each leaving a **0-byte** DLL in
`Documents\NinjaTrader 8\tmp\` beside a fully written `.xml`. `NinjaTrader.Custom.dll` never changed.
NinjaTrader keeps compile errors in the editor's error pane and writes none of them to disk, so the text was
not recoverable afterwards.

### The cause, established without the error text

| evidence | |
|---|---|
| `GuardianCore.dll` references | `netstandard, Version=2.0.0.0` — it was built as `netstandard2.0` |
| `netstandard.dll` facade present in `NinjaTrader 8\bin`? | **no** |
| …in `bin\Custom`? | **no** |
| …in the .NET Framework 4.8 reference-assembly Facades folder? | **no** |
| …anywhere? | only under `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`, which is a runtime path, not a compile reference set |

A netstandard2.0 assembly **loads** fine on .NET Framework, because the CLR resolves the facade at runtime —
which is exactly what the log line above shows. **Compiling against it** is a different resolution path, and
NinjaTrader's in-process Roslyn compile has no `netstandard` reference in its set. The classic symptom is
`CS0012: the type '…' is defined in an assembly that is not referenced`, and the same failure was reproduced
locally on this machine: a plain `ReflectionOnlyLoadFrom` of the assembly threw
*"Cannot resolve dependency to assembly 'netstandard, Version=2.0.0.0' because it has not been preloaded."*

This is the same family of mistake as the IANA time zone one in §1 — code that is correct against a modern
toolchain and wrong inside the target runtime, and invisible until it runs there. `dotnet build` and
`dotnet test` were green throughout, and so was a compile against the real NinjaTrader assemblies, because
the SDK supplies the facade automatically and NinjaTrader does not.

### The fix, prepared and NOT yet retried

`GuardianCore` now multi-targets **`netstandard2.0;net48`**. The test suite keeps consuming
`netstandard2.0`; NinjaTrader gets the `net48` build, whose entire reference list is:

```
mscorlib      v4.0.0.0
System.Core   v4.0.0.0
```

No `netstandard`, so nothing needs a facade — and still zero NinjaTrader references, so G22 holds. 137 tests
green on the multi-targeted build. `install.ps1` now copies the `net48` output and says why in a comment.

The retry is not automatic: it waits for Roberto, as it should.

### What the revert restored, verified

| | before install | after `install.ps1 -Uninstall` |
|---|---|---|
| csproj `<Compile>` entries | 296 | **296** |
| deadman entries in csproj | probe only | **probe only** |
| `AddOns\` | `DeadmanGuardianProbe.cs` | **`DeadmanGuardianProbe.cs`** |
| `GuardianCore.dll` in `bin\Custom` | absent | **absent** |
| `NinjaTrader.Custom.dll` | 23:03:24, 1,312,768 bytes | **23:03:24, 1,312,768 bytes** |

Byte-identical to the `.preinstall` backup. The failed compile never replaced the working assembly, which is
the one genuinely reassuring thing about how NinjaTrader handles this: a broken NinjaScript build leaves the
previous one running rather than taking the platform down.

## 9. Install attempt #2 — INSTALLED AND RUNNING

**2026-08-21 00:15. The adapter compiles, loads, and behaves as specified.**

### What made it work

Attempt #1 died on `CS0246`. Two separate causes, found in order:

1. **`netstandard2.0` cannot be compiled against inside NinjaTrader** (§8). Fixed by multi-targeting
   `GuardianCore` to `netstandard2.0;net48` and shipping the `net48` build, whose whole reference list is
   `mscorlib` and `System.Core`.
2. **NinjaTrader ignores a `<Reference>` you add to its csproj by hand.** The installer's entry was
   well-formed XML in its own `<ItemGroup>` with a *relative* `HintPath`, and the compiler never saw it.
   The entry NinjaTrader's own **References dialog** writes is different in two ways:

   | | hand-added (ignored) | written by the dialog (works) |
   |---|---|---|
   | `<ItemGroup>` | a new one appended at the end | the existing one that already holds `NinjaTrader.Vendor`, `WindowsBase`, … |
   | `HintPath` | `GuardianCore.dll` | `C:\Users\…\NinjaTrader 8\bin\Custom\GuardianCore.dll` — **absolute** |

   That is now the documented install step, and `install.ps1` writes the reference in exactly that shape so
   the dialog is no longer required.

### Verified, with evidence

| check | result |
|---|---|
| compile | `NinjaTrader.Custom.dll` 1,312,768 → **1,317,888 bytes**, 00:15:04 |
| AddOn loads | `adapter.log`: `boot` → `Core started; state=Disarmed` → `subscribed to Sim101` |
| ledger | `GUARDIAN_STARTED`, `seq 1`, `prev: genesis`, payload `{"fresh":true,"state":"DISARMED"}` |
| chain | **verified by an independent Python re-implementation** of the canonical hashing of §11.2 — not by the C# that wrote it. The format is reproducible by a third party, which is the entire point |
| state | `state.json` `DISARMED`, `runId` set, `lastSeenUtc` advancing — the evaluation loop is alive |
| window | **NOT PROTECTED**, and the reason is the right one: *"Disarmed. Nothing is being watched. Config: …\deadman-guardian\config.json"*. No config was installed on purpose (§4 forbids defaults), so refusing to arm is correct behaviour, not a fault. Screenshot: [`probe/evidence/status_window_not_protected.png`](probe/evidence/status_window_not_protected.png) |

### Two defects the first real run exposed, both fixed

**A boot race.** `adapter.log` opened with:

```
boot; home=...
subscribed to Sim101
Tick: Object reference not set to an instance of an object.
subscribed to Sim101
Tick: Object reference not set to an instance of an object.
Core started; state=Disarmed
```

`Account.AccountStatusUpdate` is a **static** event and NinjaTrader fires it immediately, so the handler ran
twice against a still-null guardian while `Boot()` was constructing. Caught and harmless in effect, but it
must not ship: an exception on every startup, and a window in which account events arrive and are dropped.
The registration moved to after `Start()`, and both the handler and the tick are inert until Core exists.
**Verified fixed in the platform** — every boot from 05:17:31Z onward is clean:

```
boot; home=...
Core started; state=Disarmed
subscribed to Sim101
```

**A clipped button.** Measured from the screenshot rather than eyeballed: the window is 190 px tall and the
Arm button's top edge is at y=182, leaving about 8 visible pixels of the one control the trader has to
press. The detail line wraps to a variable number of lines because the config path differs per machine, so
no fixed height is right everywhere. The window now sizes to content. *(Deployed as source; it needs one
compile to take effect, and until a config exists there is nothing to arm anyway.)*

While measuring that I nearly reported a second defect that does not exist: the text looked orange-on-grey
in the capture. Sampling the pixels gave background `#424242` and text `#FFFFFF` — exactly what the code
sets. The tint was antialiasing in a small image. Worth recording as a reminder that "looks wrong in a
screenshot" is not evidence.

## 10. First real arm, and the first soak run

**2026-08-21 12:13Z. The guardian is armed on Sim101 and the soak suite has run twice.**

### Armed, and it survived a restart

The production ledger, 38 entries, chain verified by the independent Python checker:

```
seq 20  CONFIG_LOADED   {"configHash":"aba4d873…"}
seq 21  ARMED           {"accounts":["Sim101"],"dayKey":"2026-08-21","firmLimit":"1000.00","personalLimit":"600.00"}
seq 22  SEAL_CREATED    {"expiresAtUtc":"2026-08-21T22:00:00.000Z", …}
seq 23  DAY_OPENED      {"dayKey":"2026-08-21"}
seq 24  GUARDIAN_STOPPED {"state":"ARMED"}      <- NinjaTrader restarted here
seq 25  GUARDIAN_STARTED {"state":"ARMED"}
seq 26  STATE_RESTORED   {"sealHash":"aba4d873…"}
seq 27  SEAL_VERIFIED    {"sealHash":"aba4d873…"}
```

`22:00:00Z` is 17:00 America/Chicago — the session boundary, computed through the embedded time-zone map
that §1 showed is the only thing that works in this runtime. The stored `configSnapshot` re-hashes to
`sealHash` when checked outside the process, so the seal is verifiable by someone who does not trust our
code.

**The seal survived a restart** and was re-verified rather than re-created. That is §6 and §7 working on a
real machine rather than in a fixture.

### And the fail-closed path fired on its own, for the right reason

Immediately after that restart, seq 28–37:

```
ACCOUNT_UNKNOWN      {"account":"Sim101","detail":"account is Disconnected"}
FAIL_CLOSED_ENTERED  {"reason":"AccountUnknown on Sim101: account is Disconnected"}
…six seconds of the same…
FAIL_CLOSED_CLEARED  {"previousReason":"AccountUnknown on Sim101: account is Disconnected"}
PNL_CHECKPOINT       {"dayLoss":"0.00","perAccount":{"Sim101":"0.00"},"trigger":"transition"}
```

NinjaTrader had not finished connecting. The guardian blocked entries for six seconds, said exactly why, and
cleared **through a re-computation** when the account came back. Nobody staged that; it is the startup
sequence of §3 meeting the rule of §10.

Two things to carry forward, neither a defect:

- `ACCOUNT_UNKNOWN` was logged once per evaluation — eight lines for one condition. Honest, but noisy. A
  candidate improvement is to log the transition rather than the state.
- The event is named `ACCOUNT_UNKNOWN` while the detail says `Disconnected`. The ledger carries the truth in
  `detail`; only the event name is coarse. Already noted as an amendment candidate.

### The soak found a defect on its first run — in the soak

```
order while locked is cancelled | logged=True, stillWorking=True | FAIL
```

Five of six scenarios passed, twice, identically. The failure was real and the diagnosis is not comfortable:
**the sandbox handed its Guardian the synthetic broker**, so `OnOrderObserved → CancelAllOrders` cancelled an
in-memory list and never reached the venue. Core decided correctly and logged `ORDER_REJECTED_LOCKED`; the
order stayed working at NinjaTrader because nothing had asked NinjaTrader to cancel it.

The scenario asserted something it had itself wired to be impossible. Which means **the guardian's
enforcement path was not proven end-to-end against a real order** — precisely what that scenario existed to
prove. The temptation was to relax the assertion. The fix is the wiring:

`ScopedNtBroker` talks to the real NinjaTrader account and is deliberately narrow:

- it cancels **only** orders tagged `deadman-soak`, so it can never touch an order the trader placed;
- it **never flattens**. A soak that can flatten a real account is a soak that can cost money. `Flatten` logs
  a refusal and does nothing.

The locked-order scenario now uses it. Pending one compile to take effect, and its result on the next run is
the thing to look at first.

### Order hygiene, observed

Every run placed exactly one order of the three allowed, priced at a tenth of the market
(`LIMIT buy @ 769.25` against a market near 7,690), and the run's own cleanup cancelled it:
`cleanup: cancelled 1 leftover order(s)`. `Account.All` in the log shows the funded account
`<funded-acct>/Provider31` sitting alongside `Sim101/Simulator` and untouched, run after run.

## 11. The enforcement path, proven end-to-end against a real order

**2026-08-21 12:28Z. 6 of 6, twice. The scenario that failed in §10 now passes, and it passes for the
right reason.**

The claim in §10 was that the guardian's cancel never reached the venue. The fix was `ScopedNtBroker`.
Here is the evidence that it works, taken from **NinjaTrader's own log**, not from the suite's verdict on
itself — which is exactly the mistake §10 was about:

```
07:28:13:675  Order='e6c4b237.../Sim101' Name='deadman-soak' New state='Submitted'  MES SEP26 Buy Limit 769.5
07:28:13:786  Order='e6c4b237.../Sim101' Name='deadman-soak' New state='Accepted'
07:28:13:801  Order='e6c4b237.../Sim101' Name='deadman-soak' New state='Working'
07:28:15:187  Order='e6c4b237.../Sim101' Name='deadman-soak' New state='Cancel submitted'
07:28:15:311  Order='e6c4b237.../Sim101' Name='deadman-soak' New state='Cancelled'
```

And the sandbox ledger, seq 10:

```
ORDER_REJECTED_LOCKED  {"account":"Sim101","action":"Buy","instrument":"MES SEP26",
                        "orderId":"e6c4b237666c42968f755590ac705a5a"}
```

**Same order id in both.** The guardian was handed a real, working order on a real account while LOCKED; it
logged the rejection and the order reached `Cancelled` at the venue. `Filled=0` throughout.

### The latency, separated honestly

`Cbi.Account.Cancel0` is at `15:184` in the trace and the guardian's own `ORDER_REJECTED_LOCKED` is stamped
`12:28:15.187Z`. The 1.38 s between `Working` and the cancel is the scenario's own `Thread.Sleep(1500)`,
not the guardian: the suite deliberately lets the order rest before handing it over. Ours is the gap from
`OnOrderObserved` to `Cancel0` — single-digit milliseconds — and the venue's acknowledgement took a further
**127 ms** (`15:184` → `15:311`). That is consistent with the 316 ms detect-and-cancel measured in §5, of
which 14 ms were ours. **Do not quote the 1.38 s as a guardian number.**

### The other five, unchanged

Breach → LOCKED with `LIMIT_BREACHED`, `ORDERS_CANCELLED`, `FLATTEN_REQUESTED`, `FLATTEN_VERIFIED`.
Hand-edited seal → `SEAL_MISMATCH` → LOCKED. Config edited under seal → `CONFIG_TAMPERED` → LOCKED. Killed
mid-lockout → state on disk was already LOCKED before the broker was touched, restart resumes LOCKED. Wall
clock pushed nine hours forward → `FailClosed`, seal unchanged, `CLOCK_ANOMALY`. Every sandbox chain verified
`OK`. One order of three allowed per run, and `Account.All` shows the funded `<funded-acct>/Provider31` beside
`Sim101/Simulator`, untouched.

### A live fact about the seal that the soak does NOT cover

The production seal `aba4d873…` has now survived a hot F5 recompile (`12:26:02`, down 27 ms) and a cold
NinjaTrader restart (`12:27:03` → `12:27:28`, down 25 s), re-verified both times with `STATE_RESTORED` →
`SEAL_VERIFIED` and the same hash from the original arm at `12:13:09Z`.

But `HasMonotonicContinuity` is `_state.Seal.RunId == _runId` (`Guardian.cs:107`), and the live state file now
reads:

```
state.runId : 1e1b67cafa30449f8f4aa20d4ac45c4b
seal.runId  : 70aa07e449a342d2ac18bcd4b576148b   -> continuity: False
```

So **today's seal is being measured on the wall clock, not the monotonic counter** (`Guardian.cs:489-494`).
That is the documented gap of SPEC §17.2 behaving exactly as written, but it is worth stating plainly: the
clock-forward scenario passes because it pushes the wall clock *inside one process*, where the monotonic
defence applies. It does not exercise the restart-then-move-the-clock path, because after a restart there is
no monotonic evidence left to appeal to. **The sales page's "moving the clock does not help" is true only
without an intervening restart**, and any copy that omits that is overclaiming. A restart plus a clock change
is still a premeditated two-step act that both leaves marks in the ledger, which is the design's actual claim.

### Two adapter observations, neither a defect

- `subscribed to Sim101` appears three times per boot. Checked rather than assumed: `SubscribeToAccount`
  calls `UnsubscribeFromAccount` first (`DeadmanGuardianAddOn.cs:110`), so the handlers are not stacked and
  fills cannot be double-counted. Noisy logging, nothing more.
- The `ACCOUNT_UNKNOWN` chatter of §10 recurred on the cold restart: eight entries in seven seconds while
  Sim101 finished connecting, then `FAIL_CLOSED_CLEARED` through a re-computation. Still honest, still noisy;
  still a candidate for logging the transition rather than the state.

## Step 3 is complete

| obligation | state |
|---|---|
| time zone resolved inside the NT8 process | measured (§1) |
| monotonic clock across suspension | measured across two real S3 sleeps (§2) |
| real AddOn lifecycle | measured, with one design consequence carried into the adapter (§3) |
| pre-submit hook | verified absent, 2,912 types scanned at runtime (§4) |
| detect-and-cancel latency | measured: 14.4 ms ours, 315.9 ms end to end (§5) |
| install procedure | settled, including what does *not* work (§6) |
| NtAdapter and status window | **installed and running** in real NT8, armed on `Sim101`, enforcement proven end-to-end against a real order (§9, §11) |

The one thing deliberately left undone is the install itself. A NinjaScript compile error takes down every
custom script in the platform, so `install.ps1` runs with a human watching — and after it, one `F5`.
