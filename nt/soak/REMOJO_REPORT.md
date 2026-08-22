# REMOJO — deadman-guardian soak on Sim101

An automated attacker, not a demo. Each run tries to make the guardian fail the way its
owner would: breach the limit, edit the sealed config, hand-edit the state, kill it
mid-lockout, submit orders while locked, and push the clock past expiry.

Every scenario drives a **sandbox** guardian with its own state and ledger, over the real
NinjaTrader ports. The production guardian's files are never touched. Orders, where a
scenario needs one, are LIMIT only, priced where they cannot fill, capped per session, and
cancelled by the run. `Sim101` is verified to be the simulator before anything is sent.

## What this suite CANNOT prove — read this before reading "6 of 6"

Its P&L is synthetic: fills are injected as `ExecutionRecord`s, because making a simulated account
lose exactly $600 needs fillable orders and fillable orders are what this suite refuses to send. That
choice is right, and it puts entire code paths permanently out of reach. **A green here is silent
about them, not favourable.**

**The lockout retry path.** With synthetic P&L the flatten is instantaneous, so the guardian's
position check always succeeds on the first attempt and `LOCKOUT_INCOMPLETE` never occurs. Sixteen
consecutive 6-of-6 runs never once executed the retry — **structurally could not**. Bot A hit it on
its first run against real fills (2026-08-22): the flatten is a real market order, the first check
found the position still open, and the guardian retried on the next tick and verified 502 ms later.
That path is the most safety-critical in the product and this suite cannot reach it.

**Anything that depends on real execution latency.** Time-to-fill, time-to-cancel, the window between
a post-lockout order being submitted and being stopped, partial fills, and the §5.4 cross-check
between Core's arithmetic and NinjaTrader's own `GrossRealizedProfitLoss` — all of them need orders
that actually fill. None of them is exercised here.

**What it does prove**, precisely: that GuardianCore's RULES are right — the state machine, the seal,
the tamper detection, the clock defences, the ledger chain — over inputs the suite controls. That is
worth having and it is not the same claim.

The bots in [`nt/bots/`](../bots/) exist for the other half.

---

## Soak run 2026-08-21 12:13:46Z

- account: `Sim101` (Provider must be `Simulator`, verified before anything is sent)
- orders placed this session: **1** of 3 allowed, all LIMIT, all priced below any possible fill, all cancelled
- scenarios: **5 of 6 passed**

| scenario | expected | observed | ledger chain | |
|---|---|---|---|---|
| breach at the limit locks out | LOCKED, LIMIT_BREACHED in the ledger | Locked, events: 9 (GUARDIAN_STARTED, CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED, LIMIT_BREACHED, ORDERS_CANCELLED, FLATTEN_REQUESTED) | OK | PASS |
| order while locked is cancelled | ORDER_REJECTED_LOCKED logged and the order no longer working | logged=True, stillWorking=True | OK | **FAIL** |
| hand-edited seal is caught | SEAL_MISMATCH then LOCKED | Locked, mismatch logged=True | OK | PASS |
| config edited under seal is caught | CONFIG_TAMPERED then LOCKED | Locked, tampered logged=True | OK | PASS |
| killed mid-lockout resumes LOCKED | state on disk LOCKED before the broker was touched, and the restart resumes LOCKED | onDisk=True, afterRestart=Locked, positionsLeft=0 | OK | PASS |
| wall clock pushed past expiry does not release the seal | seal maintained, entries blocked, CLOCK_ANOMALY logged | FailClosed, sealSame=True, anomaly=True | OK | PASS |

<details><summary>run log</summary>

