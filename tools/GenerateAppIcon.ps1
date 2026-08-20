<#
.SYNOPSIS
    Generates PromptHelper.ico from PromptHelperLogo.svg using ImageMagick.
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

Write-Host "Converting SVG to multi-resolution ICO..."
& magick -background none $sourceSvg -define icon:auto-resize=256,128,64,48,32,24,16 $outputIco

if (-not (Test-Path $outputIco) -or ((Get-Item $outputIco).Length -eq 0)) {
    Write-Error "ICO generation failed: output file is missing or empty."
    exit 1
}

Write-Host "Successfully generated '$outputIco'."
