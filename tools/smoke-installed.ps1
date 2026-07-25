# Validate the per-user installed MacWidget as a complete product startup.
# This is read-only by default; use -StartIfNeeded to launch a stopped app.
[CmdletBinding()]
param(
    [string]$AppPath = (Join-Path $env:LOCALAPPDATA 'Programs\MacWidget\MacWidget.exe'),
    [ValidateRange(3, 60)]
    [int]$ReadyTimeoutSeconds = 20,
    [switch]$StartIfNeeded,
    [switch]$RequireMacDeskLink,
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

$logPath = Join-Path $env:LOCALAPPDATA 'MacWidget\macwidget.log'
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw "Startup log was not found: $logPath"
}
$logTail = @(Get-Content -LiteralPath $logPath -Tail 300 -ErrorAction Stop)
$webViewReady = @($logTail | Where-Object { $_ -like '*webview2 env ready*' }).Count -gt 0
$trayReady = @($logTail | Where-Object { $_ -like '*tray ready*' }).Count -gt 0
$macDeskLinked = @($logTail | Where-Object { $_ -like '*widgetlink connected to MacDesk*' }).Count -gt 0
if (-not $webViewReady) { throw 'The startup log has no WebView2-ready signal.' }
if (-not $trayReady) { throw 'The startup log has no tray-ready signal.' }
if ($RequireMacDeskLink -and -not $macDeskLinked) {
    throw 'MacDesk link was required, but the startup log has no successful pipe connection signal.'
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
    AppVersion         = $appItem.VersionInfo.ProductVersion
    ProcessId          = $processes[0].ProcessId
    WebView2Runtime    = $webViewRuntime
    WebView2Ready      = $webViewReady
    TrayReady          = $trayReady
    MacDeskLinked      = $macDeskLinked
    WeatherHttpStatus  = $weatherStatus
    WeatherBytes       = $weatherBytes
    LogPath            = $logPath
}
