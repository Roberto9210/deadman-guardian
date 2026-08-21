# Uninstalling

```powershell
# close NinjaTrader first - the files are locked while it runs
.\nt\install.ps1 -Uninstall
```

Then start NinjaTrader, `New → NinjaScript Editor`, open any script, press **F5**. That rebuilds
`NinjaTrader.Custom.dll` without the guardian in it. Until you do, the previously compiled assembly is still
what NinjaTrader loads — the same reason installing needs a compile, in reverse.

---

## What `-Uninstall` removes

- `bin\Custom\AddOns\GuardianPorts.cs`
- `bin\Custom\AddOns\DeadmanGuardianAddOn.cs`
- `bin\Custom\AddOns\SoakSandbox.cs` and `DeadmanGuardianSoak.cs`, if the soak suite was installed
- `bin\Custom\GuardianCore.dll`
- the `<Compile>` entries and the `<Reference>` it added, by restoring
  `NinjaTrader.Custom.csproj` from `NinjaTrader.Custom.csproj.deadman-backup`, the copy it made before
  touching anything

## What it deliberately leaves

**Your record.** `Documents\NinjaTrader 8\deadman-guardian\` — the ledger, the state, your config — is not
touched. A tool whose argument is "the record cannot be quietly tidied away" does not delete the record on
its way out.

Delete that folder by hand if you also want the history gone. Nothing else depends on it.

Also left alone: `deadman-guardian-probe\` and `deadman-guardian-soak\` if you ran either, for the same
reason.

---

## Verifying you are back where you started

```powershell
$custom = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "NinjaTrader 8\bin\Custom"
(Select-String -Path "$custom\NinjaTrader.Custom.csproj" -Pattern "Compile Include" -AllMatches).Count
Select-String -Path "$custom\NinjaTrader.Custom.csproj" -Pattern "GuardianCore"   # expect nothing
Test-Path "$custom\GuardianCore.dll"                                             # expect False
Get-ChildItem "$custom\AddOns"                                                   # expect no deadman files
```

The `<Compile>` count should match what it was before you installed. If you never installed anything else
in between, that is NinjaTrader's own 294 plus whatever you had already.

---

## If the uninstaller cannot run

It refuses while NinjaTrader is open, and it needs its backup file. If either is a problem, the manual
version is four steps:

1. Close NinjaTrader.
2. Delete the deadman files from `bin\Custom\AddOns\` and `bin\Custom\GuardianCore.dll`.
3. Copy `NinjaTrader.Custom.csproj.deadman-backup` over `NinjaTrader.Custom.csproj`. If that backup is gone,
   remove by hand the `<Compile Include="AddOns\…">` lines naming deadman files and the
   `<Reference Include="GuardianCore">` block.
4. Start NinjaTrader and press F5 in the editor.

**Do not "force a rebuild" by deleting `NinjaTrader.Custom.dll`.** NinjaTrader restores a stock copy from
its installation rather than compiling one, and that copy does not contain *any* of your custom scripts. We
did it once, on purpose, to find out; the platform was fine after restoring the backup, but there is no
reason for you to repeat it.
