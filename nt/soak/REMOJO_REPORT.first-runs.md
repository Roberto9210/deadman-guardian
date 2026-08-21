# REMOJO — deadman-guardian soak on Sim101

An automated attacker, not a demo. Each run tries to make the guardian fail the way its
owner would: breach the limit, edit the sealed config, hand-edit the state, kill it
mid-lockout, submit orders while locked, and push the clock past expiry.

Every scenario drives a **sandbox** guardian with its own state and ledger, over the real
NinjaTrader ports. The production guardian's files are never touched. Orders, where a
scenario needs one, are LIMIT only, priced where they cannot fill, capped per session, and
cancelled by the run. `Sim101` is verified to be the simulator before anything is sent.

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
