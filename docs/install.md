# Installing deadman-guardian

Written from the install that actually happened on 2026-08-20/21, including the two things that failed
first. If a step here looks over-explained, it is because that step is where it broke.

**Requirements**: NinjaTrader 8 (built against 8.1.8.2), Windows, and the .NET SDK 8 if you want to build
`GuardianCore` yourself instead of using a release build.

---

## The short version

```powershell
# 1. build the library NinjaTrader will reference
dotnet build src\GuardianCore\GuardianCore.csproj -c Release

# 2. close NinjaTrader, then install
.\nt\install.ps1

# 3. start NinjaTrader, open New -> NinjaScript Editor, open the AddOn, press F5

# 4. restart NinjaTrader
```

Then write your `config.json` — see [configure.md](configure.md) — and press **Arm**.

If anything goes wrong, [troubleshooting.md](troubleshooting.md) has the failures we hit, with their
symptoms. [uninstall.md](uninstall.md) puts your platform back.

---

## The long version, and why each step is there

### 1. Build `GuardianCore` for **net48**

```powershell
dotnet build src\GuardianCore\GuardianCore.csproj -c Release
```

The project multi-targets `netstandard2.0;net48`. The installer copies the **net48** build, and that is not
a preference.

> A `netstandard2.0` assembly references the `netstandard` facade. That facade is **not** in NinjaTrader's
> `bin`, not in `bin\Custom`, and not in the .NET Framework reference assemblies — it lives only under the
> runtime directory. So a netstandard2.0 library **loads** fine inside NinjaTrader (the CLR resolves the
> facade at run time) and **fails to compile against**, with `CS0246`. Our first install died exactly there,
> after NinjaTrader had cheerfully logged `Vendor assembly 'GuardianCore' version='1.0.0.0' loaded`.

The net48 build's entire reference list is `mscorlib` and `System.Core`. Nothing to resolve.

### 2. Close NinjaTrader

The installer refuses to run while it is open, because the files are locked. Close it from its own menu
rather than killing it: a forced kill leaves NinjaTrader showing its logon window on the next start, and
you will spend ten minutes wondering why nothing compiles.

### 3. Run the installer

```powershell
.\nt\install.ps1              # the guardian
.\nt\install.ps1 -WithSoak    # ...plus the soak suite, if you are testing rather than trading
```

