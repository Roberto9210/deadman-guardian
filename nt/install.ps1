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
# PanelPlacement.cs joined on 2026-08-31, and it is the same shape as the line above: a new pure-C#
# file the addon references. Adding it here is what the exit-4 completeness check exists to force.
$sources = @("GuardedAccountRule.cs", "PanelPlacement.cs", "SoundChannel.cs",
             "GuardianPorts.cs", "DeadmanGuardianAddOn.cs")
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

# ---------------------------------------------------------------- completeness, against reality
# The lists above are written by hand, and the sixth instance of "a green check over a set that was
# no longer the artifact list" was exactly a file missing from them: M15 added GuardedAccountRule.cs,
# nobody taught the installer, the deploy shipped an addon referencing a type that was not in the
# Custom folder, and the F5 failed CS0246 on the live platform (2026-08-25). A hand list without a
# check against the filesystem repeats that identically the next time a commit adds a file.
#
# So: every .cs that actually exists in the managed directories must be in the union of the lists,
# or nothing is installed and the missing ones are named. The published set derives from what
# produces it - the same rule as everywhere else in this project.
$managedNames = $sources + $soakSources + $botShared + $botSources
$actualNames = @()
foreach ($dir in @("nt/addon", "nt/soak", "nt/bots")) {
    $actualNames += Get-ChildItem (Join-Path $repo $dir) -Filter *.cs | ForEach-Object { $_.Name }
}
$unmanaged = $actualNames | Where-Object { $managedNames -notcontains $_ }
if ($unmanaged) {
    $bar0 = "=" * 76
    Write-Host ""
    Write-Host $bar0 -ForegroundColor Red
    Write-Host ""
    Write-Host "     N O T H I N G   W A S   I N S T A L L E D" -ForegroundColor Red
    Write-Host ""
    Write-Host "     The repository has .cs files this installer does not manage:" -ForegroundColor Red
    foreach ($m in $unmanaged) { Write-Host ("       - " + $m) -ForegroundColor Red }
    Write-Host ""
    Write-Host "  A source file that exists but is not deployed produces CS0246 in the human's F5"
    Write-Host "  when anything deployed references it - which already happened once. Add each file"
    Write-Host "  to the right list in this script (sources / soakSources / botShared / botSources)"
    Write-Host "  and run again."
    Write-Host ""
    Write-Host $bar0 -ForegroundColor Red
    exit 4
}
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

# ---------------------------------------------------------------- BUILD CURRENCY GUARD
# The hash comparison at the end of this script establishes that the copy took, and nothing else.
# On 2026-08-26 it printed MATCH over a build 26 seconds OLDER than the source fix it was supposed
# to carry - and the sentence beside it, "the deployed binary is the one you just built", asserted a
# compile that had never happened. The bytes were faithfully copied. They were the wrong bytes.
#
# A stale build is worse than no build: the deploy reports success, the F5 compiles against it, and
# the live test measures the previous version while everyone believes it is measuring the new one.
# So currency is checked HERE, where refusing is still free.
#
# bin\ and obj\ are excluded on purpose. They hold this build's own outputs, some of them newer than
# the DLL by construction, and a guard that trips on its own artefacts gets switched off by the first
# person who meets it - which is the same as not having written it.
$coreSrcDir = Join-Path $repo "src\GuardianCore"
$buildTime = (Get-Item $coreDll).LastWriteTime
$coreSrcs = Get-ChildItem $coreSrcDir -Recurse -Include *.cs, *.csproj |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$newestSrc = $coreSrcs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$staleAgainst = @($coreSrcs | Where-Object { $_.LastWriteTime -gt $buildTime })

