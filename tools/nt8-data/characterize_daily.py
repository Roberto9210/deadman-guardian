"""Characterise the daily CSVs produced by extract_daily_ncd.py. COUNTING AND COVERAGE ONLY.

No statistics beyond counting and covering ranges, and no conclusions: the question comes
after knowing what is there, not before. Deciding it now would mean inventing a question to
fit the data we happen to have, which is backwards.

Reads the CSVs rather than the .ncd files on purpose - the numbers below are reproducible from
the extracted artefact, and the extractor is the record of how that artefact was produced.

    python characterize_daily.py <csv-dir>
"""

import csv
import os
import sys
from collections import defaultdict
from datetime import date, timedelta

CSV_DIR = sys.argv[1] if len(sys.argv) > 1 else "nt8-daily-csv"


def expiry_of(contract):
    """'ES 12-16' -> (2016, 12). The stored contracts all expire 2016-2026."""
    mm, yy = contract.split("_")[1].split("-")
    return 2000 + int(yy), int(mm)


def weekdays_between(a, b):
    d, out = a, []
    while d <= b:
        if d.weekday() < 5:
            out.append(d)
        d += timedelta(days=1)
    return out


def main():
    per_contract = {}
    for fn in sorted(os.listdir(CSV_DIR)):
        if not fn.endswith(".csv"):
            continue
        with open(os.path.join(CSV_DIR, fn), newline="", encoding="utf-8") as f:
            rows = list(csv.DictReader(f))
        dates = sorted(date.fromisoformat(r["date"]) for r in rows)
        per_contract[fn[:-4]] = dates

    by_root = defaultdict(list)
    for c, dates in per_contract.items():
        by_root[c.split("_")[0]].append(c)

    print("=" * 92)
    print("1 - PER ROOT SYMBOL")
    print("=" * 92)
    print(f"{'root':<6}{'contracts':>10}{'bars':>8}   {'first bar':<12}{'last bar':<12}"
          f"{'distinct days':>14}{'bars/contract':>15}")
    for root in sorted(by_root):
        cs = by_root[root]
        alld = sorted({d for c in cs for d in per_contract[c]})
        n = sum(len(per_contract[c]) for c in cs)
        print(f"{root:<6}{len(cs):>10}{n:>8}   {alld[0].isoformat():<12}{alld[-1].isoformat():<12}"
              f"{len(alld):>14}{n / len(cs):>15.1f}")

    print()
    print("=" * 92)
    print("2 - DO THE PER-CONTRACT SEGMENTS SPLICE?  (gap > 0 = hole, gap < 0 = overlap)")
    print("=" * 92)
    for root in sorted(by_root):
        cs = sorted(by_root[root], key=expiry_of)
        if len(cs) < 2:
            print(f"\n{root}: only {len(cs)} contract, nothing to splice")
            continue
        gaps, overlaps, abut = [], [], 0
        worst = []
        for a, b in zip(cs, cs[1:]):
            la, fb = per_contract[a][-1], per_contract[b][0]
            missing = [d for d in weekdays_between(la + timedelta(days=1),
                                                   fb - timedelta(days=1))]
            if fb <= la:
                overlaps.append((a, b, (la - fb).days + 1))
            elif missing:
                gaps.append((a, b, len(missing)))
                worst.append((len(missing), a, b, la, fb))
            else:
                abut += 1
        print(f"\n{root}:  {len(cs)} contracts, {len(cs) - 1} joins  ->  "
              f"clean {abut}   holes {len(gaps)}   overlaps {len(overlaps)}")
        for n, a, b, la, fb in sorted(worst, reverse=True)[:3]:
            print(f"      biggest hole: {a} ends {la}  ->  {b} starts {fb}   "
                  f"{n} weekday(s) uncovered")
        for a, b, n in sorted(overlaps, key=lambda t: -t[2])[:3]:
            print(f"      overlap: {a} and {b} share {n} calendar day(s)")

    print()
    print("=" * 92)
    print("3 - MISSING WEEKDAYS INSIDE THE UNION OF EACH ROOT'S COVERAGE")
    print("    A plain Mon-Fri calendar: exchange holidays COUNT AS MISSING here and are")
    print("    expected. This measures holes, it does not judge them.")
    print("=" * 92)
    print(f"{'root':<6}{'span first':<12}{'span last':<12}{'weekdays':>10}{'present':>9}"
          f"{'missing':>9}{'missing %':>11}")
    for root in sorted(by_root):
        alld = sorted({d for c in by_root[root] for d in per_contract[c]})
        wd = weekdays_between(alld[0], alld[-1])
        present = set(alld)
        miss = [d for d in wd if d not in present]
        print(f"{root:<6}{alld[0].isoformat():<12}{alld[-1].isoformat():<12}"
              f"{len(wd):>10}{len(wd) - len(miss):>9}{len(miss):>9}{100 * len(miss) / len(wd):>10.1f}%")

    print()
    print("=" * 92)
    print("4 - BARS ON A NON-WEEKDAY (would mean the date conversion is off)")
    print("=" * 92)
    weekend = [(c, d) for c, ds in per_contract.items() for d in ds if d.weekday() >= 5]
    print(f"  {len(weekend)} bars fall on a Saturday or Sunday")
    for c, d in weekend[:5]:
        print(f"      {c} {d} ({d.strftime('%A')})")


if __name__ == "__main__":
    main()