```
12:13:01.144  gate present; soak armed
12:13:46.154  Account.All = [Backtest/Simulator, Playback101/Playback, Sim101/Simulator, <funded-acct>/Provider31]
12:13:46.154  verified Sim101 Provider=Simulator, Connected
12:13:46.182  [breach] arm -> ARMED
12:13:46.237  PASS  breach at the limit locks out  ->  Locked, events: 9 (GUARDIAN_STARTED, CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED, LIMIT_BREACHED, ORDERS_CANCELLED, FLATTEN_REQUESTED)
12:13:46.260  [locked-order] arm -> ARMED
12:13:46.303  instrument: MES 09-26
12:13:46.303  placing 1 LIMIT buy @ 769.25 on MES SEP26 (order 1/3)
12:13:49.903  cleanup: cancelled 1 leftover order(s)
12:13:49.909  FAIL  order while locked is cancelled  ->  logged=True, stillWorking=True
12:13:49.930  [seal-tamper] arm -> ARMED
12:13:49.962  [seal-tamper] restarted -> Locked
12:13:49.963  PASS  hand-edited seal is caught  ->  Locked, mismatch logged=True
12:13:49.983  [config-tamper] arm -> ARMED
12:13:50.011  PASS  config edited under seal is caught  ->  Locked, tampered logged=True
12:13:50.048  [kill-mid-lockout] arm -> ARMED
12:13:50.112  [kill-mid-lockout] restarted -> Locked
12:13:50.114  PASS  killed mid-lockout resumes LOCKED  ->  onDisk=True, afterRestart=Locked, positionsLeft=0
12:13:50.136  [clock-forward] arm -> ARMED
12:13:50.163  PASS  wall clock pushed past expiry does not release the seal  ->  FailClosed, sealSame=True, anomaly=True
```
</details>

---

## Soak run 2026-08-21 12:15:42Z

- account: `Sim101` (Provider must be `Simulator`, verified before anything is sent)
- orders placed this session: **1** of 3 allowed, all LIMIT, all priced below any possible fill, all cancelled
- scenarios: **5 of 6 passed**

| scenario | expected | observed | ledger chain | |
|---|---|---|---|---|
| breach at the limit locks out | LOCKED, LIMIT_BREACHED in the ledger | Locked, events: 9 (GUARDIAN_STARTED, CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED, LIMIT_BREACHED, ORDERS_CANCELLED, FLATTEN_REQUESTED) | OK | PASS |
| order while locked is cancelled | ORDER_REJECTED_LOCKED logged and the order no longer working | logged=True, stillWorking=True | OK | **FAIL** |
| hand-edited seal is caught | SEAL_MISMATCH then LOCKED | Locked, mismatch logged=True | OK | PASS |
| config edited under seal is caught | CONFIG_TAMPERED then LOCKED | Locked, tampered logged=True | OK | PASS |
| killed mid-lockout resumes LOCKED | state on disk LOCKED before the broker was touched, and the restart resumes LOCKED | onDisk=True, afterRestart=Locked, positionsLeft=0 | OK | PASS |
| wall clock pushed past expiry does not release the seal | seal maintained, entries blocked, CLOCK_ANOMALY logged | FailClosed, sealSame=True, anomaly=True | OK | PASS |

<details><summary>run log</summary>

```
12:14:57.988  gate present; soak armed
12:15:43.000  Account.All = [Backtest/Simulator, Playback101/Playback, Sim101/Simulator, <funded-acct>/Provider31]
12:15:43.000  verified Sim101 Provider=Simulator, Connected
12:15:43.033  [breach] arm -> ARMED
12:15:43.080  PASS  breach at the limit locks out  ->  Locked, events: 9 (GUARDIAN_STARTED, CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED, LIMIT_BREACHED, ORDERS_CANCELLED, FLATTEN_REQUESTED)
12:15:43.100  [locked-order] arm -> ARMED
12:15:43.138  instrument: MES 09-26
12:15:43.138  placing 1 LIMIT buy @ 769.75 on MES SEP26 (order 1/3)
12:15:46.738  cleanup: cancelled 1 leftover order(s)
12:15:46.740  FAIL  order while locked is cancelled  ->  logged=True, stillWorking=True
12:15:46.759  [seal-tamper] arm -> ARMED
12:15:46.792  [seal-tamper] restarted -> Locked
12:15:46.793  PASS  hand-edited seal is caught  ->  Locked, mismatch logged=True
12:15:46.872  [config-tamper] arm -> ARMED
12:15:46.919  PASS  config edited under seal is caught  ->  Locked, tampered logged=True
12:15:46.941  [kill-mid-lockout] arm -> ARMED
12:15:46.997  [kill-mid-lockout] restarted -> Locked
12:15:46.998  PASS  killed mid-lockout resumes LOCKED  ->  onDisk=True, afterRestart=Locked, positionsLeft=0
12:15:47.017  [clock-forward] arm -> ARMED
12:15:47.041  PASS  wall clock pushed past expiry does not release the seal  ->  FailClosed, sealSame=True, anomaly=True
```
</details>

