"""M4' - the drawdown against INTRADAY equity, and the ratio to M4.

WHY. M4 accumulates (close - open), so it measures equity only at each day's close. Prop-firm
drawdowns are normally measured against INTRADAY equity, which reaches its worst point inside the
day and not at the close. So M4 is a FLOOR, and this is the ceiling.

THE FORMULA, WRITTEN OUT BEFORE THE RUN because "accumulate with the low" admits more than one
reading. For a long held continuously from the open:

    cum_i    = cum_(i-1) + (close_i - open_i)          equity at the close of day i
    trough_i = cum_(i-1) + (low_i   - open_i)          equity at day i's worst point
    peak     = running max over the CLOSE-BASED path only
    M4'      = max over the window of (peak - trough_i)

For a short the daily P&L is (open - close) and the trough uses the high:
    cum_i = cum_(i-1) + (open_i - close_i);  trough_i = cum_(i-1) + (open_i - high_i)

THE PEAK DELIBERATELY DOES NOT USE INTRADAY HIGHS. Taking the peak at an intraday high and the
trough at a later intraday low would be a larger upper bound still; that variant is not computed
here, and saying so is part of the number meaning what it says.

LIMITATION, DECLARED AND NOT SOFTENED. M4' is the bound of a CONTINUOUS HOLDING FROM THE OPEN. A
real firm's trailing drawdown is computed on the account equity with its own moving high-water
mark, which depends on when you entered and exited. M4' BOUNDS, IT DOES NOT SIMULATE.

Computed on the TRIMMED window (the micros' range) so all six roots are comparable and so M4 and
M4' are compared over identical data.

    python terreno_m4prime.py <csv-dir>
"""

import csv
import os
import sqlite3
import statistics
import sys
from collections import defaultdict

CSV_DIR = sys.argv[1]
ROOTS = ["ES", "NQ", "GC", "MGC", "MES", "MNQ"]
WINDOWS = (20, 60)


def point_values():
    src = os.path.join(os.path.expanduser("~"), "Documents", "NinjaTrader 8",
                       "db", "NinjaTrader.sqlite")
    tmp = os.path.join(os.path.dirname(CSV_DIR), "_pv3.sqlite")
    with open(src, "rb") as a, open(tmp, "wb") as b:
        b.write(a.read())
    con = sqlite3.connect(f"file:{tmp}?mode=ro", uri=True)
    rows = con.execute("SELECT Name, PointValue FROM MasterInstruments "
                       "WHERE InstrumentType = 0").fetchall()
    con.close()
    os.remove(tmp)
    return {n: v for n, v in rows if n in ROOTS}


def load():
    per = defaultdict(lambda: defaultdict(dict))
    for fn in sorted(os.listdir(CSV_DIR)):
        if not fn.endswith(".csv"):
            continue
        root = fn[:-4].split("_")[0]
        with open(os.path.join(CSV_DIR, fn), newline="", encoding="utf-8") as f:
            for r in csv.DictReader(f):
                per[root][r["date"]][fn[:-4]] = (
                    float(r["open"]), float(r["high"]), float(r["low"]),
                    float(r["close"]), int(r["volume"]))
    sel = {}
    for root, bydate in per.items():
        sel[root] = [(d, max(bydate[d], key=lambda k: bydate[d][k][4])) + bydate[d][
            max(bydate[d], key=lambda k: bydate[d][k][4])] for d in sorted(bydate)]
    return sel


def runs_of(rows):
    out, cur, prev = [], [], None
    for row in rows:
        if prev is not None and row[1] != prev:
            if cur:
                out.append(cur)
            cur = []
        else:
            cur.append(row)
        prev = row[1]
    if cur:
        out.append(cur)
    return out


def dd_close(bars):
    """M4: peak and trough both on the close-based path."""
    cum = peak = 0.0
    worst = 0.0
    for _o, _h, _l, pnl in bars:
        cum += pnl
        peak = max(peak, cum)
        worst = min(worst, cum - peak)
    return -worst


def dd_intraday(bars):
    """M4': trough at the day's adverse extreme, peak on the close-based path."""
    cum = peak = 0.0
    worst = 0.0
    for _o, _h, adverse, pnl in bars:
        peak = max(peak, cum)                 # equity entering the day
        worst = min(worst, (cum + adverse) - peak)
        cum += pnl
        peak = max(peak, cum)
        worst = min(worst, cum - peak)
    return -worst


def pctl(v, p):
    return statistics.quantiles(sorted(v), n=1000, method="inclusive")[int(round(p * 10)) - 1]


def main():
    pv = point_values()
    sel = load()
    micro = [d for r in ("MES", "MNQ") for d, *_ in sel[r]]
    lo, hi = min(micro), max(micro)

    def series(root, long_side=True):
        rows = [r for r in sel[root] if lo <= r[0] <= hi]
        out = []
        for run in runs_of(rows):
            out.append([(o, h, (l - o) if long_side else (o - h),
                         (c - o) if long_side else (o - c))
                        for _d, _c, o, h, l, c, _v in run])
        return out

    print("=" * 100)
    print("M4 vs M4' - drawdown against CLOSE equity vs against INTRADAY equity")
    print(f"trimmed window {lo} .. {hi}, so both are computed over identical data")
    print("=" * 100)
    store = {}
    for side, long_side in (("LONG", True), ("SHORT", False)):
        print(f"\n{side}")
        print(f"  {'root':<6}{'win':>4}{'n':>7}"
              + "".join(f"{h:>13}" for h in ("M4 med", "M4' med", "ratio med",
                                             "M4 p99", "M4' p99", "ratio p99")))
        for root in ROOTS:
            for w in WINDOWS:
                a, b = [], []
                for run in series(root, long_side):
                    for i in range(len(run) - w + 1):
                        a.append(dd_close(run[i:i + w]))
                        b.append(dd_intraday(run[i:i + w]))
                if not a:
                    continue
                store[(side, root, w)] = (a, b)
                ma, mb = statistics.median(a), statistics.median(b)
                qa, qb = pctl(a, 99), pctl(b, 99)
                print(f"  {root:<6}{w:>4}{len(a):>7}{ma:>13.2f}{mb:>13.2f}"
                      f"{mb / ma:>13.3f}{qa:>13.2f}{qb:>13.2f}{qb / qa:>13.3f}")

    print()
    print("=" * 100)
    print("M4' INVERTED, IN DOLLARS - a trailing limit here is broken in X% of windows")
    print("=" * 100)
    for w in WINDOWS:
        print(f"\n  window {w} days, LONG side")
        print(f"  {'root':<6}{'ctr':>4}" + "".join(f"{str(s) + '%':>14}" for s in (1, 5, 10, 25)))
        for root in ROOTS:
            if ("LONG", root, w) not in store:
                continue
            b = store[("LONG", root, w)][1]
            for n in (1, 2, 3):
                print(f"  {root:<6}{n:>4}"
                      + "".join(f"{pctl(b, 100 - s) * pv[root] * n:>14,.0f}"
                                for s in (1, 5, 10, 25)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
