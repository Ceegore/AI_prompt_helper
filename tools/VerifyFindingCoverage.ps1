<#
.SYNOPSIS
    Verifies that every audited finding has behavioral regression coverage, and that the
    generated sentinel list still matches the coverage authority.

.DESCRIPTION
    CRUU15-009. The previous gate checked that every name currently in
    RequiredRegressionTests.psd1 corresponded to a real test method. That direction alone can
    never detect a finding losing its coverage: delete the name from the manifest and the check
    stays green.

    The authority is inverted here. The set of findings that require coverage is derived from
    the checked-in audit reports themselves - a source this repository cannot quietly shrink,
    because shrinking it means editing the report that raised the finding. Each of those IDs
    must appear in tools/FindingCoverageMap.json with at least one named test, and the
    generated sentinel list must be exactly what that map produces.

    Run with -Regenerate after adding tests to rewrite the sentinel list from the map.
#>
[CmdletBinding()]
param(
    [switch]$Regenerate
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$mapPath = Join-Path $repoRoot 'tools/FindingCoverageMap.json'
$manifestPath = Join-Path $repoRoot 'tools/RequiredRegressionTests.psd1'

if (-not (Test-Path $mapPath)) {
    Write-Error "Coverage authority not found: '$mapPath'."
    exit 1
}

$map = Get-Content $mapPath -Raw | ConvertFrom-Json

# --- 1. The universe of findings, from an authority outside the map. ---------------------
$requiredIds = [System.Collections.Generic.SortedSet[string]]::new()

foreach ($property in $map.gatedReports.PSObject.Properties) {
    $prefix = $property.Name
    $reportPath = Join-Path $repoRoot $property.Value

    if (-not (Test-Path $reportPath)) {
        Write-Error "Gated audit report not found: '$reportPath' (for $prefix)."
        exit 1
    }

    $reportText = Get-Content $reportPath -Raw
    foreach ($match in [regex]::Matches($reportText, "$prefix-\d{3}")) {
        [void]$requiredIds.Add($match.Value)
    }
}

if ($requiredIds.Count -eq 0) {
    Write-Error "No finding IDs were found in the gated audit reports; the gate would be vacuous."
    exit 1
}

Write-Host "Findings requiring coverage (from audit reports): $($requiredIds.Count)"

# --- 2. Every required finding is mapped, with at least one test. ------------------------
$uncovered = @()
foreach ($id in $requiredIds) {
    $entry = $map.findings.PSObject.Properties[$id]
    if ($null -eq $entry -or @($entry.Value).Count -eq 0) {
        $uncovered += $id
    }
}

if ($uncovered.Count -gt 0) {
    Write-Error "Findings with no behavioral regression coverage: $($uncovered -join ', ')"
    exit 1
}

# --- 3. Flatten the map into the exact sentinel list. ------------------------------------
$expected = [System.Collections.Generic.List[string]]::new()
foreach ($property in ($map.findings.PSObject.Properties | Sort-Object Name)) {
    foreach ($test in @($property.Value)) {
        [void]$expected.Add($test)
    }
}

if ($expected.Count -eq 0) {
    Write-Error "The coverage map names no tests at all."
    exit 1
}

if ($Regenerate) {
    $lines = [System.Collections.Generic.List[string]]::new()
    [void]$lines.Add('<#')
    [void]$lines.Add('    GENERATED FILE - do not edit by hand.')
    [void]$lines.Add('')
    [void]$lines.Add('    The exact-name sentinel list CI feeds to VerifyTestEvidence.ps1. It is derived')
    [void]$lines.Add('    from tools/FindingCoverageMap.json, which is the coverage authority: a list')
    [void]$lines.Add('    cannot prove its own completeness by confirming that every item currently in it')
    [void]$lines.Add('    exists, so the mapping - and the check that every audited finding appears in it -')
    [void]$lines.Add('    lives there instead (CRUU15-009).')
    [void]$lines.Add('')
    [void]$lines.Add('    Regenerate with: pwsh ./tools/VerifyFindingCoverage.ps1 -Regenerate')
    [void]$lines.Add('#>')
    [void]$lines.Add('@{')
    [void]$lines.Add('    Required = @(')

    $body = [System.Collections.Generic.List[string]]::new()
    foreach ($property in ($map.findings.PSObject.Properties | Sort-Object Name)) {
        [void]$body.Add("        # $($property.Name)")
        foreach ($test in @($property.Value)) {
            [void]$body.Add("        '$test',")
        }
    }
    $body[$body.Count - 1] = $body[$body.Count - 1].TrimEnd(',')
    foreach ($line in $body) { [void]$lines.Add($line) }

    [void]$lines.Add('    )')
    [void]$lines.Add('}')

    Set-Content -Path $manifestPath -Value $lines -Encoding utf8
    Write-Host "Regenerated '$manifestPath' with $($expected.Count) sentinel(s)."
    exit 0
}

# --- 4. The generated sentinel list still matches the map exactly. -----------------------
if (-not (Test-Path $manifestPath)) {
    Write-Error "Sentinel manifest not found: '$manifestPath'."
    exit 1
}

# Parsed by pattern rather than Import-PowerShellDataFile: that cmdlet is absent from some
# Windows PowerShell hosts (notably the powershell.exe on GitHub's windows runners), and a
# release gate that only works under one shell is a gate that silently does not run.
$manifestText = Get-Content $manifestPath -Raw
$actual = @([regex]::Matches($manifestText, "'([A-Za-z0-9_]+)'") | ForEach-Object { $_.Groups[1].Value })

if ($actual.Count -eq 0) {
    Write-Error "No sentinel names could be parsed from '$manifestPath'."
    exit 1
}

$missingFromManifest = @($expected | Where-Object { $actual -notcontains $_ })
$extraInManifest = @($actual | Where-Object { $expected -notcontains $_ })

if ($missingFromManifest.Count -gt 0) {
    Write-Error "Sentinel manifest is missing mapped tests: $($missingFromManifest -join ', ')"
    exit 1
}

if ($extraInManifest.Count -gt 0) {
    Write-Error "Sentinel manifest names tests that the coverage map does not: $($extraInManifest -join ', ')"
    exit 1
}

Write-Host "Finding coverage verified: $($requiredIds.Count) finding(s), $($expected.Count) required test(s)."
exit 0
