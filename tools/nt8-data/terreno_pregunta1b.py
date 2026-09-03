"""Question 1b - period control, and the decision table read backwards.

CONTROL FIRST, AND IT GATES THE TABLE. MES's M1 median came out 35% above ES's, and MES starts in
2019 while ES starts in 2016. Either the instruments differ or the period does. Trimming the big
roots to the micros' date range separates the two, and the criterion is DECLARED HERE BEFORE THE
RUN so it cannot be chosen to fit:

    PASS  the residual median gap between big and micro is under 5%
    FAIL  it stays at or above 10% - the two order books differ by something we do not
          understand, and NO decision table is published on top of them
    between 5% and 10%: reported as UNRESOLVED, table withheld

If the control passes, the table is inverted: today we have percentiles of excursion, and what a
decision needs is the opposite - you bring your own dollar limit and the table returns the fraction
of days that break it. NO firm's limit is invented anywhere.

    python terreno_pregunta1b.py <csv-dir>
"""

import csv
import os
import sqlite3
import statistics
import sys
from collections import defaultdict

CSV_DIR = sys.argv[1]
BIG_MICRO = (("ES", "MES"), ("NQ", "MNQ"), ("GC", "MGC"))
ROOTS = ["ES", "NQ", "GC", "MGC", "MES", "MNQ"]
PCTS = (50, 90, 95, 99)
PASS_UNDER = 5.0
FAIL_AT = 10.0


def point_values():
    src = os.path.join(os.path.expanduser("~"), "Documents", "NinjaTrader 8",
                       "db", "NinjaTrader.sqlite")
    tmp = os.path.join(os.path.dirname(CSV_DIR), "_pv2.sqlite")
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
        out = []
        for d in sorted(bydate):
            c = max(bydate[d], key=lambda k: bydate[d][k][4])
            out.append((d, c) + bydate[d][c])
        sel[root] = out
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


def pctl(v, p):
    q = statistics.quantiles(sorted(v), n=1000, method="inclusive")
    return q[int(round(p * 10)) - 1]


def main():
    pv = point_values()
    sel = load()

    # window = the micros' own date range
    micro_dates = [d for r in ("MES", "MNQ") for d, *_ in sel[r]]
    lo, hi = min(micro_dates), max(micro_dates)

    def m1(root, trim, long_side=True):
        rows = [r for r in sel[root] if (not trim or lo <= r[0] <= hi)]
        return [(o - l) if long_side else (h - o)
                for run in runs_of(rows) for _d, _c, o, h, l, _cl, _v in run]

    print("#" * 96)
    print("# CONTROL 1 - IS THE 35% GAP THE INSTRUMENT OR THE PERIOD?")
    print(f"# criterion declared before the run: PASS under {PASS_UNDER}%, "
          f"FAIL at or above {FAIL_AT}%")
    print("#" * 96)
    print(f"  common window taken from the micros: {lo} .. {hi}")
    print(f"\n  {'pair':<12}{'big med':>10}{'micro med':>12}{'gap %':>9}   verdict")
    ok = True
    for big, micro in BIG_MICRO:
        b_full = statistics.median(m1(big, False))
        b = statistics.median(m1(big, True))
        m = statistics.median(m1(micro, True))
        gap = 100 * (m - b) / b
        v = ("PASS" if abs(gap) < PASS_UNDER
             else "FAIL" if abs(gap) >= FAIL_AT else "UNRESOLVED")
        ok &= v == "PASS"
        print(f"  {big + '/' + micro:<12}{b:>10.2f}{m:>12.2f}{gap:>8.2f}%   {v}"
              f"   (big over the FULL period was {b_full:.2f})")

    print(f"\n  HOW MUCH THE REGIME MOVES EACH PERCENTILE - full run vs trimmed, same instrument")
    print(f"  {'root':<6}{'pct':>5}{'full':>11}{'trimmed':>11}{'delta':>11}{'delta %':>10}")
    for root in ("ES", "NQ", "GC", "MGC"):
        full, trim = m1(root, False), m1(root, True)
        for p in PCTS:
            a, c = pctl(full, p), pctl(trim, p)
            print(f"  {root:<6}{'p' + str(p):>5}{a:>11.2f}{c:>11.2f}"
                  f"{c - a:>11.2f}{100 * (c - a) / a:>9.1f}%")

    print(f"\n  CONTROL 1 verdict: {'PASS' if ok else 'FAIL / UNRESOLVED'}")
    if not ok:
        print("\n" + "!" * 96)
        print("! THE DECISION TABLE IS NOT PUBLISHED. A table built on two series we do not")
        print("! understand is worse than no table.")
        print("!" * 96)
        return 1

    # ---------------------------------------------------------------- inverted table
    print()
    print("=" * 96)
    print("2 - THE TABLE READ BACKWARDS: bring your own dollar limit, get the fraction of days")
    print("    Every figure is the dollar level broken on exactly that share of days/windows.")
    print("    NO firm's limit appears anywhere. Trimmed window, so all six are comparable.")
    print("=" * 96)

    def m4_w20(root):
        rows = [r for r in sel[root] if lo <= r[0] <= hi]
        out = []
        for run in runs_of(rows):
            s = [c - o for _d, _c, o, _h, _l, c, _v in run]
            for i in range(len(s) - 19):
                peak = cum = worst = 0.0
                for x in s[i:i + 20]:
                    cum += x
                    peak = max(peak, cum)
                    worst = min(worst, cum - peak)
                out.append(-worst)
        return out

    for label, shares, getter in (
        ("(a) DAILY - a limit at this dollar figure is broken on X% of DAYS (M1, long side)",
         (1, 5, 10, 25, 50), lambda r: m1(r, True, True)),
        ("(a') DAILY - same, short side (high - open)",
         (1, 5, 10, 25, 50), lambda r: m1(r, True, False)),
        ("(b) 20-DAY - a drawdown limit here is broken in X% of 20-day WINDOWS (M4)",
         (1, 5, 10, 25), m4_w20),
    ):
        print(f"\n{label}")
        print(f"  {'root':<6}{'ctr':>4}" + "".join(f"{str(s) + '% of days':>15}" for s in shares))
        for root in ROOTS:
            pts = getter(root)
            for n in (1, 2, 3):
                cells = "".join(f"{pctl(pts, 100 - s) * pv[root] * n:>15,.0f}" for s in shares)
                print(f"  {root:<6}{n:>4}" + cells)

    # ---------------------------------------------------------------- the $1,000 count
    print()
    print("=" * 96)
    print("3 - SHARE OF DAYS WITH AN ADVERSE EXCURSION ABOVE $1,000, ONE CONTRACT")
    print("    Counted exactly, not interpolated. Trimmed window.")
    print("=" * 96)
    print(f"  {'root':<6}{'pv':>7}{'days':>7}{'long >$1k':>12}{'%':>8}"
          f"{'short >$1k':>13}{'%':>8}")
    rank = []
    for root in ROOTS:
        L = m1(root, True, True)
        S = m1(root, True, False)
        nl = sum(1 for x in L if x * pv[root] > 1000)
        ns = sum(1 for x in S if x * pv[root] > 1000)
        rank.append((100 * nl / len(L), root))
        print(f"  {root:<6}{pv[root]:>7}{len(L):>7}{nl:>12}{100 * nl / len(L):>7.2f}%"
              f"{ns:>13}{100 * ns / len(S):>7.2f}%")
    print("\n  ordered by that number alone (long side):")
    for pct, root in sorted(rank):
        print(f"      {root:<5} {pct:6.2f}%")
    return 0


if __name__ == "__main__":
    sys.exit(main())
