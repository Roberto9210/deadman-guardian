#!/usr/bin/env python3
"""Tests for tools/replace.py.   Run:  python tools/test_replace.py

THE PROPERTY THAT MATTERS is not "the new text is in the file". It is THE EDIT LANDS EXACTLY AND
NOTHING ELSE CHANGES. So every test asserts the file's remaining bytes are identical, not that it
"looks right" - because the failure this tool exists to prevent is a one-line change arriving as a
diff of thousands of lines with the line endings rewritten.

THE RULE APPLIED BEFORE WRITING IT - is there a change cheaper than the real fix that turns a red
green? No. The assertions are byte equality of everything outside the edit, so there is no
formatting shortcut, no normalisation, and no "close enough".
"""

import io
import json
import os
import shutil
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import replace  # noqa: E402

FAILURES = []
PASSES = [0]


def check(name, condition, detail=""):
    if condition:
        PASSES[0] += 1
    else:
        FAILURES.append(name + ("  " + detail if detail else ""))


def run(argv):
    """Run the tool, capturing output. Returns (exit_code, stdout, stderr)."""
    out, err = io.StringIO(), io.StringIO()
    real_out, real_err = sys.stdout, sys.stderr
    sys.stdout, sys.stderr = out, err
    try:
        code = replace.main(argv)
    finally:
        sys.stdout, sys.stderr = real_out, real_err
    return code, out.getvalue(), err.getvalue()