It copies `GuardianPorts.cs` and `DeadmanGuardianAddOn.cs` into
`Documents\NinjaTrader 8\bin\Custom\AddOns\`, copies `GuardianCore.dll` into `bin\Custom\`, and edits
`NinjaTrader.Custom.csproj`. It backs that project file up first, next to itself.

**Two details in that edit, both learned the hard way:**

- The `<Compile Include>` entries are required. NinjaTrader compiles what its project file lists; dropping a
  `.cs` into the folder is not enough.
- The `<Reference>` to `GuardianCore` must be **inside the `<ItemGroup>` NinjaTrader already manages** (the
  one holding `NinjaTrader.Vendor`, `WindowsBase`, …) and its `HintPath` must be **absolute**. A well-formed
  `<Reference>` appended in an `<ItemGroup>` of your own, with a relative `HintPath`, is silently ignored —
  that was our second failed install. The installer now writes the exact shape NinjaTrader's own
  **References** dialog writes.

If the installer cannot find that ItemGroup it says so and tells you to add the DLL through the dialog:
*NinjaScript Editor → right-click → References… → Add →*
`Documents\NinjaTrader 8\bin\Custom\GuardianCore.dll`.

### 4. Compile: `F5` in the NinjaScript Editor

> ## ⚠ NEVER ARM BETWEEN OPENING NINJATRADER AND PRESSING `F5`
>
> **The deploy lands in two stages, and there is a window between them.** `install.ps1` copies
> `GuardianCore.dll`, which NinjaTrader loads **at startup**; the addon lives inside
> `NinjaTrader.Custom.dll` and is only rebuilt by **`F5`**. Between those two moments the platform is
> running a **new Core against the previously compiled addon** — a pairing that **corresponds to no
> commit and that no test covers**.
>
> **Arming there means enforcing a money limit with a combination nobody has ever tested.**
>
> Measured on 2026-09-02: that window lasted **77 seconds** (ledger `seq` 8102–8104). The guardian
> happened to be `DISARMED`, which is calendar luck, not a property.
>
> **Open NinjaTrader, press `F5` first, and only then Arm.**
>
> *This is a rule a human has to keep. It stops being one when `GUARDIAN_STARTED` carries both build
> identities and the addon refuses to arm on a mismatch — see
> [`docs/freno-identidad-build-20260902.md`](freno-identidad-build-20260902.md).*

**NinjaTrader does not compile NinjaScript at startup.** Not on a restart, not when it sees a new file.
Compilation happens on demand from the editor, and its own trace says so:
`NinjaScriptEditorHotKeys: … Compile='F5'`.

- Start NinjaTrader.
- `New → NinjaScript Editor`.
- **Open a script** — expand **AddOns**, double-click `DeadmanGuardianAddOn`. An empty "New tab" is not
  enough; `F5` on it does nothing.
- Press **`F5`**.

You will know it worked because `Documents\NinjaTrader 8\bin\Custom\NinjaTrader.Custom.dll` gets a new
timestamp and grows. If the editor shows errors instead, see [troubleshooting.md](troubleshooting.md) — and
note that a failed compile leaves the previous working assembly in place, so your platform keeps running.

*(Do not try to force a build by deleting `NinjaTrader.Custom.dll`. NinjaTrader restores a stock copy from
its installation instead of building one, and takes your compiled scripts with it. We tried.)*

### 5. Restart NinjaTrader

AddOns are instantiated when NinjaTrader **starts**, not when NinjaScript compiles. Until you restart, the
guardian exists in the assembly and is not running.

### 6. Check it came up

`Documents\NinjaTrader 8\deadman-guardian\` should now contain:

| file | what it means |
|---|---|
| `adapter.log` | `boot` → `Core started; state=Disarmed` → `subscribed to <your account>` |
| `ledger.jsonl` | one line, `GUARDIAN_STARTED`, `"prev":"genesis"` |
| `state.json` | `"state":"DISARMED"` |
| `config.example.json` | a template with the two limits left blank |

And a small window appears at the top right saying **NOT PROTECTED**, with the reason: there is no
`config.json` yet. That is correct. The installer does not write one on purpose — a risk limit somebody
else typed is a default, and this tool does not do defaults.

You verify the ledger with anything that can hash JSON; it does not have to be our code:

```python
import json, hashlib
prev = "genesis"
for line in open(r"...\deadman-guardian\ledger.jsonl", encoding="utf-8"):
    e = json.loads(line); h = e.pop("hash")
    canon = json.dumps(e, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    assert e["prev"] == prev and hashlib.sha256(canon.encode()).hexdigest() == h
    prev = h
print("chain OK")
```

---

## Verifying the install actually took

Two checks, and they do **different** jobs. Reading only the first one is how you end up confidently
running the old binary.

**1. Did it load, and did it compile?** In the Control Center Log after pressing F5:

```
Vendor assembly 'GuardianCore' version='0.1.0.0' loaded.
```

That line, plus the absence of NinjaScript compile errors, tells you **that something loaded**. It does
**not** tell you *which* build: `0.1.0.0` is the `AssemblyVersion`, and it is **identical in every build
of GuardianCore ever made**. It does not move when the code changes. A reader who stops here has
verified nothing about the version they are running.

**2. Which build is it?** Compare the deployed file against the one you meant to deploy:

```
sha256sum "$USERPROFILE/Documents/NinjaTrader 8/bin/Custom/GuardianCore.dll" | cut -c1-16
sha256sum src/GuardianCore/bin/Release/net48/GuardianCore.dll                | cut -c1-16
```

Equal means the deployed binary is the one you built. **This is the only check that discriminates**,
and it is the same 16 characters that appear as `issuer.buildHash` in a certificate
([`CERT_CONFORMANCE.md`](../CERT_CONFORMANCE.md)).

The distinction is worth stating plainly because it is the defect this project keeps finding in other
places: *a check that returns the same answer whether or not the thing under test changed is not a
check.* A soak that passed with an impossible reference price, a gate file that reported "clean"
without reading anything, `version='0.1.0.0'`, an installer whose failure looked like its success, and
the one below are the same animal.

**The most expensive instance so far, and it was ours.** For a week every report here said "compiles
against the real NinjaTrader assemblies, 0 errors" — over a compile set that **did not include
`DeadmanGuardianAddOn.cs`**, the single file the user actually sees. The build was green because it
never touched the thing being claimed for. Adding the file on 2026-08-22 produced an error
immediately: inside `namespace NinjaTrader.NinjaScript.AddOns` a bare `NinjaScript.Log` resolves to
the enclosing *namespace*, not the class, so it must be fully qualified. That would have failed in a
human's F5, not in our build.

It is worse than the others because the claim was repeated. A green that never covered the artifact is
not a weaker verification, it is a **false statement made confidently and often** — and the fix is
mechanical: a verification set has to be checked against the artifact list it claims to cover, not
assumed to have grown with it.

## What the installer changes on your machine

Everything, in one list, so uninstalling is not an act of faith:

- `bin\Custom\AddOns\GuardianPorts.cs`, `bin\Custom\AddOns\DeadmanGuardianAddOn.cs` *(plus two soak files
  with `-WithSoak`)*
- `bin\Custom\GuardianCore.dll`
- `bin\Custom\NinjaTrader.Custom.csproj` — `<Compile>` entries and one `<Reference>`; the original is backed
  up beside it as `NinjaTrader.Custom.csproj.deadman-backup`
- `deadman-guardian\config.example.json`

It does not touch an account, place an order, change a NinjaTrader setting, or open a socket.
