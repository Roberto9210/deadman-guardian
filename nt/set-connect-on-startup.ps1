# Sets <ConnectOnStartup>true</ConnectOnStartup> on ONE named NinjaTrader connection.
#
# Why this is a script and not "edit the XML": the field name is not unique and the file does not
# nest the way it looks. Config.xml serialises each connection's members alphabetically -
# ConnectOnStartup, Name, Provider - so the ConnectOnStartup that governs a connection sits
# IMMEDIATELY BEFORE its <Name>, and the one right after <Provider>Simulator</Provider> already
# belongs to the NEXT connection. A search-and-replace of false -> true, or a sed anchored on the
# name, flips the wrong connection. On this machine the neighbour is "Playback Connection", and
# enabling that one would start the platform on the Playback101 account - the exact account the bots'
# rails refuse, and the guardian does not watch.
#
# So: parse the XML, find the element whose <Name> child equals the target, change that element's own
# ConnectOnStartup, write, then READ IT BACK and verify. Never trust your own write.
#
#     powershell -ExecutionPolicy Bypass -File nt\set-connect-on-startup.ps1
#     powershell -ExecutionPolicy Bypass -File nt\set-connect-on-startup.ps1 -Connection "..." -Value false
#
# WHY THIS MATTERS MORE THAN DATA RELIABILITY: IT ISOLATES THE FUNDED ACCOUNT.
#
# NinjaTrader refuses to hold two of these connections at once - its own words, from this machine's
# log: "Simulation: You can't connect while having an open 'Simulated Data Feed' connection (Panic)".
# That mutual exclusion is the point.
#
# The connection that runs when this one does not is "Simulation", and it brings the FUNDED account
# online: every one of the 16 soak runs recorded
#     Account.All = [Backtest/Simulator, Playback101/Playback, Sim101/Simulator, 2127534/Provider31]
# The last of those is real money. With the platform in that state we were one gate file away from
# arming a bot whose stated purpose is to lose, in a session where the funded account was present.
#
# The bots' rails would still have refused it - VerifyAccount demands the name Sim101 AND
# Provider == Simulator - but "a check would have caught it" is a worse guarantee than "it was not
# there". Connecting the Simulated Data Feed takes 2127534 out of the session entirely, so the funded
# account stops being something a check has to save us from.
#
# So this script is a safety control, not a convenience. Reliability of the data is the smaller half.
#
# THE RISK, written down rather than assumed: NinjaTrader's own support recommends against connecting
# on startup, because a connection that hangs can hang the platform's startup with it. That warning is
# about NETWORK connections waiting on a broker or data vendor. The Simulated Data Feed generates its
# market internally - it opens no socket and has nothing to wait for - so the failure it warns about
# has no mechanism here. Enabling it on any other connection deserves that warning in full.
#
# AND THE COROLLARY, for whoever operates this: after running this script, do not click "Simulation"
# in the Connections menu. It is mutually exclusive with the feed this sets, and choosing it puts the
# funded account back in the session.

param(
    [string]$Connection = "Simulated Data Feed",
    [ValidateSet("true", "false")][string]$Value = "true"
)

$ErrorActionPreference = "Stop"

$ntUser = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "NinjaTrader 8"
$config = Join-Path $ntUser "Config.xml"

# ---------------------------------------------------------------- NinjaTrader must be closed
# It rewrites Config.xml from memory when it exits, so a change made while it runs is overwritten
# without a trace. Same loud shape as install.ps1, and for the same reason: an error nobody notices
# is, in its result, an error that did not happen.
$ntProc = Get-Process NinjaTrader -ErrorAction SilentlyContinue
if ($ntProc) {
    $bar = "=" * 76
    Write-Host ""
    Write-Host $bar -ForegroundColor Red
    Write-Host ""
    Write-Host "     N O T H I N G   W A S   C H A N G E D" -ForegroundColor Red
    Write-Host ""
    Write-Host "     Config.xml was not touched. No backup was made." -ForegroundColor Red
    Write-Host ""
    Write-Host $bar -ForegroundColor Red
    Write-Host ""
    Write-Host ("  Why: NinjaTrader is running (PID " + ($ntProc.Id -join ", ") + ").")
    Write-Host "  It rewrites Config.xml from memory when it exits, so anything written now"
    Write-Host "  would be silently overwritten on the next shutdown."
    Write-Host ""
    Write-Host "  Close NinjaTrader - including the SYSTEM TRAY icon beside the clock, behind"
    Write-Host "  the up-arrow (^): right-click it, Exit - and run this again."
    Write-Host ""
    Write-Host $bar -ForegroundColor Red
    Write-Host ""
    exit 2
}

if (-not (Test-Path $config)) { throw "not found: $config" }

# ---------------------------------------------------------------- backup first, dated
$stamp  = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $ntUser ("Config.xml.before-connectonstartup-" + $stamp)
Copy-Item $config $backup -Force
"backed up: $backup"

# ---------------------------------------------------------------- parse, locate, change
$doc = New-Object System.Xml.XmlDocument
$doc.PreserveWhitespace = $true          # keep the file byte-identical apart from the value itself
$doc.Load($config)

$nodes = @($doc.SelectNodes("//*[Name='$Connection']"))
if ($nodes.Count -eq 0) { throw "no element in Config.xml has a <Name> child equal to '$Connection'" }
if ($nodes.Count -gt 1) { throw "$($nodes.Count) elements match <Name>='$Connection' - refusing to guess which" }

$node = $nodes[0]
$field = $node.SelectSingleNode("ConnectOnStartup")
if ($null -eq $field) { throw "'$Connection' has no ConnectOnStartup child - refusing to invent one" }

"found: <$($node.LocalName)> with <Name>$Connection</Name>, ConnectOnStartup = $($field.InnerText)"
$field.InnerText = $Value
$doc.Save($config)
"written: ConnectOnStartup = $Value"

# ---------------------------------------------------------------- read it back; do not trust the write
""
"re-reading from disk to verify:"
$check = New-Object System.Xml.XmlDocument
$check.Load($config)                      # throws if the file no longer parses

$rows = @()
foreach ($n in $check.SelectNodes("//*[ConnectOnStartup and Name]")) {
    $rows += [PSCustomObject]@{
        Name             = $n.SelectSingleNode("Name").InnerText
        ConnectOnStartup = $n.SelectSingleNode("ConnectOnStartup").InnerText
    }
}
foreach ($r in $rows) {
    $mark = if ($r.Name -eq $Connection) { "  <-- target" } else { "" }
    "    {0,-34} {1}{2}" -f $r.Name, $r.ConnectOnStartup, $mark
}

$target = $rows | Where-Object { $_.Name -eq $Connection }
$others = $rows | Where-Object { $_.Name -ne $Connection }

$ok = ($target -and $target.ConnectOnStartup -eq $Value) -and
      (($others | Where-Object { $_.ConnectOnStartup -ne "false" }).Count -eq 0)

""
if ($ok) {
    "VERIFIED: '$Connection' is $Value and every other connection is still false."
    "The XML re-parsed cleanly. Backup kept at:"
    "  $backup"
} else {
    Write-Host "*** VERIFICATION FAILED - restoring the backup ***" -ForegroundColor Red
    Copy-Item $backup $config -Force
    "restored $config from $backup"
    exit 3
}
