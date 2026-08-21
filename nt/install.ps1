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

param([switch]$Uninstall)

$ErrorActionPreference = "Stop"

$repo    = Split-Path -Parent $PSScriptRoot
$ntUser  = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "NinjaTrader 8"
$custom  = Join-Path $ntUser "bin\Custom"
$addons  = Join-Path $custom "AddOns"
$csproj  = Join-Path $custom "NinjaTrader.Custom.csproj"
$backup  = Join-Path $custom "NinjaTrader.Custom.csproj.deadman-backup"
$home8   = Join-Path $ntUser "deadman-guardian"

$sources = @("GuardianPorts.cs", "DeadmanGuardianAddOn.cs")
# net48, NOT netstandard2.0: NinjaTrader's in-process compiler has no 'netstandard' facade in its
# reference set, so a netstandard2.0 assembly loads at runtime and fails at compile time.
$coreDll = Join-Path $repo "src\GuardianCorein\Release
et48\GuardianCore.dll"

if (Get-Process NinjaTrader -ErrorAction SilentlyContinue) {
    throw "NinjaTrader is running. Close it first - the files are locked while it runs."
}
if (-not (Test-Path $csproj)) { throw "not found: $csproj" }

# ---------------------------------------------------------------- uninstall
if ($Uninstall) {
    foreach ($s in $sources) {
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

foreach ($s in $sources) {
    Copy-Item (Join-Path $repo "nt\addon\$s") (Join-Path $addons $s) -Force
    "copied $s"
}
Copy-Item $coreDll (Join-Path $custom "GuardianCore.dll") -Force
"copied GuardianCore.dll"

$xml = Get-Content $csproj -Raw

$compileAnchor = '<Compile Include="Indicators\%40DetrendedPriceOscillator.cs" />'
$toAdd = ""
foreach ($s in $sources) {
    $entry = '<Compile Include="AddOns\' + $s + '" />'
    if ($xml -notmatch [regex]::Escape($entry)) { $toAdd += "`t`t$entry`r`n" }
}
if ($toAdd -ne "") {
    $xml = $xml -replace [regex]::Escape($compileAnchor), ($toAdd + "`t`t" + $compileAnchor)
    "added $($sources.Count) <Compile> entr(y/ies)"
}

if ($xml -notmatch 'Include="GuardianCore"') {
    $ref = @"
	<ItemGroup>
		<Reference Include="GuardianCore">
			<HintPath>GuardianCore.dll</HintPath>
			<SpecificVersion>False</SpecificVersion>
			<Private>false</Private>
		</Reference>
	</ItemGroup>
"@
    $xml = $xml -replace "</Project>", ($ref + "`r`n</Project>")
    "added the GuardianCore <Reference>"
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
# the guardian shows NOT PROTECTED and refuses to arm - which is the correct state, not a fault.
if (Test-Path (Join-Path $home8 "config.json")) { "config.json already exists, left untouched" }
else { "no config.json: the guardian will start NOT PROTECTED until you write one" }

""
"Installed - but NOT yet compiled. NinjaTrader compiles NinjaScript on demand, not at"
"startup: open it, then New > NinjaScript Editor and press F5. Verified the hard way -"
"a restart does not compile, and deleting NinjaTrader.Custom.dll makes NT8 restore a"
"stock copy instead of building one (STEP3_FINDINGS.md section 6)."
""
"After that compile, the status window appears top-right saying NOT PROTECTED"
"until you press Arm."
""
"If NinjaTrader reports a NinjaScript compile error, undo everything with:"
"    .\install.ps1 -Uninstall"
"That restores the original project file from the backup this script just made."
