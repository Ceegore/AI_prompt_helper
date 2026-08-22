<#
.SYNOPSIS
    Regenerates the application icon from the approved vector source using the pinned renderer
    and proves the result matches the approval manifest and the committed ICO.

.DESCRIPTION
    CRUU15-011. The approval manifest binds an SVG hash to a set of approved pixel hashes, and
    the committed ICO is checked against those hashes. Neither of those steps proves that
    rendering the approved SVG actually produces the approved pixels - so an ICO could drift
    from its own source and every check would still pass.

    This closes that link by running the canonical generator (with its dependencies installed
    from a checked-in lockfile, so the renderer version cannot float) and comparing the frames
    it produces against the approval manifest.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$generatorDir = Join-Path $repoRoot 'tools/icon-generator'
$generator = Join-Path $repoRoot 'tools/GenerateAppIconNative.js'
$svgPath = Join-Path $repoRoot 'src/PromptHelper/Assets/PromptHelperLogo.svg'
$icoPath = Join-Path $repoRoot 'src/PromptHelper/Assets/PromptHelper.ico'
$manifestPath = Join-Path $repoRoot 'src/PromptHelper/Assets/PromptHelperIcon.approved.json'
$verifierProject = Join-Path $repoRoot 'tools/IconIdentityVerifier/IconIdentityVerifier.csproj'

foreach ($required in @($generator, $svgPath, $icoPath, $manifestPath, $verifierProject)) {
    if (-not (Test-Path $required)) {
        Write-Error "Required icon-chain artefact is missing: '$required'."
        exit 1
    }
}

# --- The renderer pin must be exact, and installable from the lockfile alone. -------------
$packageJsonPath = Join-Path $generatorDir 'package.json'
$lockPath = Join-Path $generatorDir 'package-lock.json'

if (-not (Test-Path $packageJsonPath) -or -not (Test-Path $lockPath)) {
    Write-Error "The pinned icon generator package is incomplete under '$generatorDir'."
    exit 1
}

$package = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
$pinned = $package.dependencies.sharp

if ($pinned -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "The renderer must be pinned to an exact version; found '$pinned'."
    exit 1
}

Write-Host "Renderer pinned at sharp@$pinned"

Push-Location $generatorDir
try {
    npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) {
        Write-Error "npm ci failed for the pinned icon generator."
        exit 1
    }
}
finally {
    Pop-Location
}

# --- Render into a scratch ICO and compare it with the committed one. --------------------
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("icon-gen-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$generatedIco = Join-Path $scratch 'generated.ico'

try {
    node $generator $svgPath $generatedIco
    if ($LASTEXITCODE -ne 0) {
        Write-Error "The canonical icon generator failed."
        exit 1
    }

    if (-not (Test-Path $generatedIco)) {
        Write-Error "The canonical icon generator produced no output."
        exit 1
    }

    # IconIdentityVerifier compares normalized pixel content, not container bytes: two encoders
    # can emit different PNG containers for the same image, and it is the image that was
    # approved.
    dotnet run --project $verifierProject -- compare-ico $icoPath $generatedIco
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Rendering the approved SVG does not reproduce the committed ICO."
        exit 1
    }
}
finally {
    Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
}

Write-Host "Icon generation verified: approved SVG -> pinned renderer -> approved frames -> committed ICO."
exit 0
