# deadman-guardian — Step 3 platform probe report

Written from inside the NinjaTrader process. Read-only probe: it places no orders,
cancels nothing and opens no socket.

- Written because: **Configure**
- Local time: 2026-08-20T23:03:25.2418898-05:00
- UTC: 2026-08-21T04:03:25.2428871Z
- Process: NinjaTrader pid 24208
- Runtime: .NET Framework 4.8.9300.0
- CLR: 4.0.30319.42000
- OS: Microsoft Windows 10.0.22631 
- NinjaTrader.Core: 8.1.8.2
- Machine time zone: Central Standard Time ((UTC-06:00) Central Time (US & Canada))

## 1. Time zone resolution inside NT8 (SPEC §5.1)

| id tried | result |
|---|---|
| `America/Chicago` | **TimeZoneNotFoundException** |
| `America/New_York` | **TimeZoneNotFoundException** |
| `UTC` | OK → `UTC` (offset now 00:00:00) |
| `Central Standard Time` | OK → `Central Standard Time` (offset now -05:00:00) |
| `Eastern Standard Time` | OK → `Eastern Standard Time` (offset now -04:00:00) |

DST check with the Windows id, the two dates SPEC §5.1 pins:

| local 17:00 CT | UTC | daylight? |
|---|---|---|
| 2026-03-09 17:00 | 2026-03-09 22:00Z | True |
| 2026-11-02 17:00 | 2026-11-02 23:00Z | False |

## 2. Clock sources (SPEC §6.4, §17.2)

- `Environment.TickCount64` present on this runtime: **NO (only TickCount:Int32, wraps at 24.9 days)**
- `Stopwatch.IsHighResolution`: True, Frequency 10000000
- samples taken: 0 (every 30 s; see `probe_trace.jsonl`)
- since Configure: wall 12 ms, Stopwatch 11 ms, GetTickCount64 16 ms
- wall − Stopwatch: 1 ms · wall − GetTickCount64: -4 ms

**Suspend test (needs a human):** hibernate or sleep the machine with NT8 running,
resume, and read the next `CLOCK_SAMPLE` rows. A source that keeps counting through
suspend leaves `wallMinus…Ms` near zero; one that stops shows a jump equal to the
suspended duration. SPEC §7.5 is correct either way — only the size of the logged
divergence changes — but the number belongs in the record.

## 3. AddOn lifecycle (SPEC §3.3)

```
04:03:25.232  OnStateChange -> SetDefaults
04:03:25.234  OnStateChange -> Configure
04:03:25.236  Account.All = [Backtest, Playback101, Sim101, <funded-acct>]
04:03:25.236  found Sim101 connection=Connected denomination=UsDollar
04:03:25.237  subscribed to OrderUpdate, ExecutionUpdate, PositionUpdate, AccountItemUpdate
```

## 4. Pre-submit interception (SPEC §3.3, §9.5)

Scanned **2912** types in `NinjaTrader.Core 8.1.8.2` at runtime for an event that could veto an order
before submission (`Submit*`, `Validat*`, `Approv*`, `Intercept*`, `Before*`).

**Result: none.** Enforcement stays detect-and-cancel, as SPEC §9.5 specifies.


## 5. Observed event latency

Time from the timestamp NT8 puts on the event to the moment this handler could act.
It is the *detect* half of detect-and-cancel; the cancel round-trip is measured in
Stage B, when the guardian is wired and allowed to cancel on Sim101.

- order events seen: **0**
- execution events seen: **0**
- _no order has been placed on Sim101 while this probe was running._

