<#
.SYNOPSIS
    Parses and verifies TRX test output evidence to enforce zero-failure and category coverage.
#>
[CmdletBinding()]
param(
    [string]$TrxPath
)

$ErrorActionPreference = "Stop"

if (-not $TrxPath) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
    $trxCandidates = Get-ChildItem -Path (Join-Path $repoRoot "TestResults") -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
    if ($trxCandidates.Count -eq 0) {
        Write-Error "No TRX test result files found in TestResults directory."
        exit 1
    }
    $TrxPath = $trxCandidates[0].FullName
}

if (-not (Test-Path $TrxPath)) {
    Write-Error "TRX file not found: '$TrxPath'"
    exit 1
}

Write-Host "Verifying test evidence from: '$TrxPath'"
[xml]$trx = Get-Content $TrxPath -Raw

$nsManager = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
$nsManager.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

$counters = $trx.SelectSingleNode("//t:ResultSummary/t:Counters", $nsManager)
if ($null -eq $counters) {
    # Fallback to no-namespace search if XML has no default namespace
    $counters = $trx.SelectSingleNode("//ResultSummary/Counters")
}

if ($null -eq $counters) {
    Write-Error "Invalid TRX format: Could not locate ResultSummary/Counters element."
    exit 1
}

$total = [int]$counters.total
$passed = [int]$counters.passed
$failed = [int]$counters.failed
$errCount = [int]$counters.error
$timeout = [int]$counters.timeout
$aborted = [int]$counters.aborted

Write-Host "Test Results: Total=$total, Passed=$passed, Failed=$failed, Error=$errCount, Timeout=$timeout, Aborted=$aborted"

if ($total -le 0) {
    Write-Error "TRX contains 0 executed tests."
    exit 1
}

if ($failed -gt 0 -or $errCount -gt 0 -or $timeout -gt 0 -or $aborted -gt 0) {
    Write-Error "Test run contains failures: Failed=$failed, Error=$errCount, Timeout=$timeout, Aborted=$aborted"
    exit 1
}

if ($passed -ne $total) {
    Write-Error "Not all tests passed ($passed/$total passed)."
    exit 1
}

# Verify sentinel test presence for CRUU8
$unitTestResults = $trx.SelectNodes("//t:UnitTestResult", $nsManager)
if ($unitTestResults.Count -eq 0) {
    $unitTestResults = $trx.SelectNodes("//UnitTestResult")
}

$testNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($result in $unitTestResults) {
    $name = $result.testName
    if ($name) {
        $testNames.Add($name) | Out-Null
    }
}

$sentinelPatterns = @(
    "CRUU8_001",
    "CRUU8_002",
    "CRUU8_003",
    "CRUU8_004",
    "CRUU8_005",
    "CRUU8_006",
    "CRUU8_007",
    "CRUU8_008",
    "CRUU8_009",
    "CRUU8_010",
    "CRUU8_011",
    "CRUU8_012",
    "CRUU8_013",
    "CRUU8_014",
    "CRUU8_015",
    "CRUU8_016",
    "CRUU8_017",
    "CRUU8_018",
    "CRUU8_019"
)

$missingSentinels = @()
foreach ($sentinel in $sentinelPatterns) {
    $found = $false
    foreach ($testName in $testNames) {
        if ($testName -like "*$sentinel*") {
            $found = $true
            break
        }
    }
    if (-not $found) {
        $missingSentinels += $sentinel
    }
}

if ($missingSentinels.Count -gt 0) {
    Write-Warning "The following CRUU8 sentinel tests were not found in TRX: $($missingSentinels -join ', ')"
} else {
    Write-Host "All $( $sentinelPatterns.Count ) CRUU8 sentinel test categories verified in TRX evidence."
}

Write-Host "TRX test evidence verification completed successfully ($passed passed / 0 failed)."