if ($staleAgainst.Count -gt 0) {
    $bar5 = "=" * 76
    Write-Host ""
    Write-Host $bar5 -ForegroundColor Red
    Write-Host ""
    Write-Host "     N O T H I N G   W A S   I N S T A L L E D" -ForegroundColor Red
    Write-Host ""
    Write-Host "     No file was copied. No file was edited. Nothing changed at all." -ForegroundColor Red
    Write-Host "     The version NinjaTrader would run is the OLD one, unchanged." -ForegroundColor Red
    Write-Host ""
    Write-Host $bar5 -ForegroundColor Red
    Write-Host ""
    Write-Host "  Why: GuardianCore.dll is OLDER than its own source. Installing it would deploy"
    Write-Host "  code that does not contain the changes sitting in these files:"
    Write-Host ""
    Write-Host ("    build   " + $buildTime.ToString("yyyy-MM-dd HH:mm:ss") + "   GuardianCore.dll")
    foreach ($f in ($staleAgainst | Sort-Object LastWriteTime -Descending)) {
        Write-Host ("    source  " + $f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "   " + $f.Name) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "  COMPILE FIRST, THEN INSTALL:" -ForegroundColor Yellow
    Write-Host "    dotnet build src\GuardianCore\GuardianCore.csproj -c Release"
    Write-Host ""
    Write-Host "  Then run this script again. Nothing has been installed by this run."
    Write-Host ""
    Write-Host $bar5 -ForegroundColor Red
    Write-Host ""
    exit 5
}

New-Item -ItemType Directory -Force -Path $addons, $home8 | Out-Null

if (-not (Test-Path $backup)) { Copy-Item $csproj $backup -Force; "backed up NinjaTrader.Custom.csproj" }

$copied = @()
# Every copy records where it came from, so the verification at the end can cover ALL of them. Until
# 2026-08-26 the hash check covered ONE file out of ten, and the other nine were verified by a human
# running sha256sum by hand - which is the manual step this script exists to remove.
$deployedPairs = @()
foreach ($s in $sources) {
    Copy-Item (Join-Path $repo "nt\addon\$s") (Join-Path $addons $s) -Force
    $copied += $s
    $deployedPairs += @{ Name = $s; From = (Join-Path $repo "nt\addon\$s"); To = (Join-Path $addons $s) }
    "copied $s"
}
if ($WithSoak -or $WithBots) {
    # The shared pair goes in for either switch, once.
    foreach ($s in $botShared) {
        Copy-Item (Join-Path $repo "nt\bots\$s") (Join-Path $addons $s) -Force
        $copied += $s
        $deployedPairs += @{ Name = $s; From = (Join-Path $repo "nt\bots\$s"); To = (Join-Path $addons $s) }
        "copied $s  (shared: account rail + guardrails)"
    }
}
if ($WithSoak) {
    foreach ($s in $soakSources) {
        Copy-Item (Join-Path $repo "nt\soak\$s") (Join-Path $addons $s) -Force
        $copied += $s
        $deployedPairs += @{ Name = $s; From = (Join-Path $repo "nt\soak\$s"); To = (Join-Path $addons $s) }
        "copied $s  (soak suite)"
    }
}
if ($WithBots) {
    foreach ($s in $botSources) {
        Copy-Item (Join-Path $repo "nt\bots\$s") (Join-Path $addons $s) -Force
        $copied += $s
        $deployedPairs += @{ Name = $s; From = (Join-Path $repo "nt\bots\$s"); To = (Join-Path $addons $s) }
        "copied $s  (test bots - THESE SEND FILLABLE ORDERS)"
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $ntUser "deadman-guardian-bots") | Out-Null
    "created deadman-guardian-bots\ (gates, sandbox runs and reports live there)"
}
Copy-Item $coreDll (Join-Path $custom "GuardianCore.dll") -Force
$copied += "GuardianCore.dll"
$deployedPairs += @{ Name = "GuardianCore.dll"; From = $coreDll; To = (Join-Path $custom "GuardianCore.dll") }
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

# ALL of them, not just the DLL. A source file that silently failed to copy is a compile against the
# previous version of that file - the same failure the DLL check was written to catch, in the nine
# places it was not looking.
$mismatched = @()
foreach ($pair in $deployedPairs) {
    if (-not (Test-Path $pair.To)) { $mismatched += ($pair.Name + " (missing at destination)"); continue }
    $hFrom = (Get-FileHash $pair.From -Algorithm SHA256).Hash
    $hTo   = (Get-FileHash $pair.To   -Algorithm SHA256).Hash
    if ($hFrom -ne $hTo) { $mismatched += ($pair.Name + " (differs from the repository copy)") }
}
# The other three guards refuse BEFORE mutating anything. This one cannot - a copy is not
# verifiable until it has happened - so it does the only equivalent available: it UNDOES.
#
# Printing "do not press F5" and leaving a half-deployed machine would be the failure this very
# file documents at the NinjaTrader guard: on 2026-08-21 a guard fired, printed one line, and the
# operator watched it scroll past, pressed F5 and carried on. An error that does not stop the human
# reading it is, in its result, an error that did not happen. A revert stops them without asking
# them to remember anything.
if ($mismatched.Count -gt 0) {
    $revertProblems = @()

    # Only what THIS run copied. The full source lists would delete files belonging to an earlier
    # good install that this run never touched - e.g. the bots when run without -WithBots.
    foreach ($name in $copied) {
        $target = if ($name -eq "GuardianCore.dll") { Join-Path $custom $name } else { Join-Path $addons $name }
        try { if (Test-Path $target) { Remove-Item $target -Force -ErrorAction Stop } }
        catch { $revertProblems += ("could not remove " + $name + ": " + $_.Exception.Message) }
    }
    # A MISSING backup is a rollback failure, not a no-op. The first version of this block wrapped
    # the restore in "if (Test-Path $backup)", so an absent backup skipped it silently and the
    # script went on to announce ROLLED BACK over a csproj still carrying the installer's edit -
    # this file's own defect class, committed inside the fix for it, and caught by its test. By the
    # time execution reaches here the backup always exists (it is created before the first copy), so
    # its absence means something removed it mid-run: anomalous, and the human has to know.
    #
    # And the restore is VERIFIED rather than assumed: Copy-Item reporting no error is not the same
    # fact as the project file now matching the backup.
    if (-not (Test-Path $backup)) {
        $revertProblems += "the csproj backup is gone, so the installer's edit to NinjaTrader.Custom.csproj could not be undone"
    } else {
        try {
            Copy-Item $backup $csproj -Force -ErrorAction Stop
            $hB = (Get-FileHash $backup -Algorithm SHA256).Hash
            $hC = (Get-FileHash $csproj -Algorithm SHA256).Hash
            if ($hB -ne $hC) { $revertProblems += "NinjaTrader.Custom.csproj still does not match the backup after restoring it" }
        }
        catch { $revertProblems += ("could not restore NinjaTrader.Custom.csproj: " + $_.Exception.Message) }
    }

    $bar6 = "=" * 76
    Write-Host ""
    Write-Host $bar6 -ForegroundColor Red
    Write-Host ""
    Write-Host "     D E P L O Y M E N T   I S   N O T   W H A T   T H E   R E P O   H A S" -ForegroundColor Red
    Write-Host ""
    foreach ($m in $mismatched) { Write-Host ("     " + $m) -ForegroundColor Red }
    Write-Host ""
    Write-Host $bar6 -ForegroundColor Red
    Write-Host ""

    if ($revertProblems.Count -eq 0) {
        Write-Host "     R O L L E D   B A C K   -   N O T H I N G   T O   P R E S S" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  Every file this run copied has been removed and NinjaTrader.Custom.csproj was"
        Write-Host "  restored from the backup. Nothing from this run is left on the machine, so"
        Write-Host "  there is no wrong build for an F5 to compile."
        Write-Host ""
        Write-Host "  Said precisely, because it is not quite 'as it was': if an earlier installation"
        Write-Host "  was in place, its files were overwritten before this check could run and are"
        Write-Host "  gone too. NinjaTrader is back to stock, which compiles cleanly. Fix the cause"
        Write-Host "  above and run this script again."
    } else {
        Write-Host "     T H E   R O L L B A C K   I T S E L F   F A I L E D" -ForegroundColor Red
        Write-Host ""
        Write-Host "  This one DOES need you. The machine is half-deployed and this script could not"
        Write-Host "  undo it:"
        Write-Host ""
        foreach ($r in $revertProblems) { Write-Host ("     " + $r) -ForegroundColor Red }
        Write-Host ""
        Write-Host "  Do not press F5. Undo by hand with:" -ForegroundColor Yellow
        Write-Host "      .\install.ps1 -Uninstall"
        Write-Host "  which removes the deployed files and restores the project file from the backup."
    }
    Write-Host ""
    Write-Host $bar6 -ForegroundColor Red
    Write-Host ""
    exit 6
}
$bar2 = "=" * 76

""
$bar2
"  INSTALLED - " + $copied.Count + " file(s) copied:"
foreach ($c in $copied) { "      " + $c }
""
"  all " + $deployedPairs.Count + " deployed files match the repository, byte for byte."
"  GuardianCore.dll now deployed : " + $deployedHash
"  the bytes it was copied from  : " + $builtHash
# TWO statements, because they establish two different things and one of them used to claim the
# other. "the deployed binary is the one you just built" was a compile claim made by a script
# that never compiles anything; it survived a deploy of a stale build without a murmur.
#
# Unconditional now: the ten-file verification above compares the FULL sha256 of these same two
# files and exits 6 if they differ, so reaching this line already establishes they are identical.
# The `if` that used to wrap it, its MISMATCH branch and the `exit 3` at the end of this block were
# unreachable from the moment that check landed - a check that cannot run is not a check, it is
# decoration that suggests a protection nobody has.
"  COPY VERIFIED  - the deployed bytes are the bytes in bin\Release\net48."
"  BUILD CURRENT  - build " + $buildTime.ToString("yyyy-MM-dd HH:mm:ss") +
    ", newest source " + $newestSrc.Name + " " + $newestSrc.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "."
""
"  Those 16 characters are the same value a certificate reports as issuer.buildHash."
"  Note what does NOT verify this: the NinjaTrader Log line"
"      Vendor assembly 'GuardianCore' version='0.1.0.0' loaded"
"  0.1.0.0 is the AssemblyVersion and is identical in every build ever made. It tells"
"  you something loaded. It cannot tell you WHICH."
$bar2

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

# LAST, because this is where the person is standing when it matters. Measured 2026-09-02: the deploy
# lands in TWO stages. GuardianCore.dll is copied by this script and NinjaTrader loads it AT STARTUP;
# the addon lives in NinjaTrader.Custom.dll and is only rebuilt by F5. Between those two moments the
# platform runs a NEW Core against the PREVIOUSLY COMPILED addon - a combination that corresponds to
# no commit and that no test covers. It lasted 77 seconds that day (ledger seq 8102-8104) and the
# guardian happened to be DISARMED, which is calendar luck and not a property.
#
# The code already knew this window, four times over, but only as an API-compatibility constraint:
# CertificateRequest.DaysCovered, the chainVerified parameter of Certificate.Issue, the daysCovered
# computation, and PositionSnapshot's two constructors - the last of which names the exact failure,
# MissingMethodException. Same fact, two readings, and only one of them was written down.
#
# Until GUARDIAN_STARTED carries both build identities and the addon can refuse to arm on a mismatch,
# this is a rule a human has to keep, so it goes where the human is - not into a findings document.
$bar3 = "=" * 76
Write-Host ""
Write-Host $bar3 -ForegroundColor Yellow
Write-Host ""
Write-Host "     N E V E R   A R M   B E T W E E N   O P E N I N G" -ForegroundColor Yellow
Write-Host "     N I N J A T R A D E R   A N D   P R E S S I N G   F 5" -ForegroundColor Yellow
Write-Host ""
Write-Host "  In that window NinjaTrader is running the NEW GuardianCore.dll against the"
Write-Host "  OLD compiled addon. That pairing matches no commit and no test. Arming there"
Write-Host "  means enforcing a money limit with a combination nobody has ever tested."
Write-Host ""
Write-Host "  Open NinjaTrader, press F5 FIRST, and only then Arm."
Write-Host ""
Write-Host $bar3 -ForegroundColor Yellow
