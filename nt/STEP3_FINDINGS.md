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

## 6. Deployment mechanics — PARTIALLY UNDERSTOOD, and I will not overstate it

NT8 8.1.8.2 compiles `Documents\NinjaTrader 8\bin\Custom\NinjaTrader.Custom.csproj`: an SDK-style project,
`net48`, `x64`, WPF on, `LangVersion 13`, `EnableDefaultCompileItems=false` with 294 explicit
`<Compile Include>` items.

What was observed, in order:

1. Probe `.cs` copied into `bin\Custom\AddOns\`, csproj **not** touched. NT8 launched. At +90 s the csproj
   was unchanged and no report existed. At ~+2.5 min NT8 rebuilt `NinjaTrader.Custom.dll` — and the probe
   **did** run, writing its report. **This run is the evidence above.**
2. csproj entry added by hand, NT8 restarted: report written again.
3. csproj restored to the original (no entry), evidence archived, NT8 restarted: **no report after 302 s**.
4. Entry re-added, NT8 restarted: **no report after 302 s either** — and `NinjaTrader.Custom.dll` was never
   rebuilt, so nothing was compiled at all in that run.

Runs 3 and 4 disagree with each other, which means the variable is not the csproj entry alone. By run 4 the
platform had been force-killed three times and was sitting on a `Welcome` window without progressing to a
compile, which is consistent with a startup dialog waiting for a click that no automated session can give.

**Therefore: unresolved.** What is certain is that the probe compiled and ran in the real platform and
produced the results in sections 1–4. What the minimal reliable install procedure is — copy the file, or
create it through the NinjaScript Editor so NT8 maintains its own compile list — needs one clean run with a
human present. I am not going to invent a mechanism to make the story tidy.

**State of the machine, exactly as left:**

- `bin\Custom\AddOns\DeadmanGuardianProbe.cs` — deployed, still there.
- `bin\Custom\NinjaTrader.Custom.csproj` — **modified by me**: one line added,
  `<Compile Include="AddOns\DeadmanGuardianProbe.cs" />`. The untouched original is in this repo at
  [`backups/NinjaTrader.Custom.csproj.bak`](backups/NinjaTrader.Custom.csproj.bak); restoring it is a copy.
- NinjaTrader is **stopped**.
- Nothing else on the machine was changed. No account was traded, no order was placed, no connection was
  configured.

---

## Handoff — the three things that need a human

1. **Start NT8 normally** (from the desktop, so any startup dialog can be answered) and confirm the
   Control Center reaches the connected state. If the probe report under
   `Documents\NinjaTrader 8\deadman-guardian-probe\` refreshes, the deployment procedure is "copy the file
   plus the csproj line" and §6 above can be closed.
2. **Place and cancel one order on Sim101** — any instrument, any size, it will not be filled if you cancel
   it. That single order produces the latency numbers of §5. The probe only observes it.
3. **Sleep or hibernate the machine** with NT8 running, then resume, and leave it running for a minute. The
   next `CLOCK_SAMPLE` rows in `probe_trace.jsonl` answer §17.2's suspend question.

None of the three requires touching a live account, and the probe cannot place an order even if asked to.
