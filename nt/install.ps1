# deadman-guardian — install into NinjaTrader 8, and uninstall.
#
# Established empirically on 2026-08-20 against NT 8.1.8.2 (see STEP3_FINDINGS.md §6):
# NinjaTrader compiles `Documents\NinjaTrader 8\bin\Custom\NinjaTrader.Custom.csproj`, an SDK-style
# net48/x64/WPF project with EnableDefaultCompileItems=false and an explicit <Compile Include> list.
# Dropping a .cs into bin\Custom\AddOns\ is not sufficient: the file must be listed. Nor is a restart
# enough - NinjaTrader compiles NinjaScript on demand, from the editor (F5). This installer edits the
# project file and keeps a backup; the compile is yours to trigger. See STEP3_FINDINGS.md section 6.
#
#   .\install.ps1              install
#   .\install.ps1 -Uninstall   remove everything this script added
#
# It never touches an account, never places an order, and never changes a NinjaTrader setting.

param([switch]$Uninstall, [switch]$WithSoak, [switch]$WithBots)

$ErrorActionPreference = "Stop"

$repo    = Split-Path -Parent $PSScriptRoot
$ntUser  = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "NinjaTrader 8"
$custom  = Join-Path $ntUser "bin\Custom"
$addons  = Join-Path $custom "AddOns"
$csproj  = Join-Path $custom "NinjaTrader.Custom.csproj"
$backup  = Join-Path $custom "NinjaTrader.Custom.csproj.deadman-backup"
$home8   = Join-Path $ntUser "deadman-guardian"

# GuardedAccountRule.cs was added by M15 and the installer never learned of it: the deployed
# addon referenced a type that was not in the Custom folder, and the next F5 failed with
# CS0246/CS0103 on the live platform (2026-08-25). The verification that said IGUAL 9-of-9 was
# green over a set that was no longer the artifact list - the same animal, sixth appearance.
$sources = @("GuardedAccountRule.cs", "GuardianPorts.cs", "DeadmanGuardianAddOn.cs")
# The soak suite is opt-in: it is an attacker, it places (unfillable) orders on Sim101, and it has no
# business on a machine that is not being soaked. -WithSoak adds it.
$soakSources = @("SoakSandbox.cs", "DeadmanGuardianSoak.cs")
# The two test bots are opt-in for a harder reason than the soak. The soak refuses to send a
# fillable order; the BOTS EXIST TO SEND THEM. Bot A loses money on purpose until the guardian
# locks out, and a lockout calls the account-wide Flatten. That belongs on a soak machine and
# nowhere else, so it never installs unless you ask for it by name.
# Shared by the soak AND the bots since 2026-08-22: the soak prints Account.All through the same
# mapping (BotSafety.FactsOf) and the same formatter (BotAccountRule.Describe) the bots decide
# with. That is the point - a second dialect of the same line is a second thing to get wrong, and
# the printed line is the only verification the mapping gets. So -WithSoak now needs them too.
$botShared   = @("BotAccountRule.cs", "BotGuardrails.cs")
$botSources  = @("DeadmanBotA.cs", "DeadmanBotB.cs")
# net48, NOT netstandard2.0: NinjaTrader's in-process compiler has no 'netstandard' facade in its
# reference set, so a netstandard2.0 assembly loads at runtime and fails at compile time.
$coreDll = Join-Path $repo "src\GuardianCore\bin\Release\net48\GuardianCore.dll"

# This check runs BEFORE anything is created, copied or edited, and it exits with a non-zero code so
# that a script calling this one finds out too.
#
# It is deliberately loud. On 2026-08-21 this guard fired, printed one line, and the operator - who had
# typed the right command - watched text scroll past, pressed F5, restarted NinjaTrader and carried on
# believing the install had happened. Nothing had been copied; the old build kept running. An error
# that does not stop the human reading it is, in its result, an error that did not happen. "Close it
# first" says what to DO; it never said what had NOT occurred, and that is the half that mattered.
$ntProc = Get-Process NinjaTrader -ErrorAction SilentlyContinue
if ($ntProc) {
    $bar = "=" * 76
    Write-Host ""
    Write-Host $bar -ForegroundColor Red
    Write-Host ""
    Write-Host "     N O T H I N G   W A S   I N S T A L L E D" -ForegroundColor Red
    Write-Host ""
    Write-Host "     No file was copied. No file was edited. Nothing changed at all." -ForegroundColor Red
    Write-Host "     The version NinjaTrader is running is the OLD one, unchanged." -ForegroundColor Red
    Write-Host ""
    Write-Host $bar -ForegroundColor Red
    Write-Host ""
    Write-Host ("  Why: NinjaTrader is still running (PID " + ($ntProc.Id -join ", ") + ").")
    Write-Host "  It holds GuardianCore.dll open, so that file cannot be replaced."
    Write-Host ""
    Write-Host "  CLOSING THE MAIN WINDOW IS NOT ENOUGH." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  NinjaTrader keeps running in the SYSTEM TRAY - the small icons beside the"
    Write-Host "  clock, bottom-right of your screen. Some of them are hidden behind a small"
    Write-Host "  up-arrow (^); click it to show them."
    Write-Host ""
    Write-Host "    1. find the NinjaTrader icon there"
    Write-Host "    2. right-click it"
    Write-Host "    3. choose Exit"
    Write-Host "    4. run this script again"
    Write-Host ""
    Write-Host "  You will know it worked because this script will print a list of copied"
    Write-Host "  files and a checksum at the end. If you do not see that list, nothing was"
    Write-Host "  installed - no matter what else scrolled past."
    Write-Host ""
    Write-Host $bar -ForegroundColor Red
    Write-Host ""
    exit 2
}
if (-not (Test-Path $csproj)) { throw "not found: $csproj" }

