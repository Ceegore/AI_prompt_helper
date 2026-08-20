<#
.SYNOPSIS
    Generates PromptHelper.ico from PromptHelperLogo.svg using ImageMagick with aspect-safe square padding.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$sourceSvg = Join-Path $repoRoot "src\PromptHelper\Assets\PromptHelperLogo.svg"
$outputIco = Join-Path $repoRoot "src\PromptHelper\Assets\PromptHelper.ico"

Write-Host "Checking repository assets..."
if (-not (Test-Path $sourceSvg)) {
    Write-Error "Source artwork '$sourceSvg' was not found. Please provide PromptHelperLogo.svg before generating the icon."
    exit 1
}

$magickCmd = Get-Command "magick" -ErrorAction SilentlyContinue
if (-not $magickCmd) {
    Write-Error "ImageMagick ('magick') command was not found on PATH. Please install ImageMagick to run icon conversion."
    exit 1
}

$assetsDir = Split-Path $outputIco -Parent
if (-not (Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
}

Write-Host "Converting SVG to multi-resolution square-padded ICO..."
& magick `
    -background none `
    $sourceSvg `
    -resize "256x256" `
    -gravity center `
    -extent "256x256" `
    -define icon:auto-resize=256,128,64,48,32,24,16 `
    $outputIco

if ($LASTEXITCODE -ne 0) {
    Write-Error "ImageMagick conversion exited with non-zero code: $LASTEXITCODE"
    exit 1
}

if (-not (Test-Path $outputIco) -or ((Get-Item $outputIco).Length -lt 6)) {
    Write-Error "ICO generation failed: output file is missing or truncated."
    exit 1
}

# Binary validation of generated ICO
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
}

$requiredSizes = @(16, 24, 32, 48, 64, 128, 256)
foreach ($req in $requiredSizes) {
    if (-not $sizes.Contains($req)) {
        Write-Error "ICO is missing required ${req}x${req} frame."
        exit 1
    }
}

Write-Host "Successfully generated and validated '$outputIco' with $count frames."
