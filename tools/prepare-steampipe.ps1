# Generate SteamPipe VDF scripts for a self-contained MacWidget publish directory.
# This script never invokes steamcmd and never accepts Steam credentials.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+$')]
    [string]$AppId,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+$')]
    [string]$DepotId,
    [Parameter(Mandatory)]
    [string]$ContentRoot,
    [Parameter(Mandatory)]
    [string]$SteamPipeScriptsDir,
    [Parameter(Mandatory)]
    [string]$BuildOutputDir,
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.+_-]*$')]
    [string]$Version,
    [switch]$Preview
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ContentRoot -PathType Container)) {
    throw "SteamPipe content root was not found: $ContentRoot"
}
$contentPath = [System.IO.Path]::GetFullPath($ContentRoot)
$requiredFiles = @('MacWidget.exe', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'THIRD-PARTY-NOTICES.md', 'PRIVACY.md')
$missingFiles = @($requiredFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $contentPath $_) -PathType Leaf)
})
if ($missingFiles.Count -gt 0) {
    throw "SteamPipe content root is incomplete: $($missingFiles -join ', ')"
}

if (Test-Path -LiteralPath $SteamPipeScriptsDir -PathType Leaf) {
    throw "SteamPipe scripts path is a file: $SteamPipeScriptsDir"
}
if (-not (Test-Path -LiteralPath $SteamPipeScriptsDir -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $SteamPipeScriptsDir)
}
$scriptsPath = [System.IO.Path]::GetFullPath($SteamPipeScriptsDir)
$outputPath = [System.IO.Path]::GetFullPath($BuildOutputDir)
$appScript = Join-Path $scriptsPath "app_build_$AppId.vdf"
$depotScript = Join-Path $scriptsPath "depot_build_$DepotId.vdf"
foreach ($path in @($appScript, $depotScript)) {
    if (Test-Path -LiteralPath $path) {
        throw "Refusing to overwrite existing SteamPipe script: $path"
    }
}

$previewLine = if ($Preview) { "`t`"Preview`" `"1`"`r`n" } else { '' }
$appVdf = @"
"AppBuild"
{
    "AppID" "$AppId"
    "Desc" "MacWidget $Version"
$previewLine    "ContentRoot" "$contentPath"
    "BuildOutput" "$outputPath"
    "Depots"
    {
        "$DepotId" "$(Split-Path -Leaf $depotScript)"
    }
}
"@
$depotVdf = @"
"DepotBuild"
{
    "DepotID" "$DepotId"
    "FileMapping"
    {
        "LocalPath" "*"
        "DepotPath" "."
        "recursive" "1"
    }
    "FileExclusion" "*.pdb"
}
"@

Set-Content -LiteralPath $appScript -Value $appVdf -Encoding UTF8 -NoNewline
Set-Content -LiteralPath $depotScript -Value $depotVdf -Encoding UTF8 -NoNewline

[pscustomobject][ordered]@{
    AppId = $AppId
    DepotId = $DepotId
    Preview = [bool]$Preview
    ContentRoot = $contentPath
    AppBuildScript = $appScript
    DepotBuildScript = $depotScript
    SteamCmdCommand = "steamcmd.exe +login <build-account> +run_app_build `"$appScript`" +quit"
}
