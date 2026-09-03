"""How badly would splicing ES contracts fabricate a move? MEASURING, NOT ANALYSING.

No returns, no patterns, no research question. Only: what columns exist, how much consecutive
contracts overlap, how far apart their closes are on the days they share, and whether volume
gives a mechanical way to pick the front month.

THE POINT. 252 files are individual contracts, not series. Any ten-year question needs them
glued, and the glue can manufacture the signal: two contracts trade at different prices on the
same day, so joining them untreated injects a move the market never made. This measures the size
of that artefact BEFORE any question is fixed.

Sign convention, stated once: JUMP = new_close - old_close on a shared date, and the percentage
is relative to the OLD contract's close. That is the move a naive concatenation would introduce
at the join, in the direction it would introduce it.

    python audit_splice.py <csv-dir>
"""

import csv
import os
import statistics
import sys
from collections import Counter

CSV_DIR = sys.argv[1] if len(sys.argv) > 1 else "nt8-daily-csv"
ROOT = "ES"


def expiry_of(stem):
    mm, yy = stem.split("_")[1].split("-")
    return 2000 + int(yy), int(mm)


def main():
    files = sorted(f for f in os.listdir(CSV_DIR) if f.endswith(".csv"))

    # ---------------------------------------------------------------- 1: columns
    print("=" * 92)
    print("1 - WHAT IS ACTUALLY IN THE FILES")
    print("=" * 92)
    headers = Counter()
    for f in files:
        with open(os.path.join(CSV_DIR, f), newline="", encoding="utf-8") as fh:
            headers[",".join(next(csv.reader(fh)))] += 1
    for h, n in headers.most_common():
        print(f"  {n} of {len(files)} files:  {h}")
    cols = list(headers)[0].split(",")
    print(f"\n  distinct headers among the 252: {len(headers)}")
    print(f"  VOLUME column present        : {'volume' in cols}")
    print(f"  OPEN INTEREST column present : "
          f"{any('open_interest' == c or 'openinterest' == c.replace('_', '') for c in cols)}")

    # ---------------------------------------------------------------- load ES
    data = {}
    for f in files:
        stem = f[:-4]
        if stem.split("_")[0] != ROOT:
            continue
        with open(os.path.join(CSV_DIR, f), newline="", encoding="utf-8") as fh:
            rows = list(csv.DictReader(fh))
        data[stem] = {r["date"]: (float(r["close"]), int(r["volume"])) for r in rows}

    order = sorted(data, key=expiry_of)
    print(f"\n  {ROOT}: {len(order)} contracts, {len(order) - 1} consecutive pairs")

    # ---------------------------------------------------------------- 2 & 3
    print()
    print("=" * 92)
    print("2 + 3 - OVERLAP AND THE JUMP AT THE JOIN   (jump = new close - old close)")
    print("=" * 92)
    print(f"  {'old':<10}{'new':<10}{'shared':>8}{'min pts':>10}{'med pts':>10}{'max pts':>10}"
          f"{'med %':>9}{'max |%|':>9}")
    all_pts, all_pct, zero_overlap = [], [], []
    per_pair = []
    for a, b in zip(order, order[1:]):
        shared = sorted(set(data[a]) & set(data[b]))
        if not shared:
            zero_overlap.append((a, b))
            print(f"  {a:<10}{b:<10}{0:>8}{'-':>10}{'-':>10}{'-':>10}{'-':>9}{'-':>9}")
            continue
        pts = [data[b][d][0] - data[a][d][0] for d in shared]
        pct = [100 * (data[b][d][0] - data[a][d][0]) / data[a][d][0] for d in shared]
        all_pts += pts
        all_pct += pct
        per_pair.append((a, b, len(shared), statistics.median(pts), statistics.median(pct)))
        print(f"  {a:<10}{b:<10}{len(shared):>8}{min(pts):>10.2f}"
              f"{statistics.median(pts):>10.2f}{max(pts):>10.2f}"
              f"{statistics.median(pct):>9.3f}{max(abs(p) for p in pct):>9.3f}")

    print()
    print(f"  pairs with ZERO shared dates: {len(zero_overlap)}")
    for a, b in zero_overlap:
        print(f"      {a} -> {b}")
    print(f"\n  ACROSS ALL {len(all_pts)} SHARED-DATE OBSERVATIONS:")
    for label, vals, unit in (("points", all_pts, "pts"), ("percent", all_pct, "%")):
        print(f"    {label:<8} min {min(vals):>10.3f}  median {statistics.median(vals):>10.3f}"
              f"  max {max(vals):>10.3f}   (largest absolute: {max(abs(v) for v in vals):.3f} {unit})")

    # ---------------------------------------------------------------- 4
    print()
    print("=" * 92)
    print("4 - CAN THE FRONT MONTH BE PICKED BY VOLUME?")
    print("=" * 92)
    if "volume" not in cols:
        print("  NO VOLUME COLUMN - NOT DETERMINED. No calendar rule is substituted.")
        return
    print(f"  {'old':<10}{'new':<10}{'shared':>8}{'first new>old':>15}{'crossings':>11}  clean?")
    clean = oscillating = nodata = 0
    for a, b in zip(order, order[1:]):
        shared = sorted(set(data[a]) & set(data[b]))
        if not shared:
            print(f"  {a:<10}{b:<10}{0:>8}{'-':>15}{'-':>11}  no shared dates")
            nodata += 1
            continue
        newer = [data[b][d][1] > data[a][d][1] for d in shared]
        first = next((shared[i] for i, v in enumerate(newer) if v), None)
        crossings = sum(1 for i in range(1, len(newer)) if newer[i] != newer[i - 1])
        if first is None:
            verdict = "NEVER crosses inside the overlap"
            nodata += 1
        elif crossings <= 1:
            verdict = "clean: crosses once and stays"
            clean += 1
        else:
            verdict = f"OSCILLATES ({crossings} changes)"
            oscillating += 1
        print(f"  {a:<10}{b:<10}{len(shared):>8}{(first or '-'):>15}{crossings:>11}  {verdict}")
    print(f"\n  clean crossings: {clean}   oscillating: {oscillating}   "
          f"never/no-overlap: {nodata}")


if __name__ == "__main__":
    main()
