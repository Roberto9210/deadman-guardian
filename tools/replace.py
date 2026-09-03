#!/usr/bin/env python3
"""Edit exact text in a file, from the shell, without a heredoc.

WHY IT EXISTS. On 2026-09-03 the heredoc rule was broken in THREE separate windows on the same
day, and all three reported it themselves. Three windows in one day is FRICTION, NOT CARELESSNESS:
a rule that depends on remembering is not a fix. What works is making the good path the
comfortable one. Ventana B wrote the equivalent for its repo; this is not a copy, because the
problem is the same and the environment is not.

WHAT IS DIFFERENT HERE, AND IT IS MEASURED, NOT ASSUMED:

  * `core.autocrlf` is TRUE in this repository, and the working tree is LF TODAY (checked across
    src/, nt/, tests/ and docs/ on 2026-09-03). A fresh clone therefore hands the next person
    CRLF. A tool that reads text and writes '\\n' is CORRECT TODAY AND REWRITES EVERY LINE OF
    EVERY FILE for whoever clones next - a diff of thousands of lines hiding a one-line change.
    So this works in BYTES. Line endings, BOM and encoding survive because they are never parsed.

  * 12 of the 61 tracked .cs files already contain non-ASCII bytes, so there is no repo-wide ASCII
    rule to enforce. But `Messages.cs:213` states the reason its own strings stay ASCII: "this
    file has been through patch scripts that mangle non-ASCII more than once." That is a warning
    about THIS CLASS OF TOOL. Working in bytes is what answers it; on top of that, an edit that
    puts non-ASCII into a .cs is REPORTED. A report, not a brake - the same distinction the
    guardian made today between recording an order and cancelling it.

TWO FORMS, and the second sends you to the first exactly when the shell is dangerous:

  Batch, the safe form - nothing passes through shell quoting:
      python tools/replace.py --edits edits.json
      edits.json = [{"file": "...", "old": "...", "new": "..."}, ...]

  One line, inline:
      python tools/replace.py --file src/GuardianCore/Ledger.cs --old "OLD TEXT" --new "NEW TEXT"
    Refused, with the batch command printed for you, if `old` or `new` contains a quote,
    apostrophe, backtick, backslash or newline.

THE RULES, both forms:
  * `old` must appear EXACTLY ONCE. Zero or two or more: nothing is written and it says which.
    That is this house's unique-anchor rule, enforced instead of remembered.
  * A batch is ALL OR NOTHING. Every edit is validated before any byte is written, so a batch that
    fails on the fourth edit does not leave three applied.
  * `old == new` is refused: a no-op that reports success is how you think you edited something.

Exit codes: 0 applied, 1 refused (nothing written), 2 bad usage.
"""

import argparse
import json
import os
import sys

DANGEROUS = {'"': "double quote", "'": "apostrophe", "`": "backtick",
             "\\": "backslash", "\n": "newline", "\r": "carriage return"}


def fail(message):
    print("REFUSED: " + message, file=sys.stderr)
    return 1


def plan_one(edit, index=None):
    """Validate a single edit. Returns (path, data, new_data, count) or raises ValueError."""
    where = "" if index is None else "edit %d: " % index
    for key in ("file", "old", "new"):
        if key not in edit:
            raise ValueError(where + "missing '%s'" % key)
    path, old, new = edit["file"], edit["old"], edit["new"]

    if old == new:
        raise ValueError(where + "%s: old and new are identical - that is a no-op that would "
                                 "report success" % path)
    if not os.path.isfile(path):
        raise ValueError(where + "%s: no such file" % path)

    with open(path, "rb") as fh:
        data = fh.read()

    old_b, new_b = old.encode("utf-8"), new.encode("utf-8")
    count = data.count(old_b)
    if count == 0:
        raise ValueError(where + "%s: the text does not appear. Nothing written." % path)
    if count > 1:
        raise ValueError(where + "%s: the text appears %d times and an anchor must be unique. "
                                 "Nothing written - lengthen it until it is." % (path, count))

    return path, data, data.replace(old_b, new_b, 1), old_b, new_b


def describe(path, data, old_b, new_b):
    at = data.find(old_b)
    line = data.count(b"\n", 0, at) + 1
    ascii_note = ""
    if path.endswith(".cs"):
        try:
            new_b.decode("ascii")
        except UnicodeDecodeError:
            # Reported, never refused. Messages.cs:213 records that patch scripts have mangled
            # non-ASCII in this repo before; this tool works in bytes so it cannot, but the next
            # one might not.
            ascii_note = "   [NOTE: non-ASCII written into a .cs file]"
    return "  %s:%d  -%d bytes +%d bytes%s" % (path, line, len(old_b), len(new_b), ascii_note)


def apply(edits):
    planned = []
    try:
        for i, edit in enumerate(edits, 1):
            planned.append(plan_one(edit, i if len(edits) > 1 else None))
    except ValueError as exc:
        return fail(str(exc))

    # Validated first, written second: a batch is all or nothing.
    for path, data, new_data, old_b, new_b in planned:
        with open(path, "wb") as fh:
            fh.write(new_data)
        print(describe(path, data, old_b, new_b))
    print("applied %d edit(s)" % len(planned))
    return 0


def batch_command_for(edit):
    payload = json.dumps([edit], ensure_ascii=False, indent=2)
    return ("Write the edit to a file and use the batch form:\n\n"
            "  (write edits.json)\n%s\n\n"
            "  python tools/replace.py --edits edits.json" % payload)


def main(argv=None):
    parser = argparse.ArgumentParser(description="Replace exact text in a file, in bytes.")
    parser.add_argument("--edits", help="JSON file: [{file, old, new}, ...]")
    parser.add_argument("--file")
    parser.add_argument("--old")
    parser.add_argument("--new")
    args = parser.parse_args(argv)

    if args.edits:
        if args.file or args.old is not None or args.new is not None:
            print("usage: --edits is not combined with --file/--old/--new", file=sys.stderr)
            return 2
        try:
            with open(args.edits, "r", encoding="utf-8") as fh:
                edits = json.load(fh)
        except Exception as exc:                                  # noqa: BLE001 - reported as-is
            return fail("%s: %s" % (args.edits, exc))
        if not isinstance(edits, list) or not edits:
            return fail("%s: expected a non-empty JSON list of edits" % args.edits)
        return apply(edits)

    if not (args.file and args.old is not None and args.new is not None):
        print("usage: --edits FILE   |   --file F --old TEXT --new TEXT", file=sys.stderr)
        return 2

    # The refusal fires exactly where the shell is dangerous, and hands over the safe form.
    for field, value in (("--old", args.old), ("--new", args.new)):
        for ch, name in DANGEROUS.items():
            if ch in value:
                return fail("%s contains a %s, which the shell mangles.\n\n%s"
                            % (field, name,
                               batch_command_for({"file": args.file, "old": args.old,
                                                  "new": args.new})))

    return apply([{"file": args.file, "old": args.old, "new": args.new}])


if __name__ == "__main__":
    sys.exit(main())
