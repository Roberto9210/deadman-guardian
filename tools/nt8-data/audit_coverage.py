"""Coverage audit of the daily CSVs. MEASURING THE POPULATION, NOT USING IT.

No price is read. Only dates, counts and presence.

WHY THIS EXISTS. The NinjaTrader directory is not a dataset: it is the record of what we happened
to open, to whatever depth that chart asked for on that day. The CSVs inherit exactly that. Before
any research question is fixed, the population it would be fixed over has to be known.

THE DEFINITION OF A HOLE, and it is the whole method. A date missing from EVERY contract is a
market closure and is not a finding. A date present in some and missing in others is a hole. But
"others" has to be restricted to contracts that were ALIVE on that date - ES 12-16 and MNQ 09-26
never coexist, and comparing them would manufacture ten years of false holes. So:

    for a contract with span [first, last]:
        expected = every date in the corpus that falls inside [first, last]
        hole     = an expected date this contract does not have

A file that cannot be read is NAMED, never skipped: an unreadable file is not an instrument
without data.

    python audit_coverage.py <csv-dir>
"""

import csv
import os
import sys
from collections import defaultdict
from datetime import date, timedelta

CSV_DIR = sys.argv[1] if len(sys.argv) > 1 else "nt8-daily-csv"


def weekdays_between(a, b):
    n, d = 0, a
    while d <= b:
        if d.weekday() < 5:
            n += 1
        d += timedelta(days=1)
    return n


def load():
    good, bad, empty = {}, [], []
    for fn in sorted(os.listdir(CSV_DIR)):
        if not fn.endswith(".csv"):
            continue
        path = os.path.join(CSV_DIR, fn)
        name = fn[:-4].replace("_", " ")
        try:
            with open(path, newline="", encoding="utf-8") as f:
                rows = list(csv.DictReader(f))
        except Exception as exc:
            bad.append((name, f"unreadable: {exc}"))
            continue
        try:
            dates = sorted({date.fromisoformat(r["date"]) for r in rows})
        except (KeyError, ValueError, TypeError) as exc:
            bad.append((name, f"malformed rows: {exc}"))
            continue
        if not dates:
            empty.append(name)
            continue
        good[name] = dates
    return good, bad, empty


def main():
    good, bad, empty = load()

    print("=" * 100)
    print("FILES THAT COULD NOT BE USED  (named, not skipped)")
    print("=" * 100)
    print(f"  unreadable or malformed : {len(bad)}")
    for n, why in bad:
        print(f"      {n}: {why}")
    print(f"  readable but EMPTY      : {len(empty)}")
    for n in empty:
        print(f"      {n}")
    print(f"  usable                  : {len(good)}")

    corpus = sorted({d for ds in good.values() for d in ds})
    print(f"\n  corpus: {len(corpus)} distinct dates, {corpus[0]} .. {corpus[-1]}")

    # -------------------------------------------------- step 1
    print()
    print("=" * 100)
    print("STEP 1 - PER CONTRACT: span, rows, calendar weekdays in span")
    print("=" * 100)
    print(f"  {'contract':<14}{'first':<12}{'last':<12}{'rows':>6}{'weekdays':>10}{'rows/wd':>9}")
    for name in sorted(good):
        ds = good[name]
        wd = weekdays_between(ds[0], ds[-1])
        print(f"  {name:<14}{ds[0].isoformat():<12}{ds[-1].isoformat():<12}"
              f"{len(ds):>6}{wd:>10}{len(ds) / wd:>9.2f}")

    # -------------------------------------------------- step 2 / 3
    holes = {}
    expected_n = {}
    for name, ds in good.items():
        first, last = ds[0], ds[-1]
        have = set(ds)
        exp = [d for d in corpus if first <= d <= last]
        expected_n[name] = len(exp)
        holes[name] = [d for d in exp if d not in have]

    # dates present in NO contract at all, but a weekday inside the corpus span
    all_present = set(corpus)
    d, never = corpus[0], []
    while d <= corpus[-1]:
        if d.weekday() < 5 and d not in all_present:
            never.append(d)
        d += timedelta(days=1)

    print()
    print("=" * 100)
    print("STEP 2 - HOLES, BY CONTRACT  (a date inside this contract's own span that other")
    print("         contracts alive on that date do have, and this one does not)")
    print("=" * 100)
    withholes = {k: v for k, v in holes.items() if v}
    print(f"  contracts WITH holes: {len(withholes)} of {len(good)}\n")
    for name in sorted(withholes):
        hs = withholes[name]
        print(f"  {name}  ({len(hs)} hole(s))")
        line = "      "
        for h in hs:
            piece = f"{h.isoformat()}({h.strftime('%a')}) "
            if len(line) + len(piece) > 98:
                print(line)
                line = "      "
            line += piece
        print(line)

    print()
    print(f"  WEEKDAYS MISSING FROM EVERY CONTRACT (market closure, NOT a finding): {len(never)}")
    print("  no holiday calendar is used or invented; these are simply absent everywhere")
    line = "      "
    for n in never:
        piece = f"{n.isoformat()} "
        if len(line) + len(piece) > 98:
            print(line)
            line = "      "
        line += piece
    if line.strip():
        print(line)

    # -------------------------------------------------- step 3
    print()
    print("=" * 100)
    print("STEP 3 - THREE NUMBERS PER CONTRACT, INCLUDING THE ZEROS")
    print("=" * 100)
    print(f"  {'contract':<14}{'holes':>7}{'longest run':>13}{'% of peers'' dates missing':>28}")
    for name in sorted(good):
        hs = holes[name]
        longest = 0
        if hs:
            run, prev = 1, hs[0]
            longest = 1
            for h in hs[1:]:
                idx_prev = corpus.index(prev)
                run = run + 1 if corpus[idx_prev + 1] == h else 1
                longest = max(longest, run)
                prev = h
        pct = 100 * len(hs) / expected_n[name] if expected_n[name] else 0.0
        print(f"  {name:<14}{len(hs):>7}{longest:>13}{pct:>27.2f}%")

    # -------------------------------------------------- step 4
    print()
    print("=" * 100)
    print("STEP 4 - REAL START DATE, and which contracts share one")
    print("=" * 100)
    byfirst = defaultdict(list)
    for name, ds in good.items():
        byfirst[ds[0]].append(name)
    print("  contracts sharing EXACTLY the same first date (2 or more):")
    shared = 0
    for d in sorted(byfirst):
        if len(byfirst[d]) > 1:
            shared += len(byfirst[d])
            print(f"      {d.isoformat()}  x{len(byfirst[d])}: {', '.join(sorted(byfirst[d]))}")
    print(f"\n  contracts in a shared-start group : {shared}")
    print(f"  contracts with a unique first date: {len(good) - shared}")


if __name__ == "__main__":
    main()
