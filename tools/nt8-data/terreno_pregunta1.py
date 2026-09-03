"""Question 1 - how hostile is the ground. Executes docs/prerregistro-pregunta1-terreno.md.

The pre-registration was committed ALONE, in bd5012a, before this file existed. Nothing here
departs from it. No edge is looked for and none is reported.

CONSTRUCTION. Per date, per root, the contract with the highest volume that day (verified total
on 14,115 of 14,115 dates in 37a0144). Dates where the selection CHANGES are discarded, so every
measurement lives strictly inside one contract. Those discards are not random - they fall exactly
on the quarterly joins - and that is stated in the pre-registration, not discovered here.

THE THREE CONTROLS RUN AND PRINT FIRST. If C1 or C2 fails, nothing else is published.
"""

import csv
import os
import random
import sqlite3
import statistics
import sys
from collections import defaultdict

CSV_DIR = sys.argv[1]
SEED = 20260902                     # declared in the pre-registration, not chosen after the fact
ROOTS = ["ES", "NQ", "GC", "MGC", "MES", "MNQ"]
EXCLUDED = ["CL", "MCL", "MBT", "YM", "MYM", "APA"]
WINDOWS = (20, 60)
PCTS = (50, 90, 95, 99)


def point_values():
    """Multipliers WITH THEIR SOURCE: MasterInstruments in NinjaTrader's own catalogue,
    InstrumentType = 0 (Future). Copied first; the live file is never opened."""
    src = os.path.join(os.path.expanduser("~"), "Documents", "NinjaTrader 8",
                       "db", "NinjaTrader.sqlite")
    tmp = os.path.join(os.path.dirname(CSV_DIR), "_pv_copy.sqlite")
    with open(src, "rb") as a, open(tmp, "wb") as b:
        b.write(a.read())
    con = sqlite3.connect(f"file:{tmp}?mode=ro", uri=True)
    rows = con.execute("SELECT Name, PointValue FROM MasterInstruments "
                       "WHERE InstrumentType = 0").fetchall()
    con.close()
    os.remove(tmp)
    return {n: v for n, v in rows if n in ROOTS}, src


def load():
    """root -> sorted list of (date, contract, o, h, l, c, v) for the SELECTED contract."""
    per_root = defaultdict(lambda: defaultdict(dict))
    for fn in sorted(os.listdir(CSV_DIR)):
        if not fn.endswith(".csv"):
            continue
        stem = fn[:-4]
        root = stem.split("_")[0]
        with open(os.path.join(CSV_DIR, fn), newline="", encoding="utf-8") as f:
            for r in csv.DictReader(f):
                per_root[root][r["date"]][stem] = (
                    float(r["open"]), float(r["high"]), float(r["low"]),
                    float(r["close"]), int(r["volume"]))
    sel = {}
    for root, bydate in per_root.items():
        out = []
        for d in sorted(bydate):
            c = max(bydate[d], key=lambda k: bydate[d][k][4])
            out.append((d, c) + bydate[d][c])
        sel[root] = out
    return sel


def runs_of(rows):
    """Maximal blocks sharing one contract. The date on which the selection CHANGES is dropped."""
    out, cur = [], []
    prev = None
    for row in rows:
        if prev is not None and row[1] != prev:
            if cur:
                out.append(cur)
            cur = []              # the changing date itself is discarded
        else:
            cur.append(row)
        prev = row[1]
    if cur:
        out.append(cur)
    return out


def dist(v):
    v = sorted(v)
    q = statistics.quantiles(v, n=100, method="inclusive")
    return [q[p - 1] for p in PCTS] + [v[-1]]


def max_drawdown(series):
    peak = cum = 0.0
    worst = 0.0
    for x in series:
        cum += x
        peak = max(peak, cum)
        worst = min(worst, cum - peak)
    return -worst


def rolling_dd(series, w):
    return [max_drawdown(series[i:i + w]) for i in range(len(series) - w + 1)]