---

## Soak run 2026-08-21 12:26:47Z

- account: `Sim101` (Provider must be `Simulator`, verified before anything is sent)
- orders placed this session: **1** of 3 allowed, all LIMIT, all priced below any possible fill, all cancelled
- scenarios: **6 of 6 passed**

| scenario | expected | observed | ledger chain | |
|---|---|---|---|---|
| breach at the limit locks out | LOCKED, LIMIT_BREACHED in the ledger | Locked, events: 9 (GUARDIAN_STARTED, CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED, LIMIT_BREACHED, ORDERS_CANCELLED, FLATTEN_REQUESTED) | OK | PASS |
| order while locked is cancelled | ORDER_REJECTED_LOCKED logged and the order no longer working | logged=True, stillWorking=False | OK | PASS |
| hand-edited seal is caught | SEAL_MISMATCH then LOCKED | Locked, mismatch logged=True | OK | PASS |
| config edited under seal is caught | CONFIG_TAMPERED then LOCKED | Locked, tampered logged=True | OK | PASS |
| killed mid-lockout resumes LOCKED | state on disk LOCKED before the broker was touched, and the restart resumes LOCKED | onDisk=True, afterRestart=Locked, positionsLeft=0 | OK | PASS |
| wall clock pushed past expiry does not release the seal | seal maintained, entries blocked, CLOCK_ANOMALY logged | FailClosed, sealSame=True, anomaly=True | OK | PASS |

<details><summary>run log</summary>

```
12:26:02.490  gate present; soak armed
12:26:47.501  Account.All = [Backtest/Simulator, Playback101/Playback, Sim101/Simulator, <funded-acct>/Provider31]
12:26:47.501  verified Sim101 Provider=Simulator, Connected
12:26:47.528  [breach] arm -> ARMED
12:26:47.572  PASS  breach at the limit locks out  ->  Locked, events: 9 (GUARDIAN_STARTED, CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED, LIMIT_BREACHED, ORDERS_CANCELLED, FLATTEN_REQUESTED)
12:26:47.591  [locked-order] arm -> ARMED
12:26:47.616  scoped cancel: nothing of ours working on Sim101
12:26:47.617  scoped flatten: REFUSED on Sim101 - the soak never flattens a real account
12:26:47.634  instrument: MES 09-26
12:26:47.634  placing 1 LIMIT buy @ 769.5 on MES SEP26 (order 1/3)
12:26:49.154  scoped cancel: 1 order(s) tagged 'deadman-soak' on Sim101
12:26:52.160  PASS  order while locked is cancelled  ->  logged=True, stillWorking=False
12:26:52.183  [seal-tamper] arm -> ARMED
12:26:52.218  [seal-tamper] restarted -> Locked
12:26:52.219  PASS  hand-edited seal is caught  ->  Locked, mismatch logged=True
12:26:52.239  [config-tamper] arm -> ARMED
12:26:52.267  PASS  config edited under seal is caught  ->  Locked, tampered logged=True
12:26:52.287  [kill-mid-lockout] arm -> ARMED
12:26:52.342  [kill-mid-lockout] restarted -> Locked
12:26:52.343  PASS  killed mid-lockout resumes LOCKED  ->  onDisk=True, afterRestart=Locked, positionsLeft=0
12:26:52.361  [clock-forward] arm -> ARMED
12:26:52.385  PASS  wall clock pushed past expiry does not release the seal  ->  FailClosed, sealSame=True, anomaly=True
```
</details>

