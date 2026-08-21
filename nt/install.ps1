# deadman-guardian — install into NinjaTrader 8, and uninstall.
#
# Established empirically on 2026-08-20 against NT 8.1.8.2 (see STEP3_FINDINGS.md §6):
# NinjaTrader compiles `Documents\NinjaTrader 8\bin\Custom\NinjaTrader.Custom.csproj`, an SDK-style
# net48/x64/WPF project with EnableDefaultCompileItems=false and an explicit <Compile Include> list.
# Dropping a .cs into bin\Custom\AddOns\ was NOT sufficient in a controlled A/B: with the entry the
# AddOn compiled and loaded, without it nothing happened. So the installer edits that project file,
# and keeps a backup of the original next to it.
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
$coreDll = Join-Path $repo "src\GuardianCore\bin\Release\netstandard2.0\GuardianCore.dll"

if (Get-Process NinjaTrader -ErrorAction SilentlyContinue) {
    throw "NinjaTrader is running. Close it first: the compile happens at startup and the files are locked while it runs."
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
    throw "GuardianCore.dll not built. Run: dotnet build src\GuardianCore\GuardianCore.csproj -c Release"
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

$config = Join-Path $home8 "config.json"
if (-not (Test-Path $config)) {
    $ledger = (Join-Path $home8 "ledger.jsonl") -replace '\\', '\\'
    $state  = (Join-Path $home8 "state.json")   -replace '\\', '\\'
    @"
{
  "schemaVersion": 1,
  "accounts": ["Sim101"],
  "currency": "UsDollar",
  "firmDailyLossLimit": "1000.00",
  "personalDailyLossLimit": "600.00",
  "sessionResetTimeZone": "America/Chicago",
  "sessionResetLocalTime": "17:00",
  "ledgerPath": "$ledger",
  "statePath": "$state",
  "pnlToleranceUsd": "5.00"
}
"@ | Set-Content -Path $config -Encoding UTF8
    "wrote a starting config at $config  (Sim101, personal 600.00 under firm 1000.00)"
} else {
    "config already exists, left alone: $config"
}

""
"Installed. Start NinjaTrader; the status window appears top-right saying NOT PROTECTED"
"until you press Arm."
""
"If NinjaTrader reports a NinjaScript compile error, undo everything with:"
"    .\install.ps1 -Uninstall"
"That restores the original project file from the backup this script just made."