# ---------------------------------------------------------------- uninstall
if ($Uninstall) {
    foreach ($s in ($sources + $soakSources + $botShared + $botSources)) {
        $p = Join-Path $addons $s
        if (Test-Path $p) { Remove-Item $p -Force; "removed $s" }
    }
    $dll = Join-Path $custom "GuardianCore.dll"
    if (Test-Path $dll) { Remove-Item $dll -Force; "removed GuardianCore.dll" }

    if (Test-Path $backup) {
        Copy-Item $backup $csproj -Force
        Remove-Item $backup -Force
        "restored the original NinjaTrader.Custom.csproj"
    }
    ""
    "Uninstalled. Your state, ledger and config under $home8 were left alone -"
    "delete that folder by hand if you also want the record gone."
    return
}

# ---------------------------------------------------------------- install
if (-not (Test-Path $coreDll)) {
    throw "GuardianCore.dll (net48) not built. Run: dotnet build src\GuardianCore\GuardianCore.csproj -c Release"
}

New-Item -ItemType Directory -Force -Path $addons, $home8 | Out-Null

if (-not (Test-Path $backup)) { Copy-Item $csproj $backup -Force; "backed up NinjaTrader.Custom.csproj" }

$copied = @()
foreach ($s in $sources) {
    Copy-Item (Join-Path $repo "nt\addon\$s") (Join-Path $addons $s) -Force
    $copied += $s
    "copied $s"
}
if ($WithSoak -or $WithBots) {
    # The shared pair goes in for either switch, once.
    foreach ($s in $botShared) {
        Copy-Item (Join-Path $repo "nt\bots\$s") (Join-Path $addons $s) -Force
        $copied += $s
        "copied $s  (shared: account rail + guardrails)"
    }
}
if ($WithSoak) {
    foreach ($s in $soakSources) {
        Copy-Item (Join-Path $repo "nt\soak\$s") (Join-Path $addons $s) -Force
        $copied += $s
        "copied $s  (soak suite)"
    }
}
if ($WithBots) {
    foreach ($s in $botSources) {
        Copy-Item (Join-Path $repo "nt\bots\$s") (Join-Path $addons $s) -Force
        $copied += $s
        "copied $s  (test bots - THESE SEND FILLABLE ORDERS)"
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $ntUser "deadman-guardian-bots") | Out-Null
    "created deadman-guardian-bots\ (gates, sandbox runs and reports live there)"
}
Copy-Item $coreDll (Join-Path $custom "GuardianCore.dll") -Force
$copied += "GuardianCore.dll"
"copied GuardianCore.dll"

$xml = Get-Content $csproj -Raw

$compileAnchor = '<Compile Include="Indicators\%40DetrendedPriceOscillator.cs" />'
# Use the ANCHOR LINE'S OWN indentation for the entries we insert, instead of hardcoding tabs.
# Two reasons: the project file keeps one indentation style, and the anchor line is not ours
# to reformat.
$indent = "`t`t"
$anchorMatch = [regex]::Match($xml, '(?m)^([ \t]*)' + [regex]::Escape($compileAnchor))
if ($anchorMatch.Success) { $indent = $anchorMatch.Groups[1].Value }

$toAdd = ""
$added = @()
$allSources = $sources
if ($WithSoak -or $WithBots) { $allSources += $botShared }
if ($WithSoak) { $allSources += $soakSources }
if ($WithBots) { $allSources += $botSources }
$allSources = $allSources | Select-Object -Unique   # a file listed twice would get two <Compile> entries
foreach ($s in $allSources) {
    $entry = '<Compile Include="AddOns\' + $s + '" />'
    if ($xml -notmatch [regex]::Escape($entry)) { $toAdd += "$indent$entry`r`n"; $added += $s }
}
if ($toAdd -ne "") {
    # The match CONSUMES the anchor's leading whitespace and the replacement puts it back once.
    # Replacing the anchor alone left the first inserted entry double-indented - visible on line
    # 86 of the installed project file after the -WithBots run of 2026-08-21.
    $xml = $xml -replace ('(?m)^[ \t]*' + [regex]::Escape($compileAnchor)), ($toAdd + $indent + $compileAnchor)
    # $added, not $allSources: naming the ones already present would claim work never done. A message
    # that overstates is a message that lies, and this one is read by someone deciding whether to trust
    # the install. (An earlier version of this comment cited "SPEC section 4 rule 5" for that; rule 5 is
    # about currency denomination. The principle stands on its own and needed no borrowed authority.)
    "added <Compile> entries for: $($added -join ', ')"
}
else { "no <Compile> entries to add: all $($allSources.Count) were already listed" }