---

## Soak run 2026-08-21 12:28:13Z

- account: `Sim101` (Provider must be `Simulator`, verified before anything is sent)
- orders placed this session: **1** of 3 allowed, all LIMIT, all priced below any possible fill, all cancelled
- scenarios: **6 of 6 passed**

| scenario | expected | observed | ledger chain | |
|---|---|---|---|---|
| breach at the limit locks out | LOCKED, LIMIT_BREACHED in the ledger | Locked, events: 9 (GUARDIAN_STARTED, CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED, LIMIT_BREACHED, ORDERS_CANCELLED, FLATTEN_REQUESTED) | OK | PASS |
| order while locked is cancelled | ORDER_REJECTED_LOCKED logged and the order no longer working | logged=True, stillWorking=False | OK | PASS |
| hand-edited seal is caught | SEAL_MISMATCH then LOCKED | Locked, mismatch logged=True | OK | PASS |
| config edited under seal is caught | CONFIG_TAMPERED then LOCKED | Locked, tampered logged=True | OK | PASS |
| killed mid-lockout resumes LOCKED | state on disk LOCKED before the broker was touched, and the restart resumes LOCKED | onDisk=True, afterRestart=Locked, positionsLeft=0 | OK | PASS |
| wall clock pushed past expiry does not release the seal | seal maintained, entries blocked, CLOCK_ANOMALY logged | FailClosed, sealSame=True, anomaly=True | OK | PASS |

<details><summary>run log</summary>

```
12:27:28.457  gate present; soak armed
12:28:13.455  Account.All = [Backtest/Simulator, Playback101/Playback, Sim101/Simulator, <funded-acct>/Provider31]
12:28:13.455  verified Sim101 Provider=Simulator, Connected
12:28:13.487  [breach] arm -> ARMED
12:28:13.539  PASS  breach at the limit locks out  ->  Locked, events: 9 (GUARDIAN_STARTED, CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED, LIMIT_BREACHED, ORDERS_CANCELLED, FLATTEN_REQUESTED)
12:28:13.561  [locked-order] arm -> ARMED
12:28:13.588  scoped cancel: nothing of ours working on Sim101
12:28:13.589  scoped flatten: REFUSED on Sim101 - the soak never flattens a real account
12:28:13.605  instrument: MES 09-26
12:28:13.605  placing 1 LIMIT buy @ 769.5 on MES SEP26 (order 1/3)
12:28:15.180  scoped cancel: 1 order(s) tagged 'deadman-soak' on Sim101
12:28:18.191  PASS  order while locked is cancelled  ->  logged=True, stillWorking=False
12:28:18.215  [seal-tamper] arm -> ARMED
12:28:18.250  [seal-tamper] restarted -> Locked
12:28:18.251  PASS  hand-edited seal is caught  ->  Locked, mismatch logged=True
12:28:18.269  [config-tamper] arm -> ARMED
12:28:18.305  PASS  config edited under seal is caught  ->  Locked, tampered logged=True
12:28:18.328  [kill-mid-lockout] arm -> ARMED
12:28:18.390  [kill-mid-lockout] restarted -> Locked
12:28:18.391  PASS  killed mid-lockout resumes LOCKED  ->  onDisk=True, afterRestart=Locked, positionsLeft=0
12:28:18.417  [clock-forward] arm -> ARMED
12:28:18.446  PASS  wall clock pushed past expiry does not release the seal  ->  FailClosed, sealSame=True, anomaly=True
```
</details>
