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
    $frameHashes = [System.Collections.Generic.HashSet[string]]::new()
    $sha256 = [System.Security.Cryptography.SHA256]::Create()

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

        if ($RequireIcon) {
            $frameBytes = [byte[]]::new($imageSize)
            [System.Array]::Copy($bytes, $imageOffset, $frameBytes, 0, $imageSize)
            $hashBytes = $sha256.ComputeHash($frameBytes)
            $hashHex = [System.BitConverter]::ToString($hashBytes).Replace("-", "").ToLowerInvariant()

            if ($frameHashes.Contains($hashHex)) {
                Write-Error "ICO frame $i (${w}x${h}) has duplicate image data matching another frame. Frames must be independently rendered at native resolutions."
                exit 1
            }
            $frameHashes.Add($hashHex) | Out-Null
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

    # ExtractIconEx check superseded by exact pixel IconIdentityVerifier
    $resolvedExe = (Resolve-Path $PublishedExe).Path
    $verifierDll = Join-Path $repoRoot "tools\IconIdentityVerifier\bin\Release\net10.0-windows\IconIdentityVerifier.dll"
    if (-not (Test-Path $verifierDll)) {
        $verifierDll = Join-Path $repoRoot "tools\IconIdentityVerifier\bin\Debug\net10.0-windows\IconIdentityVerifier.dll"
    }

    if (Test-Path $verifierDll) {
        Write-Host "Running IconIdentityVerifier compare-exe..."
        dotnet $verifierDll compare-exe $outputIco $resolvedExe
        if ($LASTEXITCODE -ne 0) {
            Write-Error "IconIdentityVerifier failed for published executable."
            exit 1
        }
    } else {
        Write-Host "Building and running IconIdentityVerifier..."
        dotnet run --project (Join-Path $repoRoot "tools\IconIdentityVerifier\IconIdentityVerifier.csproj") -- compare-exe $outputIco $resolvedExe
        if ($LASTEXITCODE -ne 0) {
            Write-Error "IconIdentityVerifier failed for published executable."
            exit 1
        }
    }
}

Write-Host "Release asset verification completed successfully."
