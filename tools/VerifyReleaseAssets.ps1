<#
.SYNOPSIS
    Verifies that required release assets are present and valid.
#>
[CmdletBinding()]
param(
    [switch]$RequireIcon,
    [string]$PublishedExe
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$sourceSvg = Join-Path $repoRoot "src\PromptHelper\Assets\PromptHelperLogo.svg"
$outputIco = Join-Path $repoRoot "src\PromptHelper\Assets\PromptHelper.ico"

if ($RequireIcon) {
    if (-not (Test-Path $sourceSvg)) {
        Write-Error "MISSING_REQUIRED_ASSET: Prompt Helper logo SVG ('$sourceSvg')"
        exit 1
    }

    if (-not (Test-Path $outputIco)) {
        Write-Error "MISSING_REQUIRED_ASSET: Prompt Helper ICO ('$outputIco'). Run tools/GenerateAppIcon.ps1."
        exit 1
    }
} else {
    if (-not (Test-Path $sourceSvg)) {
        Write-Host "ICON ASSET: NOT PRESENT -- release icon validation deferred"
    }
}

if (Test-Path $outputIco) {
    $bytes = [System.IO.File]::ReadAllBytes($outputIco)
    if ($bytes.Length -lt 6) {
        Write-Error "ICO file is too small."
        exit 1
    }

    $reserved = [System.BitConverter]::ToUInt16($bytes, 0)
    $type = [System.BitConverter]::ToUInt16($bytes, 2)
    $count = [System.BitConverter]::ToUInt16($bytes, 4)

    if ($reserved -ne 0 -or $type -ne 1 -or $count -lt 7) {
        Write-Error "Invalid ICO header: reserved=$reserved, type=$type, count=$count"
        exit 1
    }

    $directoryLength = 6 + ($count * 16)
    if ($bytes.Length -lt $directoryLength) {
        Write-Error "ICO directory table is truncated."
        exit 1
    }

    $sizes = [System.Collections.Generic.HashSet[int]]::new()
    for ($i = 0; $i -lt $count; $i++) {
        $offset = 6 + ($i * 16)
        $w = if ($bytes[$offset] -eq 0) { 256 } else { [int]$bytes[$offset] }
        $h = if ($bytes[$offset + 1] -eq 0) { 256 } else { [int]$bytes[$offset + 1] }

        if ($w -ne $h) {
            Write-Error "ICO frame $i is not square: ${w}x${h}"
            exit 1
        }
        $sizes.Add($w) | Out-Null

        $imageSize = [System.BitConverter]::ToUInt32($bytes, $offset + 8)
        $imageOffset = [System.BitConverter]::ToUInt32($bytes, $offset + 12)

        if ($imageSize -eq 0) {
            Write-Error "ICO frame $i has zero image size."
            exit 1
        }

        if ($imageOffset -lt $directoryLength) {
            Write-Error "ICO frame $i points inside the directory table."
            exit 1
        }

        $end = [UInt64]$imageOffset + [UInt64]$imageSize
        if ($end -gt [UInt64]$bytes.Length) {
            Write-Error "ICO frame $i extends beyond end of file."
            exit 1
        }
    }

    $requiredSizes = @(16, 24, 32, 48, 64, 128, 256)
    foreach ($req in $requiredSizes) {
        if (-not $sizes.Contains($req)) {
            Write-Error "ICO is missing required ${req}x${req} frame."
            exit 1
        }
    }

    Write-Host "Validated PromptHelper.ico with $count square frames."
}

if ($RequireIcon -and $PublishedExe) {
    if (-not (Test-Path $PublishedExe)) {
        Write-Error "Published executable not found: '$PublishedExe'"
        exit 1
    }

    $resolvedExe = (Resolve-Path $PublishedExe).Path

    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class PromptHelperNativeIconCheck
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconEx(
        string szFileName,
        int nIconIndex,
        IntPtr phiconLarge,
        IntPtr phiconSmall,
        uint nIcons);
}
"@

    $iconCount = [PromptHelperNativeIconCheck]::ExtractIconEx(
        $resolvedExe,
        -1,
        [IntPtr]::Zero,
        [IntPtr]::Zero,
        0)

    if ($iconCount -lt 1) {
        Write-Error "Published PromptHelper.exe contains no embedded icon resources."
        exit 1
    }

    Write-Host "Published EXE exposes $iconCount embedded icon group(s)."
}

Write-Host "Release asset verification completed successfully."
