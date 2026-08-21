# Deploys the certificate emitter into the installed add-on.
#
# Why this is a script and not "copy two files": the add-on source and GuardianCore.dll must move
# TOGETHER. The new DeadmanGuardianAddOn.cs references Certificate/CertificateRequest, which do not
# exist in the currently installed DLL, so an F5 with only one of the two in place fails CS0246 -
# the exact failure that cost two installs during Step 3. NinjaTrader locks the DLL while running,
# so this refuses to do half the job.
#
#     powershell -ExecutionPolicy Bypass -File nt\deploy-certificate-update.ps1
#
# Then: open NinjaTrader, NinjaScript Editor, open DeadmanGuardianAddOn, F5, restart.

$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent $PSScriptRoot
$nt      = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'NinjaTrader 8'
$custom  = Join-Path $nt 'bin\Custom'
$addons  = Join-Path $custom 'AddOns'
$dllFrom = Join-Path $repo 'src\GuardianCore\bin\Release\net48\GuardianCore.dll'
$csFrom  = Join-Path $repo 'nt\addon\DeadmanGuardianAddOn.cs'

if (Get-Process NinjaTrader -ErrorAction SilentlyContinue) {
    Write-Host ''
    Write-Host 'NinjaTrader is running, and it holds GuardianCore.dll open.' -ForegroundColor Yellow
    Write-Host 'Close NinjaTrader completely, then run this again.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Nothing was changed. Deploying the .cs without the .dll would make the next F5'
    Write-Host 'fail with CS0246, so this refuses to do half of it.'
    exit 1
}

if (-not (Test-Path $dllFrom)) {
    throw "No release build at $dllFrom - run: dotnet build src\GuardianCore\GuardianCore.csproj -c Release"
}

# Both files carry the emitter; verify that before touching anything.
if (-not (Select-String -Path $csFrom -Pattern 'ExportDay' -Quiet)) {
    throw "$csFrom has no ExportDay - wrong source, refusing"
}

$backup = Join-Path $repo ('nt\backups\cert-update-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $backup | Out-Null
Copy-Item (Join-Path $custom 'GuardianCore.dll') $backup -ErrorAction SilentlyContinue
Copy-Item (Join-Path $addons 'DeadmanGuardianAddOn.cs') $backup -ErrorAction SilentlyContinue
Write-Host "backed up the previous pair to $backup"

Copy-Item $dllFrom (Join-Path $custom 'GuardianCore.dll') -Force
Copy-Item $csFrom  (Join-Path $addons 'DeadmanGuardianAddOn.cs') -Force

Write-Host ''
Write-Host 'Deployed together:' -ForegroundColor Green
Write-Host "  GuardianCore.dll         -> $custom"
Write-Host "  DeadmanGuardianAddOn.cs  -> $addons"
Write-Host ''
Write-Host 'Now: open NinjaTrader, NinjaScript Editor, open DeadmanGuardianAddOn, press F5, restart.'
Write-Host 'The guardian window gains an "Export my day" button under Arm.'
Write-Host ''
Write-Host 'If F5 fails, restore the pair from the backup above before doing anything else.'