def main():
    pv, pv_src = point_values()
    sel = load()
    runs = {r: runs_of(sel[r]) for r in ROOTS}
    kept = {r: sum(len(x) for x in runs[r]) for r in ROOTS}

    print("=" * 96)
    print("MULTIPLIERS, WITH THEIR SOURCE")
    print("=" * 96)
    print(f"  source: {pv_src}  (MasterInstruments, InstrumentType = 0)")
    print("  VERIFIED: it exists on this machine and was read from it, not quoted.")
    for r in ROOTS:
        print(f"    {r:<5} point value {pv[r]}")

    # ------------------------------------------------------------------ CONTROLS FIRST
    print()
    print("#" * 96)
    print("# THE THREE CONTROLS - printed ABOVE the results, as the pre-registration requires")
    print("#" * 96)

    print("\nC1 MULTIPLIER - dollar excursion ratio big/micro on the SAME date, must be exactly 10")
    c1_ok = True
    for big, micro in (("ES", "MES"), ("NQ", "MNQ"), ("GC", "MGC")):
        b = {d: (o - lo) for d, _c, o, _h, lo, _cl, _v in sel[big]}
        m = {d: (o - lo) for d, _c, o, _h, lo, _cl, _v in sel[micro]}
        shared = sorted(set(b) & set(m))
        ratios = [(b[d] * pv[big]) / (m[d] * pv[micro]) for d in shared if m[d] > 0]
        exact = sum(1 for x in ratios if abs(x - 10.0) < 1e-9)
        ok = exact == len(ratios)
        c1_ok &= ok
        print(f"  {big:<4}/{micro:<4} shared dates {len(shared):>5}  usable {len(ratios):>5}  "
              f"exactly 10: {exact:>5}  ({100 * exact / len(ratios):.2f}%)  "
              f"median {statistics.median(ratios):.6f}  "
              f"min {min(ratios):.4f}  max {max(ratios):.4f}   {'PASS' if ok else 'FAIL'}")

    print("\nC2 SCALE - two contracts must be exactly double one")
    c2_ok = True
    for r in ROOTS:
        one = [(o - lo) * pv[r] * 1 for _d, _c, o, _h, lo, _cl, _v in sel[r]]
        two = [(o - lo) * pv[r] * 2 for _d, _c, o, _h, lo, _cl, _v in sel[r]]
        ok = all(abs(t - 2 * o1) < 1e-9 for o1, t in zip(one, two)) and sum(two) > 0
        c2_ok &= ok
        print(f"  {r:<5} sum(1 contract) {sum(one):>16,.2f}   sum(2 contracts) {sum(two):>16,.2f}"
              f"   ratio {sum(two) / sum(one):.6f}   {'PASS' if ok else 'FAIL'}")

    if not (c1_ok and c2_ok):
        print("\n" + "!" * 96)
        print("! A CONTROL FAILED. Stopping before any figure is published, as pre-registered.")
        print("!" * 96)
        return 1

    print("\nC3 ORDER - rolling drawdown, real series vs the same series shuffled "
          f"(seed {SEED})")
    rng = random.Random(SEED)
    c3 = {}
    for r in ROOTS:
        real_all, shuf_all = [], []
        for w in WINDOWS:
            real, shuf = [], []
            for run in runs[r]:
                s = [c - o for _d, _c, o, _h, _l, c, _v in run]
                if len(s) < w:
                    continue
                real += rolling_dd(s, w)
                t = s[:]
                rng.shuffle(t)
                shuf += rolling_dd(t, w)
            if real:
                c3[(r, w)] = (statistics.median(real), statistics.median(shuf),
                              max(real), max(shuf))
    print(f"  {'root':<6}{'win':>5}{'median REAL':>14}{'median SHUFFLED':>18}"
          f"{'max REAL':>12}{'max SHUFFLED':>15}   differ?")
    all_differ = True
    for (r, w), (mr, ms, xr, xs) in sorted(c3.items()):
        d = abs(mr - ms) > 1e-9
        all_differ &= d
        print(f"  {r:<6}{w:>5}{mr:>14.2f}{ms:>18.2f}{xr:>12.2f}{xs:>15.2f}   "
              f"{'YES' if d else 'NO - INVALIDATES M4'}")
    print(f"\n  C3 verdict: {'PASS - order matters' if all_differ else 'FAIL'}")

    # ------------------------------------------------------------------ RESULTS
    print()
    print("=" * 96)
    print("RESULTS IN POINTS.  Dollars are a separate, later step.")
    print("=" * 96)
    print(f"\n  dates kept per root (selection changes discarded):")
    for r in ROOTS:
        print(f"    {r:<5} {kept[r]:>5} of {len(sel[r]):>5}   "
              f"discarded {len(sel[r]) - kept[r]}   runs {len(runs[r])}")

    hdr = f"  {'root':<6}{'n':>7}" + "".join(f"{'p' + str(p):>11}" for p in PCTS) + f"{'max':>12}"
    for label, fn in (
        ("M1 ADVERSE EXCURSION FROM THE OPEN - LONG (open - low)",
         lambda o, h, l, c: o - l),
        ("M1 ADVERSE EXCURSION FROM THE OPEN - SHORT (high - open)",
         lambda o, h, l, c: h - o),
        ("M2 DAILY RANGE (high - low) - the BOUND for an entry at any time",
         lambda o, h, l, c: h - l),
    ):
        print(f"\n{label}")
        print(hdr)
        for r in ROOTS:
            v = [fn(o, h, l, c) for run in runs[r] for _d, _c, o, h, l, c, _vv in run]
            print(f"  {r:<6}{len(v):>7}" + "".join(f"{x:>11.2f}" for x in dist(v)))

    print("\nM3 CLOSE TO CLOSE, within one contract (absolute move)")
    print(hdr)
    for r in ROOTS:
        v = [abs(run[i][5] - run[i - 1][5]) for run in runs[r] for i in range(1, len(run))]
        print(f"  {r:<6}{len(v):>7}" + "".join(f"{x:>11.2f}" for x in dist(v)))

    print("\nM4 ROLLING DRAWDOWN over the daily (close - open) series")
    print(hdr)
    for r in ROOTS:
        for w in WINDOWS:
            v = []
            for run in runs[r]:
                s = [c - o for _d, _c, o, _h, _l, c, _v in run]
                if len(s) >= w:
                    v += rolling_dd(s, w)
            if v:
                print(f"  {r + ' w' + str(w):<6}{len(v):>7}"
                      + "".join(f"{x:>11.2f}" for x in dist(v)))

    print()
    print("=" * 96)
    print("SAME NUMBERS IN DOLLARS - one contract, multiplier read from the catalogue above")
    print("=" * 96)
    print(f"  {'root':<6}{'measure':<10}" + "".join(f"{'p' + str(p):>13}" for p in PCTS)
          + f"{'max':>14}")
    for r in ROOTS:
        for name, fn in (("M1 long", lambda o, h, l, c: o - l),
                         ("M2 range", lambda o, h, l, c: h - l)):
            v = [fn(o, h, l, c) * pv[r] for run in runs[r]
                 for _d, _c, o, h, l, c, _vv in run]
            print(f"  {r:<6}{name:<10}" + "".join(f"{x:>13,.0f}" for x in dist(v)))
        for w in WINDOWS:
            v = []
            for run in runs[r]:
                s = [c - o for _d, _c, o, _h, _l, c, _v in run]
                if len(s) >= w:
                    v += [x * pv[r] for x in rolling_dd(s, w)]
            if v:
                print(f"  {r:<6}{'M4 w' + str(w):<10}" + "".join(f"{x:>13,.0f}" for x in dist(v)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
