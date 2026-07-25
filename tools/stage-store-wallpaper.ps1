# Safely stage and restore a neutral wallpaper for an approved MacWidget store screenshot session.
# With no switches this script is read-only. Applying a wallpaper requires -Apply and a local file path.
[CmdletBinding(DefaultParameterSetName = 'Status')]
param(
    [Parameter(ParameterSetName = 'Apply', Mandatory = $true)]
    [switch]$Apply,
    [Parameter(ParameterSetName = 'Restore', Mandatory = $true)]
    [switch]$Restore,
    [Parameter(ParameterSetName = 'Apply', Mandatory = $true)]
    [string]$WallpaperPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$desktopKey = 'HKCU:\Control Panel\Desktop'
$sessionRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'MacWidget\store-screenshot-wallpaper'
$statePath = Join-Path $sessionRoot 'state.json'

function Install-WallpaperNative {
    if ('MacWidgetStoreWallpaperNative' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MacWidgetStoreWallpaperNative
{
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x0001;
    private const uint SPIF_SENDCHANGE = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint uiParam, string pvParam, uint flags);

    public static void SetWallpaper(string path)
    {
        if (!SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
}
'@
}

function Get-DesktopState {
    $desktop = Get-ItemProperty -LiteralPath $desktopKey
    return [pscustomobject][ordered]@{
        Wallpaper      = [string]$desktop.WallPaper
        WallpaperStyle = [string]$desktop.WallpaperStyle
        TileWallpaper  = [string]$desktop.TileWallpaper
    }
}

function Get-Status {
    $desktop = Get-DesktopState
    $state = $null
    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    }
    [pscustomobject][ordered]@{
        ActiveSession       = $null -ne $state
        SessionRoot         = $sessionRoot
        CurrentWallpaper    = $desktop.Wallpaper
        CurrentStyle        = $desktop.WallpaperStyle
        CurrentTile         = $desktop.TileWallpaper
        OriginalWallpaper   = if ($state) { [string]$state.OriginalWallpaper } else { $null }
        BackupWallpaper     = if ($state) { [string]$state.BackupWallpaper } else { $null }
        StagedWallpaper     = if ($state) { [string]$state.StagedWallpaper } else { $null }
    }
}

function Restore-FromState {
    param([Parameter(Mandatory)]$State)

    $restorePath = if (Test-Path -LiteralPath ([string]$State.OriginalWallpaper) -PathType Leaf) {
        [string]$State.OriginalWallpaper
    }
    elseif (Test-Path -LiteralPath ([string]$State.BackupWallpaper) -PathType Leaf) {
        [string]$State.BackupWallpaper
    }
    else {
        throw 'Neither the original wallpaper path nor the session backup is available.'
    }

    Set-ItemProperty -LiteralPath $desktopKey -Name WallpaperStyle -Value ([string]$State.WallpaperStyle)
    Set-ItemProperty -LiteralPath $desktopKey -Name TileWallpaper -Value ([string]$State.TileWallpaper)
    Install-WallpaperNative
    [MacWidgetStoreWallpaperNative]::SetWallpaper($restorePath)
    return $restorePath
}

if ($Apply) {
    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        throw "A screenshot wallpaper session is already active at $sessionRoot. Run with -Restore before applying another wallpaper."
    }
    if (-not (Test-Path -LiteralPath $WallpaperPath -PathType Leaf)) {
        throw "Wallpaper file was not found: $WallpaperPath"
    }

    $original = Get-DesktopState
    if ([string]::IsNullOrWhiteSpace($original.Wallpaper) -or -not (Test-Path -LiteralPath $original.Wallpaper -PathType Leaf)) {
        throw 'The current wallpaper has no recoverable local file path. Do not stage a screenshot wallpaper automatically; restore it manually after capture instead.'
    }

    $resolvedStagedWallpaper = (Resolve-Path -LiteralPath $WallpaperPath).Path
    New-Item -ItemType Directory -Path $sessionRoot -Force | Out-Null
    $extension = [IO.Path]::GetExtension($original.Wallpaper)
    if ([string]::IsNullOrWhiteSpace($extension)) { $extension = '.img' }
    $backupWallpaper = Join-Path $sessionRoot ("original" + $extension)
    Copy-Item -LiteralPath $original.Wallpaper -Destination $backupWallpaper -Force

    $state = [pscustomobject][ordered]@{
        SchemaVersion  = 1
        CreatedAtUtc   = [DateTime]::UtcNow.ToString('o')
        OriginalWallpaper = $original.Wallpaper
        BackupWallpaper   = $backupWallpaper
        StagedWallpaper   = $resolvedStagedWallpaper
        WallpaperStyle    = $original.WallpaperStyle
        TileWallpaper     = $original.TileWallpaper
    }
    $state | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8

    try {
        Set-ItemProperty -LiteralPath $desktopKey -Name WallpaperStyle -Value '10'
        Set-ItemProperty -LiteralPath $desktopKey -Name TileWallpaper -Value '0'
        Install-WallpaperNative
        [MacWidgetStoreWallpaperNative]::SetWallpaper($resolvedStagedWallpaper)
    }
    catch {
        $recovered = $false
        try {
            [void](Restore-FromState -State $state)
            $recovered = $true
        }
        catch { }
        if ($recovered) {
            Remove-Item -LiteralPath $sessionRoot -Recurse -Force
        }
        throw
    }

    Get-Status
    return
}

if ($Restore) {
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        throw "No screenshot wallpaper session state was found at $statePath."
    }
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $restoredWallpaper = Restore-FromState -State $state
    Remove-Item -LiteralPath $sessionRoot -Recurse -Force
    [pscustomobject][ordered]@{
        RestoredWallpaper = $restoredWallpaper
        BackupRemoved     = $true
        SessionRoot       = $sessionRoot
    }
    return
}

Get-Status
