# Verify an extracted private Steam content artifact against its CI SHA-256 manifest.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ContentRoot,
    [Parameter(Mandatory)]
    [string]$ChecksumPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ContentRoot -PathType Container)) {
    throw "Steam content root was not found: $ContentRoot"
}
if (-not (Test-Path -LiteralPath $ChecksumPath -PathType Leaf)) {
    throw "Steam content checksum manifest was not found: $ChecksumPath"
}

$resolvedRoot = [System.IO.Path]::GetFullPath($ContentRoot).TrimEnd('\', '/')
$rootPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
$manifestLines = @(Get-Content -LiteralPath $ChecksumPath | Where-Object { $_.Trim().Length -gt 0 })
if ($manifestLines.Count -eq 0) {
    throw 'Steam content checksum manifest is empty.'
}

$seen = @{}
$verifiedFiles = 0
foreach ($line in $manifestLines) {
    $entry = [regex]::Match($line, '^(?<hash>[A-Fa-f0-9]{64}) \*(?<path>.+)$')
    if (-not $entry.Success) {
        throw "Invalid Steam content checksum line: $line"
    }
    $relativePath = $entry.Groups['path'].Value
    if ([System.IO.Path]::IsPathRooted($relativePath) -or $seen.ContainsKey($relativePath)) {
        throw "Unsafe or duplicate Steam content checksum path: $relativePath"
    }
    $seen[$relativePath] = $true
    $candidate = Join-Path $resolvedRoot ($relativePath.Replace('/', '\'))
    $fullPath = [System.IO.Path]::GetFullPath($candidate)
    if (-not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Steam content checksum path escapes its root: $relativePath"
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Steam content file is missing: $relativePath"
    }
    $expectedHash = $entry.Groups['hash'].Value.ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "SHA-256 mismatch for Steam content file $relativePath. Expected $expectedHash, got $actualHash."
    }
    $verifiedFiles++
}

[pscustomobject][ordered]@{
    ContentRoot = $resolvedRoot
    ChecksumPath = [System.IO.Path]::GetFullPath($ChecksumPath)
    VerifiedFiles = $verifiedFiles
    Verified = $true
}