# NinjaTrader IGNORES a <Reference> appended in an ItemGroup of your own with a relative HintPath -
# established the hard way, see STEP3_FINDINGS.md section 9. The entry its own References dialog writes
# goes INSIDE the ItemGroup NT8 already manages, and its HintPath is ABSOLUTE. Write exactly that shape.
if ($xml -notmatch 'Include="GuardianCore"') {
    $absolute = Join-Path $custom "GuardianCore.dll"
    $anchor = '<Reference Include="WindowsBase">'
    if ($xml -match [regex]::Escape($anchor)) {
        $entry = "<Reference Include=`"GuardianCore`">`r`n        <HintPath>$absolute</HintPath>`r`n      </Reference>`r`n      "
        $xml = $xml -replace [regex]::Escape($anchor), ($entry + $anchor)
        "added the GuardianCore <Reference> (absolute HintPath, in NinjaTrader's own ItemGroup)"
    } else {
        "WARNING: could not find NinjaTrader's reference ItemGroup. Add GuardianCore.dll by hand:"
        "  NinjaScript Editor -> right click -> References... -> Add -> $absolute"
    }
}

Set-Content -Path $csproj -Value $xml -Encoding UTF8
"updated NinjaTrader.Custom.csproj"

$example = Join-Path $home8 "config.example.json"
$ledger  = (Join-Path $home8 "ledger.jsonl") -replace '\\', '\\\\'
$state   = (Join-Path $home8 "state.json")   -replace '\\', '\\\\'
@"
{
  "schemaVersion": 1,
  "accounts": ["Sim101"],
  "currency": "UsDollar",
  "firmDailyLossLimit": "PUT-YOUR-FIRM-LIMIT-HERE",
  "personalDailyLossLimit": "PUT-YOUR-OWN-LIMIT-HERE",
  "sessionResetTimeZone": "America/Chicago",
  "sessionResetLocalTime": "17:00",
  "ledgerPath": "$ledger",
  "statePath": "$state",
  "pnlToleranceUsd": "5.00"
}
"@ | Set-Content -Path $example -Encoding UTF8
"wrote $example"

# Deliberately NOT writing config.json. SPEC section 4 forbids defaults, and a limit somebody else
# typed is a default. Until you copy the example to config.json and put your own two numbers in it,
# the guardian shows NOT ARMED and refuses to arm - which is the correct state, not a fault.
if (Test-Path (Join-Path $home8 "config.json")) { "config.json already exists, left untouched" }
else { "no config.json: the guardian will start NOT ARMED until you write one" }

# ---------------------------------------------------------------- what actually changed
# The confirmation belongs on THIS screen. Requiring a second command to find out whether the first
# one worked is how an operator ends up believing an install that never happened.
$deployedDll  = Join-Path $custom "GuardianCore.dll"
$deployedHash = (Get-FileHash $deployedDll -Algorithm SHA256).Hash.Substring(0, 16).ToLower()
$builtHash    = (Get-FileHash $coreDll     -Algorithm SHA256).Hash.Substring(0, 16).ToLower()
$bar2 = "=" * 76

""
$bar2
"  INSTALLED - " + $copied.Count + " file(s) copied:"
foreach ($c in $copied) { "      " + $c }
""
"  GuardianCore.dll now deployed : " + $deployedHash
"  the build it was copied from  : " + $builtHash
if ($deployedHash -eq $builtHash) {
    "  MATCH - the deployed binary is the one you just built."
} else {
    Write-Host "  *** MISMATCH - the copy did NOT take. Do not compile, do not continue. ***" -ForegroundColor Red
}
""
"  Those 16 characters are the same value a certificate reports as issuer.buildHash."
"  Note what does NOT verify this: the NinjaTrader Log line"
"      Vendor assembly 'GuardianCore' version='0.1.0.0' loaded"
"  0.1.0.0 is the AssemblyVersion and is identical in every build ever made. It tells"
"  you something loaded. It cannot tell you WHICH."
$bar2
if ($deployedHash -ne $builtHash) { exit 3 }

""
"Installed - but NOT yet compiled. NinjaTrader compiles NinjaScript on demand, not at"
"startup: open it, then New > NinjaScript Editor and press F5. Verified the hard way -"
"a restart does not compile, and deleting NinjaTrader.Custom.dll makes NT8 restore a"
"stock copy instead of building one (STEP3_FINDINGS.md section 6)."
""
"After that compile, the status window appears top-right. Until you press Arm it"
"says NOT ARMED; if it says CANNOT SEE YOUR ACCOUNT, NinjaTrader has no data"
"connection open and the window tells you what to do about it."
""
"If NinjaTrader reports a NinjaScript compile error, undo everything with:"
"    .\install.ps1 -Uninstall"
"That restores the original project file from the backup this script just made."
