# Validate the per-user installed MacWidget as a complete product startup.
# This is read-only by default; use -StartIfNeeded to launch a stopped app.
[CmdletBinding()]
param(
    [string]$AppPath = (Join-Path $env:LOCALAPPDATA 'Programs\MacWidget\MacWidget.exe'),
    [ValidateRange(3, 60)]
    [int]$ReadyTimeoutSeconds = 20,
    [switch]$StartIfNeeded,
    [switch]$RequireMacDeskLink,
    [switch]$RequireMultipleDisplays,
    [switch]$ExerciseRestart,
    [string]$ExpectedVersion,
    [switch]$SkipNetwork
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$webViewClientId = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$runtimeKeys = @(
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\$webViewClientId",
    "HKCU:\Software\Microsoft\EdgeUpdate\Clients\$webViewClientId"
)

function Get-WebView2RuntimeVersion {
    foreach ($key in $runtimeKeys) {
        $version = (Get-ItemProperty -LiteralPath $key -Name pv -ErrorAction SilentlyContinue).pv
        if ($version) { return [string]$version }
    }
    return $null
}

function Get-MacWidgetProcesses {
    return @(Get-CimInstance Win32_Process -Filter "Name='MacWidget.exe'" |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath -ieq $script:resolvedAppPath })
}

if (-not (Test-Path -LiteralPath $AppPath -PathType Leaf)) {
    throw "Installed application was not found: $AppPath"
}
$resolvedAppPath = [System.IO.Path]::GetFullPath($AppPath)
$appItem = Get-Item -LiteralPath $resolvedAppPath
$appVersion = [string]$appItem.VersionInfo.ProductVersion
$bundledRuntimeFiles = @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')
$missingRuntimeFiles = @($bundledRuntimeFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $appItem.DirectoryName $_) -PathType Leaf)
})
if ($missingRuntimeFiles.Count -gt 0) {
    throw "Installed app is missing self-contained .NET runtime files: $($missingRuntimeFiles -join ', ')"
}
$requiredNoticeFiles = @('THIRD-PARTY-NOTICES.md', 'PRIVACY.md')
$missingNoticeFiles = @($requiredNoticeFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $appItem.DirectoryName $_) -PathType Leaf)
})
if ($missingNoticeFiles.Count -gt 0) {
    throw "Installed app is missing product notice files: $($missingNoticeFiles -join ', ')"
}
if ($ExpectedVersion -and -not $appVersion.StartsWith($ExpectedVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Installed app version '$appVersion' does not start with expected version '$ExpectedVersion'."
}
$webViewRuntime = Get-WebView2RuntimeVersion
if (-not $webViewRuntime) {
    throw 'Microsoft Edge WebView2 Runtime was not found. Re-run the MacWidget installer to install it.'
}

$processes = @(Get-MacWidgetProcesses)
if ($processes.Count -eq 0 -and $StartIfNeeded) {
    Start-Process -FilePath $resolvedAppPath -WorkingDirectory $appItem.DirectoryName
    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 250
        $processes = @(Get-MacWidgetProcesses)
    } while ($processes.Count -eq 0 -and (Get-Date) -lt $deadline)
}
if ($processes.Count -ne 1) {
    $state = if ($processes.Count -eq 0) { 'not running (pass -StartIfNeeded to launch it)' } else { "$($processes.Count) instances were found" }
    throw "The installed app must have exactly one MacWidget.exe instance: $state"
}
$originalProcessId = $processes[0].ProcessId
$restartExercised = $false
if ($ExerciseRestart) {
    Start-Process -FilePath $resolvedAppPath -ArgumentList '--restart' -WorkingDirectory $appItem.DirectoryName
    $restartDeadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 250
        $processes = @(Get-MacWidgetProcesses)
    } while (($processes.Count -ne 1 -or $processes[0].ProcessId -eq $originalProcessId) -and (Get-Date) -lt $restartDeadline)
    if ($processes.Count -ne 1 -or $processes[0].ProcessId -eq $originalProcessId) {
        $state = if ($processes.Count -eq 0) { 'not running' } elseif ($processes.Count -gt 1) { "$($processes.Count) instances were found" } else { "process id stayed $originalProcessId" }
        throw "The installed app did not complete a safe restart: $state"
    }
    $restartExercised = $true
}

