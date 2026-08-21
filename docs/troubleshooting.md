# Troubleshooting

Every entry below is a failure that actually happened during development or installation, with the symptom
you would see and what it turned out to be. Nothing here is hypothetical.

---

## Nothing happens after installing

### The window never appears, and `deadman-guardian\adapter.log` does not exist

**Almost always: it was never compiled.** NinjaTrader does not compile NinjaScript at startup — not on a
restart, not because a file is new. Open `New → NinjaScript Editor`, open the AddOn from the **AddOns**
folder in the tree, press **F5**, then restart NinjaTrader.

Check `bin\Custom\NinjaTrader.Custom.dll`: if its timestamp is older than the `.cs` files in
`bin\Custom\AddOns\`, it has not been rebuilt since they were installed.

### You pressed F5 and nothing happened

The editor was probably on an empty **New tab**. `F5` there does nothing. Open an actual script first —
double-click `DeadmanGuardianAddOn` under **AddOns** — then press `F5`.

### It compiled but the window still does not appear

AddOns are instantiated when NinjaTrader **starts**. Restart it.

---

## Compile errors

### `CS0246: The type or namespace name 'GuardianCore' could not be found`

The reference is not reaching NinjaTrader's compiler. Two known causes:

1. **The `<Reference>` is in the wrong place or has a relative path.** NinjaTrader ignores a well-formed
   `<Reference>` appended in an `<ItemGroup>` of your own with `<HintPath>GuardianCore.dll</HintPath>`. It
   accepts the shape its own dialog writes: inside the `<ItemGroup>` that already holds
   `NinjaTrader.Vendor` and `WindowsBase`, with an **absolute** `HintPath`. Fix it by hand, or let the
   dialog do it: *NinjaScript Editor → right-click → References… → Add →*
   `Documents\NinjaTrader 8\bin\Custom\GuardianCore.dll`.
2. **You installed the `netstandard2.0` build instead of `net48`.** A netstandard2.0 assembly needs the
   `netstandard` facade, which NinjaTrader's compiler does not have — even though NinjaTrader will happily
   *load* the same DLL at runtime and log `Vendor assembly 'GuardianCore' … loaded`. Build with
   `dotnet build src\GuardianCore\GuardianCore.csproj -c Release` and let the installer copy the `net48`
   output.

The tell for the second one: the log line says the assembly loaded, and the compile still fails. Loading and
compiling resolve references by different rules.

### The compile fails and the platform seems fine

It is fine. A failed NinjaScript build leaves the previous assembly in place — your other scripts keep
working and the guardian simply does not exist yet. Nothing is damaged, so read the error rather than
reinstalling.

### The error list is empty but the DLL never changes

Look in `Documents\NinjaTrader 8\tmp\`. NinjaTrader compiles to a GUID-named temp file first; **0-byte**
`.dll` files there mean the compile ran and failed. The error text lives only in the editor's error pane —
NinjaTrader writes none of it to disk, so read it before closing the window.

---

## It is running but it will not arm

### "no config at …"

There is no `config.json`. The installer deliberately does not write one; copy `config.example.json` and put
your own two numbers in it. See [configure.md](configure.md).

### `'personalDailyLossLimit' … must be STRICTLY LESS than 'firmDailyLossLimit'`

Exactly what it says, including when the two are equal. The gap between them is the product.

### `unknown key '…'` and `missing key '…'` for what looks like the same field

You have a typo. A misspelled key is reported twice on purpose: once as unknown, once as missing. A key the
guardian does not recognise is a rule you think is active and is not, so it refuses rather than ignoring it.

### `unsupported time zone id`

Only `America/Chicago`, `America/New_York` and `UTC` are accepted. NinjaTrader's runtime cannot resolve IANA
names at all — the guardian carries a small translation table, and it will not guess at ids outside it.

### `account '…' is not known to the platform`

The name must match NinjaTrader's account name exactly, and the account must exist at the moment you arm.
`Sim101` is the built-in simulation account.

---

## It says NOT PROTECTED in orange

That is `FAIL_CLOSED`: the guardian does not know something it needs, so it blocks entries rather than
assuming. The reason is printed under the headline. The usual ones:

| reason | what happened |
|---|---|
| account is Disconnected | the connection dropped, or NinjaTrader is still connecting after startup |
| open position with no current price | it cannot value what you are holding, and it will not call it zero |
| sources disagree | its own P&L and NinjaTrader's differ by more than your tolerance |
| clock moved | the system clock jumped relative to real elapsed time |
| ledger is not writable | disk full, permissions, or the file is open elsewhere |

All of them clear by themselves when the underlying thing resolves — through a re-computation, never by
assumption. If one persists, the reason tells you where to look.

### It went orange right after resuming from sleep

Expected, briefly. NinjaTrader tears down and rebuilds its connections on resume, so the account is
disconnected for a few tens of seconds. Measured on a real suspend: NinjaTrader logged `Connection lost` on
both providers, warned about 36 seconds of latency, and reconnected about 40 seconds after the resume. The
guardian blocks entries throughout, which is the point.

---

## It locked me out and I think it is wrong

**There is no manual exit, by design.** Not a button, not a config key, not a restart. The lockout ends when
the seal expires at your configured session reset, and then only into `NOT PROTECTED` — you have to arm
again deliberately.

Before assuming it is wrong, read `ledger.jsonl`. Every lockout carries the number that caused it:

```
LIMIT_BREACHED  {"dayLoss":"600.00","limit":"600.00","perAccount":{...}}
```

If it says `SEAL_MISMATCH` or `CONFIG_TAMPERED`, something edited the sealed configuration or the state
file. That is treated as an attempt to trade past the limit, because that is what it is.

---

## The ledger

### Verifying it yourself

You do not need our code, and that is the point:

```python
import json, hashlib
prev = "genesis"
for n, line in enumerate(open(r"...\deadman-guardian\ledger.jsonl", encoding="utf-8"), 1):
    e = json.loads(line); h = e.pop("hash")
    canon = json.dumps(e, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    if e["prev"] != prev or hashlib.sha256(canon.encode()).hexdigest() != h:
        print("broken at line", n); break
    prev = h
else:
    print("chain OK")
```

### It says the chain is broken

Something edited the file. The guardian fails closed on a broken chain — it will not run against a record it
cannot trust. The broken sequence number tells you where.

---

## Getting your platform back

[uninstall.md](uninstall.md). One command, and a backup of NinjaTrader's project file that the installer
made before touching it.
