# BOTA_REPORT

---

## Bot A run 2026-08-22 19:18:17Z

- account: `Sim101` (Provider proven `Simulator` before anything was sent)
- sandbox guardian limit: personal $50.00, firm $100.00 (production's files untouched)
- orders: 72/200 orders, 72 contracts requested, caps 1/order and 1 net
- round trips before the lockout: **32**

### What the guardian did

| | |
|---|---|
| fired | **yes**, 19:20:44.005Z |
| day loss at the lockout | $50.00 |
| reason | daily loss limit reached |
| ledger events | 40 (GUARDIAN_STARTED, CONFIG_LOADED, ARMED, SEAL_CREATED, DAY_OPENED, LIMIT_BREACHED, ORDERS_CANCELLED, ORDER_REJECTED_LOCKED, FLATTEN_REQUESTED, LOCKOUT_INCOMPLETE) |
| ledger chain | OK |
| left the Locked state during provocation | no |
| position left open at the end | 0 |

### Post-lockout entry attempts

Two kinds, because NT8 has no pre-submit veto (addon header, 2,912 types scanned) and the
two kinds meet enforcement differently. Reported separately on purpose.

| probe kind | submitted | stopped by the guardian | not stopped | latency |
|---|---|---|---|---|
| resting LIMIT (cancellable) | 4 | 4 cancelled | 0 left working | min 232 ms, median 241 ms, max 248 ms |
| MARKET (fills first) | 4 | 0 flattened after filling | 0 left open | n/a |

Market probes that filled: 0 of 4. A market order
reaching a fill is **not** a guardian failure - it is the documented consequence of
detect-and-cancel. The claim under test is the next column: the exposure did not survive.

### The account gate, re-asked before every send

| | |
|---|---|
| times evaluated | 72 |
| cost per call | min 25 us, median 66 us, max 232 us |
| closed mid-run | no |
| bot-events chain | empty |
| bot-event write failures | 0 (a zero that is always printed, so it is a verified zero) |

### Did this run disturb production?

- production guardian state after the run: **ARMED**
- production files were never opened for writing by this bot; its limit is $600 and this
  run's losses are bounded by the sandbox limit of $50.00.

<details><summary>run log</summary>

```
14:17:32.340  gate present; bot A armed, starting in 45s
14:18:17.349  Account.All = [Backtest/Simulator/Disconnected, Playback101/Playback/Disconnected, Sim101/Simulator/Connected, 2127534/Provider31/Disconnected]
14:18:17.351  verified Sim101 Provider=Simulator, Connected
14:18:17.352  instrument resolved: MES 09-26  pointValue=5  tickSize=0.25
14:18:17.352  gate burned before sending anything: C:\Users\home\Documents\NinjaTrader 8\deadman-guardian-bots\botA.GO
14:18:17.360  sandbox guardian started at C:\Users\home\Documents\NinjaTrader 8\deadman-guardian-bots\runs\botA-20260822-191817; state=Disarmed
14:18:17.374  sandbox arm -> ARMED
14:18:17.374  subscribed to Sim101 (executions and orders feed the sandbox guardian)
14:18:17.375  ---- loss phase: churning until the sandbox guardian locks at $50.00 ----
14:18:38.768  round trip 5; sandbox dayLoss=0.00; 10/200 orders, 10 contracts requested, caps 1/order and 1 net
14:19:01.725  round trip 10; sandbox dayLoss=0.00; 20/200 orders, 20 contracts requested, caps 1/order and 1 net
14:19:24.617  round trip 15; sandbox dayLoss=0.00; 30/200 orders, 30 contracts requested, caps 1/order and 1 net
14:19:47.524  round trip 20; sandbox dayLoss=0.00; 40/200 orders, 40 contracts requested, caps 1/order and 1 net
14:20:10.435  round trip 25; sandbox dayLoss=0.00; 50/200 orders, 50 contracts requested, caps 1/order and 1 net
14:20:33.327  round trip 30; sandbox dayLoss=0.00; 60/200 orders, 60 contracts requested, caps 1/order and 1 net
14:20:42.670  flatten 1 instrument(s) on Sim101
14:20:42.703  cancel 1 order(s) on Sim101
14:20:42.821  cancel 1 order(s) on Sim101
14:20:44.005  LOCKED after 32 round trips; dayLoss=50.00; reason=daily loss limit reached
14:20:44.006  ---- provocation phase: 8 entry attempts against a LOCKED guardian ----
14:20:44.028  cancel 1 order(s) on Sim101
14:20:44.029  probe 1 (limit) submitted @ 769.25 - the guardian should cancel it
14:20:44.146  cancel 1 order(s) on Sim101
14:20:59.043  probe 1 (limit): CANCELLED by the guardian after 248 ms
14:21:05.095  cancel 1 order(s) on Sim101
14:21:05.097  probe 2 (market) submitted - the guardian cannot stop the fill; it must flatten it
14:21:05.214  cancel 1 order(s) on Sim101
14:21:20.229  probe 2 (market): never filled, state=Cancelled
14:21:26.254  cancel 1 order(s) on Sim101
14:21:26.255  probe 3 (limit) submitted @ 769.5 - the guardian should cancel it
14:21:26.367  cancel 1 order(s) on Sim101
14:21:41.279  probe 3 (limit): CANCELLED by the guardian after 232 ms
14:21:47.337  cancel 1 order(s) on Sim101
14:21:47.338  probe 4 (market) submitted - the guardian cannot stop the fill; it must flatten it
14:21:47.442  cancel 1 order(s) on Sim101
14:22:02.460  probe 4 (market): never filled, state=Cancelled
14:22:08.488  cancel 1 order(s) on Sim101
14:22:08.491  probe 5 (limit) submitted @ 769.5 - the guardian should cancel it
14:22:08.607  cancel 1 order(s) on Sim101
14:22:23.519  probe 5 (limit): CANCELLED by the guardian after 241 ms
14:22:29.591  cancel 1 order(s) on Sim101
14:22:29.592  probe 6 (market) submitted - the guardian cannot stop the fill; it must flatten it
14:22:29.703  cancel 1 order(s) on Sim101
14:22:44.712  probe 6 (market): never filled, state=Cancelled
14:22:50.739  cancel 1 order(s) on Sim101
14:22:50.740  probe 7 (limit) submitted @ 769.75 - the guardian should cancel it
14:22:50.850  cancel 1 order(s) on Sim101
14:23:05.768  probe 7 (limit): CANCELLED by the guardian after 232 ms
14:23:11.802  cancel 1 order(s) on Sim101
14:23:11.804  probe 8 (market) submitted - the guardian cannot stop the fill; it must flatten it
14:23:11.918  cancel 1 order(s) on Sim101
14:23:26.921  probe 8 (market): never filled, state=Cancelled
14:23:32.962  provocation finished; guardian state = Locked; net position left = 0
14:23:32.962  shutdown: run finished
```

</details>

