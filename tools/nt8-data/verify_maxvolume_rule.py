"""Verify ONE construction rule. No market statistic, no return, no pattern.

    THE RULE: for each DATE, within each ROOT, the selected contract is the one with the
    highest volume that day.

It is a per-date rule, not a per-join rule, so it never has to know when two contracts crossed.
This checks whether it is TOTAL (always defined) and STABLE (never ambiguous, and monotone in
expiry), and how many returns a consumer would have to discard.

The only place the rule can be undefined is a TIE, so ties are counted and listed before
anything else. A row with no volume cannot compete for a maximum, so those are counted too.

    python verify_maxvolume_rule.py <csv-dir>
"""

import csv
import os
import sys
from collections import defaultdict

CSV_DIR = sys.argv[1] if len(sys.argv) > 1 else "nt8-daily-csv"


def expiry_of(stem):
    mm, yy = stem.split("_")[1].split("-")
    return 2000 + int(yy), int(mm)


def main():
    # root -> date -> {contract: volume}
    corpus = defaultdict(lambda: defaultdict(dict))
    bad_volume = defaultdict(list)          # root -> (contract, date, raw)

    for fn in sorted(os.listdir(CSV_DIR)):
        if not fn.endswith(".csv"):
            continue
        stem = fn[:-4]
        root = stem.split("_")[0]
        with open(os.path.join(CSV_DIR, fn), newline="", encoding="utf-8") as f:
            for r in csv.DictReader(f):
                raw = r.get("volume")
                if raw is None or raw == "":
                    bad_volume[root].append((stem, r["date"], "EMPTY"))
                    continue
                v = int(raw)
                if v <= 0:
                    bad_volume[root].append((stem, r["date"], str(v)))
                    if v < 0:
                        continue          # cannot compete; also a control-4 violation
                corpus[root][r["date"]][stem] = v

    roots = sorted(corpus)

    # ---------------------------------------------------------------- 2 first: bad volume
    print("=" * 92)
    print("2 - ROWS THAT CANNOT COMPETE FOR A MAXIMUM  (volume 0, empty, or negative)")
    print("=" * 92)
    total_bad = 0
    for root in roots:
        rows = bad_volume.get(root, [])
        total_bad += len(rows)
        print(f"  {root:<5} {len(rows)} row(s)")
        for c, d, raw in rows[:10]:
            print(f"        {c} {d} volume={raw}")
        if len(rows) > 10:
            print(f"        ... and {len(rows) - 10} more")
    print(f"\n  TOTAL across all roots: {total_bad}")

    # ---------------------------------------------------------------- 1 ties
    print()
    print("=" * 92)
    print("1 - IS THE RULE TOTAL?  Ties are the only place it can be undefined")
    print("=" * 92)
    selection = {}
    ties = defaultdict(list)
    for root in roots:
        for d, vols in corpus[root].items():
            if not vols:
                continue
            top = max(vols.values())
            winners = sorted(c for c, v in vols.items() if v == top)
            if len(winners) > 1:
                ties[root].append((d, winners, top))
            selection.setdefault(root, {})[d] = winners[0] if len(winners) == 1 else None
    for root in roots:
        n = len(corpus[root])
        t = ties.get(root, [])
        print(f"  {root:<5} dates {n:>5}   ties {len(t)}")
        for d, w, v in sorted(t):
            print(f"        {d}  volume={v}  ->  {', '.join(w)}")
    print(f"\n  TOTAL tied dates: {sum(len(v) for v in ties.values())}")

    # ---------------------------------------------------------------- 3 monotonic
    print()
    print("=" * 92)
    print("3 - IS THE SELECTION MONOTONE IN EXPIRY?  Every step back is listed")
    print("=" * 92)
    regressions = defaultdict(list)
    for root in roots:
        dates = sorted(selection[root])
        prev = None
        for d in dates:
            cur = selection[root][d]
            if cur is None:
                continue
            if prev is not None and expiry_of(cur) < expiry_of(prev[1]):
                regressions[root].append((d, prev[1], cur, prev[0]))
            if prev is None or expiry_of(cur) >= expiry_of(prev[1]):
                prev = (d, cur)
        print(f"  {root:<5} steps back: {len(regressions.get(root, []))}")
        for d, was, now, since in regressions.get(root, []):
            print(f"        {d}  selected {now}  after {was} (held since {since})")
    print(f"\n  TOTAL steps back: {sum(len(v) for v in regressions.values())}")

    # ---------------------------------------------------------------- 4 discarded
    print()
    print("=" * 92)
    print("4 - HOW MANY RETURNS FALL OUT  (dates whose selection differs from the day before)")
    print("=" * 92)
    print(f"  {'root':<6}{'dates':>7}{'switches':>10}{'% of dates':>13}   switch dates")
    switches = {}
    for root in roots:
        dates = sorted(selection[root])
        sw = [d for a, d in zip(dates, dates[1:])
              if selection[root][a] != selection[root][d]]
        switches[root] = sw
        pct = 100 * len(sw) / len(dates) if dates else 0
        shown = ", ".join(sw[:4]) + (" ..." if len(sw) > 4 else "")
        print(f"  {root:<6}{len(dates):>7}{len(sw):>10}{pct:>12.2f}%   {shown}")

    # ---------------------------------------------------------------- 5 cross-check
    print()
    print("=" * 92)
    print("5 - AGAINST THE EARLIER PAIRWISE MEASUREMENT (ES)")
    print("=" * 92)
    root = "ES"
    contracts = sorted({c for v in corpus[root].values() for c in v}, key=expiry_of)
    print(f"  earlier method: for each of the {len(contracts) - 1} consecutive pairs, count sign")
    print("                  changes of (new_volume > old_volume) over SHARED dates only")
    print("  this method   : argmax volume over ALL contracts alive on EVERY date")
    print()
    disagree = 0
    for a, b in zip(contracts, contracts[1:]):
        shared = sorted(set(d for d in corpus[root] if a in corpus[root][d] and b in corpus[root][d]))
        if not shared:
            continue
        newer = [corpus[root][d][b] > corpus[root][d][a] for d in shared]
        crossings = sum(1 for i in range(1, len(newer)) if newer[i] != newer[i - 1])
        # what argmax says on those same shared dates
        picks = [selection[root][d] for d in shared]
        pick_changes = sum(1 for i in range(1, len(picks)) if picks[i] != picks[i - 1])
        third = sorted({p for p in picks if p not in (a, b)})
        if crossings != pick_changes or third:
            disagree += 1
            print(f"  DISAGREE {a} -> {b}: pairwise crossings={crossings}, "
                  f"argmax changes={pick_changes}"
                  + (f", and argmax picked a THIRD contract: {', '.join(third)}" if third else ""))
    print(f"\n  pairs where the two methods disagree: {disagree}")

    # ---------------------------------------------------------------- conclusion
    print()
    print("=" * 92)
    undefined = sum(len(v) for v in ties.values())
    total_dates = sum(len(corpus[r]) for r in roots)
    print(f"DEFINED ON {total_dates - undefined} OF {total_dates} DATES.  "
          f"Undefined (tied): {undefined}")
    print("=" * 92)


if __name__ == "__main__":
    main()
