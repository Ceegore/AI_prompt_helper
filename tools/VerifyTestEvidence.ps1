<#
.SYNOPSIS
    Parses and verifies TRX test output evidence to enforce zero-failure and required test coverage.
#>
[CmdletBinding()]
param(
    [string]$TrxPath,
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

if (-not $TrxPath) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
    $trxCandidates = Get-ChildItem -Path (Join-Path $repoRoot "TestResults"), (Join-Path $repoRoot "tests") -Filter "*.trx" -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
    if ($trxCandidates.Count -eq 0) {
        Write-Error "No TRX test result files found."
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

$unitTestResults = $trx.SelectNodes("//t:UnitTestResult", $nsManager)
if ($unitTestResults.Count -eq 0) {
    $unitTestResults = $trx.SelectNodes("//UnitTestResult")
}

$resultsByName = @{}
foreach ($result in $unitTestResults) {
    $name = [string]$result.testName
    if ($name) {
        $resultsByName[$name] = $result
    }
}

if ($RequiredTests.Count -gt 0) {
    $missingOrFailed = @()
    foreach ($required in $RequiredTests) {
        $matched = $false
        foreach ($testName in $resultsByName.Keys) {
            if ($testName -eq $required -or $testName -like "*$required*") {
                $outcome = [string]$resultsByName[$testName].outcome
                if ($outcome -eq "Passed") {
                    $matched = $true
                    break
                } else {
                    $missingOrFailed += "$required (Outcome: $outcome)"
                    $matched = $true
                    break
                }
            }
        }
        if (-not $matched) {
            $missingOrFailed += "$required (Not Executed)"
        }
    }

    if ($missingOrFailed.Count -gt 0) {
        Write-Error "Required test verification failed: $($missingOrFailed -join ', ')"
        exit 1
    }

    Write-Host "Required test evidence verified: $($RequiredTests.Count) required test(s) passed."
}

Write-Host "TRX test evidence verification completed successfully ($passed passed / 0 failed)."
exit 0