$logPath = Join-Path $env:LOCALAPPDATA 'MacWidget\macwidget.log'
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw "Startup log was not found: $logPath"
}
$webViewReady = $false
$trayReady = $false
$macDeskLinked = $false
$topology = $null
$topologyDisplayCount = 0
$currentStartupLog = @()
$startupIndexes = @()
$logDeadline = (Get-Date).AddSeconds($ReadyTimeoutSeconds)
do {
    $logTail = @(Get-Content -LiteralPath $logPath -Tail 2000 -ErrorAction Stop)
    $startupIndexes = @(
        for ($index = 0; $index -lt $logTail.Count; $index++) {
            if ($logTail[$index] -like '*=== start:*') { $index }
        }
    )
    if ($startupIndexes.Count -gt 0) {
        $lastStartupIndex = $startupIndexes[-1]
        $currentStartupLog = @($logTail[$lastStartupIndex..($logTail.Count - 1)])
        $webViewReady = @($currentStartupLog | Where-Object { $_ -like '*webview2 env ready*' }).Count -gt 0
        $trayReady = @($currentStartupLog | Where-Object { $_ -like '*tray ready*' }).Count -gt 0
        $macDeskLinked = @($currentStartupLog | Where-Object { $_ -like '*widgetlink connected to MacDesk*' }).Count -gt 0
        $topologyLine = @($currentStartupLog | Where-Object { $_ -like '*display topology stable:*' } | Select-Object -Last 1)
        if ($topologyLine.Count -gt 0) {
            $topology = ($topologyLine[0] -replace '^.*display topology stable:\s*', '').Trim()
            if ($topology) {
                $topologyDisplayCount = @($topology -split '\|' | Where-Object { $_.Trim().Length -gt 0 }).Count
            }
        }
    }
    $ready = $webViewReady -and $trayReady -and ((-not $RequireMacDeskLink) -or $macDeskLinked)
    if (-not $ready -and (Get-Date) -lt $logDeadline) { Start-Sleep -Milliseconds 250 }
} while (-not $ready -and (Get-Date) -lt $logDeadline)
if ($startupIndexes.Count -eq 0) {
    throw 'The startup log has no MacWidget start marker.'
}
if (-not $webViewReady) { throw 'The startup log has no WebView2-ready signal.' }
if (-not $trayReady) { throw 'The startup log has no tray-ready signal.' }
if ($RequireMacDeskLink -and -not $macDeskLinked) {
    throw 'MacDesk link was required, but the startup log has no successful pipe connection signal.'
}
if ($RequireMultipleDisplays -and $topologyDisplayCount -lt 2) {
    $observedTopology = if ($topology) { $topology } else { 'no stable topology entry was written to the startup log' }
    throw "At least two active displays were required, but MacWidget observed $topologyDisplayCount. Topology: $observedTopology"
}

$weatherStatus = 'skipped'
$weatherBytes = 0
if (-not $SkipNetwork) {
    try {
        $weather = Invoke-WebRequest `
            -Uri 'https://api.met.no/weatherapi/locationforecast/2.0/compact?lat=59.9139&lon=10.7522' `
            -UserAgent 'MacWidget/0.2 (+https://github.com/Nishikinonakai/MacWidget)' `
            -UseBasicParsing -TimeoutSec 15
        $weatherStatus = [int]$weather.StatusCode
        $weatherBytes = [int]$weather.RawContentLength
        if ($weather.StatusCode -ne 200 -or $weather.RawContentLength -le 0) {
            throw "HTTP $($weather.StatusCode), response length $($weather.RawContentLength)"
        }
    }
    catch {
        throw "MET Norway weather connectivity check failed: $($_.Exception.Message)"
    }
}

[pscustomobject][ordered]@{
    AppPath            = $resolvedAppPath
    AppVersion         = $appVersion
    BundledRuntimeFiles = $bundledRuntimeFiles -join ', '
    ProductNoticeFiles = $requiredNoticeFiles -join ', '
    OriginalProcessId  = $originalProcessId
    ProcessId          = $processes[0].ProcessId
    RestartExercised   = $restartExercised
    WebView2Runtime    = $webViewRuntime
    WebView2Ready      = $webViewReady
    TrayReady          = $trayReady
    MacDeskLinked      = $macDeskLinked
    Topology           = $topology
    TopologyDisplayCount = $topologyDisplayCount
    StartupLogEntries  = $currentStartupLog.Count
    WeatherHttpStatus  = $weatherStatus
    WeatherBytes       = $weatherBytes
    LogPath            = $logPath
}
