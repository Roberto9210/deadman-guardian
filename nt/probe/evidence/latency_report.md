# deadman-guardian — detect-and-cancel latency

One resting limit order on **Sim101**, placed programmatically, watched to a
working state and cancelled immediately. It could not fill: the limit sat far below the market.

- UTC: 2026-08-21T04:03:55.5968583Z
- submitted at: 2026-08-21T04:03:55.2774421Z

## Measurements

| leg | what it covers | ms |
|---|---|---|
| submit → working observed | our call out, the venue's accept, and NT8 raising the event | 171.1 |
| **working observed → cancel issued** | **the guardian's own reaction — the only part this design controls** | 14.4 |
| cancel issued → cancelled confirmed | the venue's round trip back | 130.4 |
| **submit → cancelled confirmed** | **the whole cycle, end to end** | 315.9 |

The middle row is the number SPEC §9.5 is about. The others are the platform's and the venue's,
and no add-on can shrink them — which is exactly why §2 says this bounds exposure and not loss.

## Trace

```
04:03:25.231  gate file present; latency probe armed
04:03:55.243  Account.All = [Backtest/Simulator, Playback101/Playback, Sim101/Simulator, <funded-acct>/Provider31]
04:03:55.243  verified Provider=Simulator
04:03:55.243  SimulatorInitialCash=100000
04:03:55.244  denomination=UsDollar
04:03:55.244  connection options=TradovateOptions name='Simulation' brand='NinjaTrader'
04:03:55.244  verified ConnectionStatus=Connected
04:03:55.244  instrument resolved: MES 09-26
04:03:55.244  market reference price = 7667.5
04:03:55.244  buy limit price = 766.75 (tick size 0.25)
04:03:55.248  gate file deleted before submitting
04:03:55.277  submitting 1 LIMIT buy @ 766.75 on MES SEP26 / Sim101
04:03:55.339  order state -> Submitted  (NT8 event time 23:03:55.299)
04:03:55.449  order state -> Accepted  (NT8 event time 23:03:55.448)
04:03:55.459  order state -> CancelPending  (NT8 event time 23:03:55.458)
04:03:55.462  order state -> CancelSubmitted  (NT8 event time 23:03:55.462)
04:03:55.462  cancel issued
04:03:55.463  order state -> Working  (NT8 event time 23:03:55.462)
04:03:55.593  order state -> Cancelled  (NT8 event time 23:03:55.569)
```
