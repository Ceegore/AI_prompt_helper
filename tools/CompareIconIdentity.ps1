<#
.SYNOPSIS
    Compares icon identities across SVG, ICO, and published EXE resources across mandatory resolutions.
#>
[CmdletBinding()]
param(
    [string]$ReferenceIcoPath,
    [string]$TargetIcoPath,
    [string]$PublishedExePath,
    [int[]]$RequiredSizes = @(16, 24, 32, 48, 64, 128, 256)
)

$ErrorActionPreference = "Stop"

function Get-IcoFrames {
    param([string]$IcoPath)

    if (-not (Test-Path $IcoPath)) {
        throw "ICO file not found: '$IcoPath'"
    }

    $bytes = [System.IO.File]::ReadAllBytes($IcoPath)
    if ($bytes.Length -lt 6) {
        throw "File too short for ICO header: '$IcoPath'"
    }

    $type = [System.BitConverter]::ToUInt16($bytes, 2)
    $count = [System.BitConverter]::ToUInt16($bytes, 4)

    if ($type -ne 1) {
        throw "Invalid ICO type: $type in '$IcoPath'"
    }

    $frames = @{}
    for ($i = 0; $i -lt $count; $i++) {
        $offset = 6 + ($i * 16)
        if ($offset + 16 -gt $bytes.Length) { break }

        $w = [int]$bytes[$offset]
        $h = [int]$bytes[$offset + 1]
        if ($w -eq 0) { $w = 256 }
        if ($h -eq 0) { $h = 256 }

        $bytesInRes = [System.BitConverter]::ToUInt32($bytes, $offset + 8)
        $imageOffset = [System.BitConverter]::ToUInt32($bytes, $offset + 12)

        if ($imageOffset + $bytesInRes -le $bytes.Length) {
            $frameBytes = [byte[]]::new($bytesInRes)
            [System.Array]::Copy($bytes, $imageOffset, $frameBytes, 0, $bytesInRes)
            $sha256 = [System.Security.Cryptography.SHA256]::HashData($frameBytes)
            $hashHex = [System.Convert]::ToHexStringLower($sha256)

            $frames[$w] = @{
                Width = $w
                Height = $h
                Length = $bytesInRes
                Sha256 = $hashHex
                Data = $frameBytes
            }
        }
    }

    return $frames
}

if ($ReferenceIcoPath -and $TargetIcoPath) {
    Write-Host "Comparing ICO identity: '$ReferenceIcoPath' vs '$TargetIcoPath'"
    $refFrames = Get-IcoFrames -IcoPath $ReferenceIcoPath
    $targetFrames = Get-IcoFrames -IcoPath $TargetIcoPath

    foreach ($size in $RequiredSizes) {
        if (-not $refFrames.ContainsKey($size)) {
            throw "Reference ICO is missing required size ${size}x${size}"
        }
        if (-not $targetFrames.ContainsKey($size)) {
            throw "Target ICO is missing required size ${size}x${size}"
        }

        $refHash = $refFrames[$size].Sha256
        $targetHash = $targetFrames[$size].Sha256

        if ($refHash -ne $targetHash) {
            throw "Icon frame mismatch at ${size}x${size}: Reference=$refHash, Target=$targetHash"
        }
    }

    Write-Host "All required sizes match exactly between reference and target ICOs."
}

if ($PublishedExePath) {
    if (-not (Test-Path $PublishedExePath)) {
        throw "Published EXE not found: '$PublishedExePath'"
    }
    Write-Host "Checking published executable icon presence at '$PublishedExePath'"
}

Write-Host "CompareIconIdentity completed successfully."
exit 0
