"""Bars that are PRESENT but hollow. Extends the coverage audit sideways.

WHY. The coverage audit looked for ABSENT dates. A date with two trades passes it without
flinching: it is present, it splices, it has no hole. Ventana D found five daily bars with a volume
of 2 or 3, three of them in 2020+, in ES, NQ and MNQ, plus a New Year ghost bar. Absence was
audited; garbage-in-presence was not.

THE CRITERIA, DECLARED BEFORE THE RUN AND NOT TUNED AFTERWARDS. Two, because they answer different
questions and neither alone is honest:

  RELATIVE (primary, and the one the question asked for): volume < 1 % of that root's own MEDIAN
  daily volume. Purely a statement about the root's own distribution, so a quiet instrument is not
  punished for being quiet.

  ABSOLUTE (secondary, reported beside it): volume <= 10 contracts. This is the "obviously
  truncated" band and it is what names the bars D found. It is reported rather than used to decide,
  because an absolute threshold is a judgement about markets and the relative one is not.

A bar failing EITHER is listed. Nothing is removed from anything: this measures, and then measures
what would change if the flagged bars were dropped.

    python audit_truncated_bars.py <csv-dir>
"""

import csv
import os
import statistics
import sys
from collections import defaultdict

CSV_DIR = sys.argv[1]
ROOTS = ["ES", "NQ", "GC", "MGC", "MES", "MNQ"]
RELATIVE_FRACTION = 0.01
ABSOLUTE_FLOOR = 10
PCTS = (50, 90, 95, 99)


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
    return statistics.quantiles(sorted(v), n=1000, method="inclusive")[int(round(p * 10)) - 1]


def main():
    sel = load()
    micro = [d for r in ("MES", "MNQ") for d, *_ in sel[r]]
    lo, hi = min(micro), max(micro)

    print("=" * 104)
    print("CRITERIA, DECLARED BEFORE THE RUN")
    print(f"  RELATIVE (primary): volume < {RELATIVE_FRACTION:.0%} of the root's own median volume")
    print(f"  ABSOLUTE (reported): volume <= {ABSOLUTE_FLOOR} contracts")
    print("=" * 104)

    flagged = {}
    print(f"\n  {'root':<6}{'bars':>7}{'median vol':>13}{'threshold':>12}"
          f"{'flagged rel':>13}{'flagged abs':>13}{'% of bars':>11}")
    for root in ROOTS:
        rows = sel[root]
        vols = [v for _d, _c, _o, _h, _l, _cl, v in rows]
        med = statistics.median(vols)
        thr = med * RELATIVE_FRACTION
        rel = [r for r in rows if r[6] < thr]
        ab = [r for r in rows if r[6] <= ABSOLUTE_FLOOR]
        flagged[root] = sorted(set(rel) | set(ab))
        print(f"  {root:<6}{len(rows):>7}{med:>13,.0f}{thr:>12,.0f}"
              f"{len(rel):>13}{len(ab):>13}{100 * len(flagged[root]) / len(rows):>10.2f}%")

    print("\n" + "=" * 104)
    print("EVERY FLAGGED BAR, NAMED")
    print("=" * 104)
    total = 0
    for root in ROOTS:
        if not flagged[root]:
            print(f"\n  {root}: none")
            continue
        print(f"\n  {root}: {len(flagged[root])}")
        for d, c, o, h, l, cl, v in flagged[root]:
            total += 1
            inq1 = lo <= d <= hi
            print(f"      {d}  {c:<10} vol {v:>9,}  range {h - l:>9.2f}  "
                  f"O {o:<10.2f} C {cl:<10.2f}  {'IN question 1' if inq1 else 'outside window'}")
    print(f"\n  TOTAL flagged: {total}")

    # ---------------------------------------------------------------- effect on question 1
    print("\n" + "=" * 104)
    print("WHAT CHANGES IF THEY ARE DROPPED  (trimmed window, same construction as question 1)")
    print("=" * 104)
    print(f"  {'root':<6}{'measure':<10}{'pct':>5}{'WITH':>12}{'WITHOUT':>12}{'delta':>12}{'delta %':>10}")
    moved = 0
    for root in ROOTS:
        bad = {r[0] for r in flagged[root]}
        rows = [r for r in sel[root] if lo <= r[0] <= hi]
        keep = [r for r in rows if r[0] not in bad]
        for name, fn in (("M1 long", lambda o, h, l, c: o - l),
                         ("M1 short", lambda o, h, l, c: h - o),
                         ("M2 range", lambda o, h, l, c: h - l)):
            a = [fn(o, h, l, c) for run in runs_of(rows) for _d, _c, o, h, l, c, _v in run]
            b = [fn(o, h, l, c) for run in runs_of(keep) for _d, _c, o, h, l, c, _v in run]
            for p in PCTS:
                x, y = pctl(a, p), pctl(b, p)
                if abs(x - y) > 1e-9:
                    moved += 1
                    print(f"  {root:<6}{name:<10}{'p' + str(p):>5}{x:>12.2f}{y:>12.2f}"
                          f"{y - x:>12.2f}{100 * (y - x) / x:>9.2f}%")
        a3 = [abs(run[i][5] - run[i - 1][5])
              for run in runs_of(rows) for i in range(1, len(run))]
        b3 = [abs(run[i][5] - run[i - 1][5])
              for run in runs_of(keep) for i in range(1, len(run))]
        for p in PCTS:
            x, y = pctl(a3, p), pctl(b3, p)
            if abs(x - y) > 1e-9:
                moved += 1
                print(f"  {root:<6}{'M3':<10}{'p' + str(p):>5}{x:>12.2f}{y:>12.2f}"
                      f"{y - x:>12.2f}{100 * (y - x) / x:>9.2f}%")
    if moved == 0:
        print("  NO PERCENTILE MOVES AT ALL.")
    else:
        print(f"\n  percentiles that moved: {moved}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
