<#
.SYNOPSIS
    Parses and verifies TRX test output evidence to enforce zero-failure and required test coverage.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string[]]$TrxPath,

    [Parameter(Mandatory=$false)]
    [string[]]$RequiredTests = @()
)

$ErrorActionPreference = "Stop"

if ($RequiredTests) {
    $flattened = @()
    foreach ($item in $RequiredTests) {
        if ($item) {
            foreach ($sub in $item.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries)) {
                $trimmed = $sub.Trim()
                if ($trimmed) {
                    $flattened += $trimmed
                }
            }
        }
    }
    $RequiredTests = $flattened
}

if (-not $TrxPath -or $TrxPath.Count -eq 0) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
    $trxCandidates = Get-ChildItem -Path (Join-Path $repoRoot "TestResults"), (Join-Path $repoRoot "tests") -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
    if ($trxCandidates.Count -eq 0) {
        Write-Error "No TRX test result files found."
        exit 1
    }
    $TrxPath = @($trxCandidates[0].FullName)
}

$resultsByName = @{}
$grandTotal = 0
$grandPassed = 0
$grandFailed = 0
$grandError = 0
$grandTimeout = 0
$grandAborted = 0

foreach ($singleTrx in $TrxPath) {
    if (-not (Test-Path $singleTrx)) {
        Write-Error "TRX file not found: '$singleTrx'"
        exit 1
    }

    Write-Host "Verifying test evidence from: '$singleTrx'"
    [xml]$trx = Get-Content $singleTrx -Raw

    $nsManager = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $nsManager.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

    $counters = $trx.SelectSingleNode("//t:ResultSummary/t:Counters", $nsManager)
    if ($null -eq $counters) {
        $counters = $trx.SelectSingleNode("//ResultSummary/Counters")
    }

    if ($null -eq $counters) {
        Write-Error "Invalid TRX format: Could not locate ResultSummary/Counters element in '$singleTrx'."
        exit 1
    }

    $total = [int]$counters.total
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $errCount = [int]$counters.error
    $timeout = [int]$counters.timeout
    $aborted = [int]$counters.aborted

    Write-Host "  TRX Results: Total=$total, Passed=$passed, Failed=$failed, Error=$errCount, Timeout=$timeout, Aborted=$aborted"

    if ($total -le 0) {
        Write-Error "TRX '$singleTrx' contains 0 executed tests."
        exit 1
    }

    if ($failed -gt 0 -or $errCount -gt 0 -or $timeout -gt 0 -or $aborted -gt 0) {
        Write-Error "Test run '$singleTrx' contains failures: Failed=$failed, Error=$errCount, Timeout=$timeout, Aborted=$aborted"
        exit 1
    }

    if ($passed -ne $total) {
        Write-Error "Not all tests passed in '$singleTrx' ($passed/$total passed)."
        exit 1
    }

    $grandTotal += $total
    $grandPassed += $passed
    $grandFailed += $failed
    $grandError += $errCount
    $grandTimeout += $timeout
    $grandAborted += $aborted

    $unitTestResults = $trx.SelectNodes("//t:UnitTestResult", $nsManager)
    if ($unitTestResults.Count -eq 0) {
        $unitTestResults = $trx.SelectNodes("//UnitTestResult")
    }

    foreach ($result in $unitTestResults) {
        $name = [string]$result.testName
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            if (-not $resultsByName.ContainsKey($name)) {
                $resultsByName[$name] = @()
            }
            $resultsByName[$name] += $result
        }
    }
}

Write-Host "Aggregated Evidence: Total=$grandTotal, Passed=$grandPassed, Failed=$grandFailed, Error=$grandError"

if ($RequiredTests.Count -gt 0) {
    $missingOrFailed = @()

    foreach ($required in $RequiredTests) {
        if (-not $resultsByName.ContainsKey($required)) {
            $missingOrFailed += "$required (Not Executed)"
            continue
        }

        $runs = @($resultsByName[$required])
        if ($runs.Count -lt 1) {
            $missingOrFailed += "$required (Expected at least one result, found $($runs.Count))"
            continue
        }

        $passedCount = 0
        foreach ($run in $runs) {
            $outcome = [string]$run.outcome
            if ($outcome -eq "Passed") {
                $passedCount++
            } else {
                $missingOrFailed += "$required (Outcome: $outcome)"
            }
        }

        if ($passedCount -eq 0 -and (-not ($missingOrFailed -match [regex]::Escape($required)))) {
            $missingOrFailed += "$required (No passed execution)"
        }
    }

    if ($missingOrFailed.Count -gt 0) {
        Write-Error "Required test evidence failed: $($missingOrFailed -join ', ')"
        exit 1
    }

    Write-Host "Required test evidence verified: $($RequiredTests.Count) required test(s) passed with exact match."
}

Write-Host "TRX test evidence verification completed successfully ($grandPassed passed / 0 failed)."
exit 0
