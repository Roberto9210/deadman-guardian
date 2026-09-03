"""Read NinjaTrader 8's DAILY .ncd bar files once, check them against independent evidence,
and write CSV.

WHAT THIS IS AND IS NOT. A ONE-TIME EXTRACTION, not a pipeline. .ncd is an undocumented
proprietary format; a build that depends on decoding it breaks with any NinjaTrader update.
The supported way out of the platform is its own historical-data export. This exists because
the daily files have a fixed layout that can be checked against evidence NinjaTrader did not
produce, and because running it once and keeping the CSV costs nothing afterwards.

It reads. It never writes into the NinjaTrader tree and never touches the platform.

THE LAYOUT IS A HYPOTHESIS, DECLARED BEFORE THE CHECKS RUN
    header   28 bytes:  int32 version | float64 tickSize | float64 ? | int64 firstTicks
    record   48 bytes:  int64 ticks (.NET, 100ns since 0001-01-01)
                        float64 open, high, low, close
                        int64 volume
Everything little-endian. Derived from a hex dump of db/day/ES 12-16/2016.Last.ncd and from
the fact that (size - 28) % 48 == 0 for all 306 files. FITTING IS NOT DECODING: a shifted
offset or the wrong endianness also fits. That is what the controls below are for.

THE CONTROLS, and each one can fail
  1  TICK GRID       every open/high/low/close is an exact multiple of the instrument's tick
                     size - and the tick size comes from MasterInstruments in NinjaTrader.sqlite,
                     a SEPARATE artefact. The catalogue says what a tick is worth and the bars
                     have to obey it. Wrong offset or endianness produces prices off the grid,
                     and the chance of landing on it by accident is negligible.
  1b HEADER TICK     the tick size embedded in each file's own header must equal the
                     catalogue's. Free, and independent of control 1's arithmetic.
  2  OHLC ORDER      low <= open, close <= high in every bar. Catches a wrong field order.
  3  EPOCH           the year derived from the .NET ticks must equal the year in the filename.
                     Catches a mis-converted epoch, which is the most common failure here.
  4  SANITY          volume integral and >= 0, timestamps strictly increasing within a file.

IF ANY CONTROL FAILS THIS STOPS AND SAYS SO. It does not adjust the layout until the checks
pass: a decoder tuned until its own checks go green is a complicit verification, and the
checks stop being evidence the moment they are used as a fitting target.

A TRAP THAT WAS MEASURED, not guessed: MasterInstruments.Name is NOT unique. "ES" appears
three times - the future (tick 0.25), a US equity (0.01) and an index (0.01). Taking the first
row would have given 0.01, and control 1 would then pass VACUOUSLY, because every price is a
multiple of 0.01. So the lookup requires InstrumentType = 0 and asserts exactly one match.
"""

import csv
import os
import struct
import sqlite3
import sys
from collections import defaultdict
from datetime import datetime, timedelta

# ---------------------------------------------------------------- paths

NT8 = os.path.join(os.path.expanduser("~"), "Documents", "NinjaTrader 8")
DAY_DIR = os.path.join(NT8, "db", "day")
SQLITE_LIVE = os.path.join(NT8, "db", "NinjaTrader.sqlite")

OUT_DIR = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.getcwd(), "nt8-daily-csv")

HEADER_BYTES = 28
RECORD_BYTES = 48
RECORD = struct.Struct("<q4dq")          # ticks, open, high, low, close, volume
HEADER_TICK = struct.Struct("<d")        # at offset 4

TICKS_PER_MICROSECOND = 10
DOTNET_EPOCH = datetime(1, 1, 1)