def write(path, data):
    with open(path, "wb") as fh:
        fh.write(data)


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def main():
    tmp = tempfile.mkdtemp(prefix="replace-test-")
    try:
        # ---- 1. exact-once replaces, and nothing else moves -------------------------------------
        p = os.path.join(tmp, "one.txt")
        before = b"alpha\nBETA\ngamma\n"
        write(p, before)
        code, _, _ = run(["--file", p, "--old", "BETA", "--new", "DELTA"])
        after = read(p)
        check("1 exact-once applies", code == 0 and after == b"alpha\nDELTA\ngamma\n", repr(after))

        # ---- 2. zero occurrences refuses and writes nothing --------------------------------------
        write(p, before)
        code, _, err = run(["--file", p, "--old", "NOPE", "--new", "X"])
        check("2 zero occurrences refuses", code == 1 and "does not appear" in err)
        check("2 zero occurrences writes nothing", read(p) == before)

        # ---- 3. two occurrences refuses - the unique-anchor rule ---------------------------------
        twice = b"x\nSAME\ny\nSAME\nz\n"
        write(p, twice)
        code, _, err = run(["--file", p, "--old", "SAME", "--new", "OTHER"])
        check("3 two occurrences refuses", code == 1 and "appears 2 times" in err, err.strip())
        check("3 two occurrences writes nothing", read(p) == twice)

        # ---- 4. CRLF SURVIVES, and so does a lone LF in the same file ----------------------------
        # The measured reason this tool works in bytes: core.autocrlf is true here, the tree is LF
        # today, and a fresh clone is CRLF. A text-mode tool passes this repo today and rewrites
        # every line for the next person.
        mixed = b"first\r\nTARGET\r\nthird\r\nragged\n"
        pc = os.path.join(tmp, "crlf.txt")
        write(pc, mixed)
        code, _, _ = run(["--file", pc, "--old", "TARGET", "--new", "HIT"])
        got = read(pc)
        check("4 CRLF preserved", code == 0 and got == b"first\r\nHIT\r\nthird\r\nragged\n", repr(got))
        check("4 no line ending was normalised", got.count(b"\r\n") == mixed.count(b"\r\n"))

        # ---- 5. a UTF-8 BOM is untouched ----------------------------------------------------------
        pb = os.path.join(tmp, "bom.txt")
        write(pb, b"\xef\xbb\xbfhead\nOLD\ntail\n")
        code, _, _ = run(["--file", pb, "--old", "OLD", "--new", "NEW"])
        got = read(pb)
        check("5 BOM preserved", code == 0 and got.startswith(b"\xef\xbb\xbf") and b"NEW" in got, repr(got[:12]))

        # ---- 6. accents survive both sides (the docs here are Spanish) ---------------------------
        pa = os.path.join(tmp, "acentos.md")
        write(pa, "una anotación\nel guardián NO frena\nfin\n".encode("utf-8"))
        code, _, _ = run(["--file", pa, "--old", "NO frena", "--new", "sólo registra"])
        got = read(pa).decode("utf-8")
        check("6 accents survive", code == 0 and "el guardián sólo registra" in got and
              "anotación" in got, got)

        # ---- 7. dangerous characters refuse AND hand over the batch form -------------------------
        for text, label in (('say "hi"', "quote"), ("it's", "apostrophe"),
                            ("`cmd`", "backtick"), ("a\\b", "backslash"), ("a\nb", "newline")):
            write(p, before)
            code, _, err = run(["--file", p, "--old", "BETA", "--new", text])
            check("7 %s refused" % label, code == 1, err.strip()[:80])
            check("7 %s names the batch form" % label, "--edits" in err)
            check("7 %s writes nothing" % label, read(p) == before)

        # ---- 8. a batch is ALL OR NOTHING ---------------------------------------------------------
        a = os.path.join(tmp, "a.txt")
        b = os.path.join(tmp, "b.txt")
        write(a, b"one AAA one\n")
        write(b, b"two BBB two\n")
        edits = os.path.join(tmp, "edits.json")
        with open(edits, "w", encoding="utf-8") as fh:
            json.dump([{"file": a, "old": "AAA", "new": "111"},
                       {"file": b, "old": "MISSING", "new": "222"}], fh)
        code, _, err = run(["--edits", edits])
        check("8 bad batch refuses", code == 1, err.strip()[:80])
        check("8 bad batch leaves file 1 untouched", read(a) == b"one AAA one\n")
        check("8 bad batch leaves file 2 untouched", read(b) == b"two BBB two\n")

        with open(edits, "w", encoding="utf-8") as fh:
            json.dump([{"file": a, "old": "AAA", "new": '1"1'},
                       {"file": b, "old": "BBB", "new": "2`2"}], fh)
        code, out, _ = run(["--edits", edits])
        check("8 good batch applies both", code == 0, out.strip()[:80])
        check("8 batch takes characters the inline form refuses",
              read(a) == b'one 1"1 one\n' and read(b) == b"two 2`2 two\n")

        # ---- 9. a no-op is refused, not reported as success ---------------------------------------
        write(p, before)
        code, _, err = run(["--file", p, "--old", "BETA", "--new", "BETA"])
        check("9 no-op refused", code == 1 and "no-op" in err, err.strip()[:80])

        # ---- 10. a missing file is refused, not created -------------------------------------------
        ghost = os.path.join(tmp, "ghost.txt")
        code, _, err = run(["--file", ghost, "--old", "a", "--new", "b"])
        check("10 missing file refused", code == 1 and "no such file" in err)
        check("10 missing file not created", not os.path.exists(ghost))

        # ---- 11. non-ASCII into a .cs is REPORTED, never refused ----------------------------------
        pcs = os.path.join(tmp, "Sample.cs")
        write(pcs, b"// head\nvar s = \"plain\";\n")
        code, out, _ = run(["--file", pcs, "--old", "plain", "--new", "café"])
        check("11 non-ASCII into .cs applies", code == 0 and "café".encode("utf-8") in read(pcs))
        check("11 non-ASCII into .cs is reported", "non-ASCII" in out, out.strip()[:80])

    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    print("passed %d" % PASSES[0])
    for f in FAILURES:
        print("FAILED: " + f)
    print("FAILED %d" % len(FAILURES) if FAILURES else "all green")
    return 1 if FAILURES else 0


if __name__ == "__main__":
    sys.exit(main())