def ticks_to_datetime(ticks):
    return DOTNET_EPOCH + timedelta(microseconds=ticks // TICKS_PER_MICROSECOND)


# ---------------------------------------------------------------- the catalogue

def tick_sizes(db_path):
    """{root symbol: tick size} for FUTURES only. Raises if a root is missing or ambiguous."""
    tmp = os.path.join(OUT_DIR, "_catalogue.sqlite")
    os.makedirs(OUT_DIR, exist_ok=True)
    with open(db_path, "rb") as src, open(tmp, "wb") as dst:
        dst.write(src.read())                       # never open the live file
    con = sqlite3.connect(f"file:{tmp}?mode=ro", uri=True)
    con.text_factory = lambda b: b.decode("utf-8", "replace")
    rows = con.execute(
        "SELECT Name, TickSize, TradingHours FROM MasterInstruments WHERE InstrumentType = 0"
    ).fetchall()
    con.close()
    os.remove(tmp)

    sizes, hours, seen = {}, {}, defaultdict(int)
    for name, tick, th in rows:
        seen[name] += 1
        sizes[name] = tick
        hours[name] = th
    return sizes, hours, seen


# ---------------------------------------------------------------- decode

def read_bars(path):
    with open(path, "rb") as f:
        blob = f.read()
    if len(blob) < HEADER_BYTES or (len(blob) - HEADER_BYTES) % RECORD_BYTES:
        raise ValueError(f"{path}: {len(blob)} bytes does not fit 28 + 48n")
    header_tick = HEADER_TICK.unpack_from(blob, 4)[0]
    bars = [RECORD.unpack_from(blob, off)
            for off in range(HEADER_BYTES, len(blob), RECORD_BYTES)]
    return header_tick, bars


def on_grid(price, tick):
    q = price / tick
    return abs(q - round(q)) < 1e-6


# ---------------------------------------------------------------- main

def main():
    sizes, hours, seen = tick_sizes(SQLITE_LIVE)

    contracts = sorted(d for d in os.listdir(DAY_DIR)
                       if os.path.isdir(os.path.join(DAY_DIR, d)))
    roots = sorted({c.split(" ")[0] for c in contracts})

    print("=" * 78)
    print("TICK SIZES FROM THE CATALOGUE (MasterInstruments, InstrumentType = 0 = Future)")
    print("=" * 78)
    fatal = []
    for r in roots:
        if seen.get(r, 0) != 1:
            fatal.append(f"{r}: {seen.get(r, 0)} future rows in the catalogue, need exactly 1")
        else:
            print(f"  {r:<5} tick {sizes[r]:<8} trading hours: {hours[r]}")
    if fatal:
        print("\nSTOPPED - the catalogue cannot name a unique tick size:")
        for f in fatal:
            print("   " + f)
        return 2

    # ------------------------------------------------------------ controls
    v1 = v1b = v2 = v3 = v4 = 0
    examples = defaultdict(list)
    total_bars = 0
    data = {}

    for c in contracts:
        root = c.split(" ")[0]
        tick = sizes[root]
        for fn in sorted(os.listdir(os.path.join(DAY_DIR, c))):
            if not fn.endswith(".ncd"):
                continue
            path = os.path.join(DAY_DIR, c, fn)
            file_year = int(fn.split(".")[0])
            header_tick, bars = read_bars(path)
            total_bars += len(bars)

            if abs(header_tick - tick) > 1e-12:
                v1b += 1
                if len(examples["1b"]) < 5:
                    examples["1b"].append(f"{c}/{fn}: header {header_tick} vs catalogue {tick}")

            prev_ticks = None
            rows = []
            for ticks, o, h, lo, cl, vol in bars:
                dt = ticks_to_datetime(ticks)

                for label, p in (("open", o), ("high", h), ("low", lo), ("close", cl)):
                    if not on_grid(p, tick):
                        v1 += 1
                        if len(examples["1"]) < 5:
                            examples["1"].append(
                                f"{c}/{fn} {dt:%Y-%m-%d} {label}={p!r} not a multiple of {tick}")

                if not (lo <= o <= h and lo <= cl <= h):
                    v2 += 1
                    if len(examples["2"]) < 5:
                        examples["2"].append(
                            f"{c}/{fn} {dt:%Y-%m-%d} o={o} h={h} l={lo} c={cl}")

                if dt.year != file_year:
                    v3 += 1
                    if len(examples["3"]) < 5:
                        examples["3"].append(
                            f"{c}/{fn}: bar dated {dt:%Y-%m-%d}, filename says {file_year}")

                if vol < 0:
                    v4 += 1
                    if len(examples["4"]) < 5:
                        examples["4"].append(f"{c}/{fn} {dt:%Y-%m-%d} volume={vol}")
                if prev_ticks is not None and ticks <= prev_ticks:
                    v4 += 1
                    if len(examples["4"]) < 5:
                        examples["4"].append(
                            f"{c}/{fn} {dt:%Y-%m-%d} timestamp not increasing")
                prev_ticks = ticks

                rows.append((dt.date(), o, h, lo, cl, vol))

            data.setdefault(c, []).extend(rows)

    print()
    print("=" * 78)
    print(f"CONTROLS  -  {len(contracts)} contracts, {total_bars} bars")
    print("=" * 78)
    results = [("1  tick grid (catalogue tick size)", v1),
               ("1b header tick vs catalogue", v1b),
               ("2  low <= open,close <= high", v2),
               ("3  derived year == filename year", v3),
               ("4  volume >= 0 and time increasing", v4)]
    for name, bad in results:
        print(f"  {'PASS' if bad == 0 else 'FAIL'}  {name:<40} violations: {bad}")
    for key in ("1", "1b", "2", "3", "4"):
        for e in examples[key]:
            print(f"        [{key}] {e}")

    if any(bad for _, bad in results):
        print()
        print("STOPPED. A control failed, so nothing is written and no number below is")
        print("trustworthy. The layout is NOT adjusted to make these pass - a decoder tuned")
        print("until its own checks go green has stopped being verified by them.")
        return 1

    # ------------------------------------------------------------ CSV
    os.makedirs(OUT_DIR, exist_ok=True)
    for c, rows in sorted(data.items()):
        rows.sort()
        with open(os.path.join(OUT_DIR, c.replace(" ", "_") + ".csv"),
                  "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f)
            w.writerow(["date", "open", "high", "low", "close", "volume"])
            for d, o, h, lo, cl, vol in rows:
                w.writerow([d.isoformat(), repr(o), repr(h), repr(lo), repr(cl), vol])
    print(f"\nwrote {len(data)} CSV files to {OUT_DIR}")

    # ------------------------------------------------------------ how is a bar stamped?
    # WHICH SESSION A DAILY BAR COVERS is not printed anywhere in these files, but HOW IT IS
    # STAMPED is measurable, and it narrows the question. Two things are reported and neither
    # is interpreted here:
    #   - the time-of-day carried by the record's own timestamp;
    #   - whether any bar is stamped LATER than the wall-clock date on which it was written.
    # The second is the sharp one: a bar dated tomorrow can only come from a session keyed to
    # the exchange's trade date, not to the calendar day it was recorded in.
    times = defaultdict(int)
    for c in contracts:
        root = c.split(" ")[0]
        for fn in sorted(os.listdir(os.path.join(DAY_DIR, c))):
            if not fn.endswith(".ncd"):
                continue
            _, bars = read_bars(os.path.join(DAY_DIR, c, fn))
            for ticks, *_ in bars:
                dt = ticks_to_datetime(ticks)
                times[dt.strftime("%H:%M:%S")] += 1

    print()
    print("=" * 78)
    print("HOW EACH BAR IS TIME-STAMPED (time-of-day inside the record)")
    print("=" * 78)
    for t, n in sorted(times.items(), key=lambda kv: -kv[1]):
        print(f"  {t}   {n} bars")

    today = datetime.now().date()
    ahead = sorted({(c, d) for c, rows in data.items() for d, *_ in rows if d > today})
    print()
    print(f"bars stamped LATER than today ({today.isoformat()}): {len(ahead)}")
    for c, d in ahead[:10]:
        print(f"  {c}  {d.isoformat()} ({d.strftime('%A')})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
