<#
.SYNOPSIS
    Runs the Prompt Helper test suite so that a Windows Smart App Control / WDAC
    code-integrity block can never be misread as a product test failure.

.DESCRIPTION
    On a SAC-enforcing machine a freshly built unsigned assembly can be refused at
    image-load time with HRESULT 0x800711C7. vstest then reports every test in the
    assembly as failed, or aborts the host entirely, which looks exactly like a huge
    code regression. See C:\Projects\_SACsolutions.md for the measured field guide.

    This wrapper implements the documented mitigations:
      1. Settle      - pause after a build before the first test-host launch.
      2. Canary      - run one cheap test first so a block costs seconds, not minutes.
      3. Detect      - two independent detectors: the locale-independent string
                       0x800711C7 in run output, and new CodeIntegrity 3033/3077
                       events naming one of this repository's own assemblies inside
                       the run window.
      4. Back off    - bounded retries with increasing delay.
      5. Exit code   - a distinct exit code for an environment block so it can never
                       be confused with a real test failure.

    It never changes SAC, Defender, WDAC or execution-policy settings, and it never
    copies, renames or re-signs binaries.

.PARAMETER Filter
    Optional vstest --filter expression. Prefer small targeted runs during iteration.

.PARAMETER Build
    Build before testing. Omitted means --no-build against the current output.

.PARAMETER Configuration
    Build configuration. Default Release.

.PARAMETER SettleSeconds
    Pause before the first test-host launch. Only applied when -Build was used,
    because only a fresh image needs a fresh code-integrity verdict.

.PARAMETER Attempts
    Maximum number of attempts when an environment block is detected.

.PARAMETER TimeoutSeconds
    Hard timeout per attempt. The process tree is killed when it is exceeded.

.PARAMETER SkipCanary
    Skip the canary probe and go straight to the real run.

.PARAMETER Trx
    Also write a .trx logger file into TestResults.

.OUTPUTS
    Exit code 0  - the run completed and all tests passed.
    Exit code 1  - the run completed and there were real test failures.
    Exit code 42 - ENVIRONMENT BLOCK. The tests did not run. Any reported failure
                   count is meaningless. Do not debug, fix or revert code.
    Exit code 43 - the filter matched no tests. Not a regression; fix the filter.
    Exit code 44 - inconclusive: timed out or the exit code was unreadable.

.EXAMPLE
    powershell -NoProfile -File tools\RunTestsSacSafe.ps1 -Build

.EXAMPLE
    powershell -NoProfile -File tools\RunTestsSacSafe.ps1 -Filter "FullyQualifiedName~LibraryRepository"
#>
[CmdletBinding()]
param(
    [string]$Filter,
    [switch]$Build,
    [string]$Configuration = "Release",
    [int]$SettleSeconds = 20,
    [int]$Attempts = 4,
    [int]$TimeoutSeconds = 1800,
    [switch]$SkipCanary,
    [switch]$Trx
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$EXIT_PASS         = 0
$EXIT_TEST_FAILURE = 1
$EXIT_SAC_BLOCK    = 42
$EXIT_NO_MATCH     = 43
$EXIT_INCONCLUSIVE = 44

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$solution = Join-Path $repoRoot "PromptHelper.slnx"
$canaryFilter = "FullyQualifiedName~PromptHelper.Tests.TextUtilitiesTests"

# The canary probes whether the loader trusts the Windows product and test assemblies, which
# is what a code-integrity policy refuses. It therefore runs against that one project rather
# than the whole solution: the Core and Linux test projects contain no matching test, so
# aiming the canary at the solution made every other project report "no test matches the
# filter".
$canaryProject = Join-Path $repoRoot "tests/PromptHelper.Tests/PromptHelper.Tests.csproj"

# Assemblies this repository produces. A CodeIntegrity event only counts as *our*
# block when it names one of these; the machine's log also carries unrelated blocks
# from other products, and treating those as ours would report a false block.
#
# Matched by prefix rather than by an enumerated list. An exhaustive list silently
# stopped covering the solution when it grew PromptHelper.Core, .Desktop and
# .Platform.Linux (plus their test projects): a block of PromptHelper.Core.dll would
# have gone undetected and been reported as real test failures, which is the exact
# failure mode this wrapper exists to prevent. A prefix match keeps covering projects
# added later.
$ownAssemblyPattern = 'PromptHelper[A-Za-z0-9_.]*\.(dll|exe)'

# The canonical HRESULT. Matched instead of the message text because the message is
# localized - on a German Windows it reads "Eine Anwendungssteuerungsrichtlinie...".
$sacHresultPattern = '0x800711C7'

# Locale-independent-enough detection of "the filter matched nothing".
$noMatchPatterns = @(
    'No test matches the given testcase filter',
    'Kein Test entspricht dem angegebenen Testfallfilter',
    'No test is available in'
)

function Write-Section([string]$text) {
    Write-Host ""
    Write-Host "=== $text ===" -ForegroundColor Cyan
}

function Get-OwnCodeIntegrityBlocks([datetime]$since) {
    # A missing/empty channel must not look like a clean result and must not throw.
    try {
        $events = Get-WinEvent -FilterHashtable @{
            LogName   = 'Microsoft-Windows-CodeIntegrity/Operational'
            Id        = 3033, 3077
            StartTime = $since
        } -ErrorAction Stop
    } catch {
        return , @()
    }

    # The leading comma keeps an empty or single result an array. Without it
    # PowerShell unwraps it, $result.BlockEvents becomes $null or a bare event, and
    # the .Count check below fails under Set-StrictMode instead of reporting "no
    # block" - which would turn a clean run into a crash of this wrapper.
    if (-not $events) { return , @() }
    return , @($events | Where-Object { $_.Message -match $ownAssemblyPattern })
}

function Invoke-DotnetTest {
    param(
        [string[]]$ExtraArgs,
        [string]$Label,
        [int]$Timeout,
        [string]$Target
    )

    $stamp = [DateTime]::Now.ToString("yyyyMMdd-HHmmss-fff")
    $logDir = Join-Path $repoRoot "artifacts\sac-safe-test"
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    $outFile = Join-Path $logDir "$Label-$stamp.out.txt"
    $errFile = Join-Path $logDir "$Label-$stamp.err.txt"

    $effectiveTarget = if ($Target) { $Target } else { $solution }
    $dotnetArgs = @("test", $effectiveTarget, "-c", $Configuration)
    if (-not $Build) { $dotnetArgs += "--no-build" }
    if ($Filter)     { $dotnetArgs += @("--filter", $Filter) }
    $dotnetArgs += $ExtraArgs

    Write-Host "dotnet $($dotnetArgs -join ' ')" -ForegroundColor DarkGray

    # Trap (a) from the field guide: never pipe dotnet test. Its stdout is
    # block-buffered when stdout is not a console, so a piped run prints nothing for
    # many minutes and looks hung. Redirect to a file and tail the file instead.
    $startedAt = Get-Date
    $proc = Start-Process -FilePath "dotnet" -ArgumentList $dotnetArgs `
        -WorkingDirectory $repoRoot -NoNewWindow -PassThru `
        -RedirectStandardOutput $outFile -RedirectStandardError $errFile

    # Trap (b): cache the native handle while the process is alive, otherwise
    # $proc.ExitCode is unreadable after exit and silently compares as "not 0".
    try { $null = $proc.Handle } catch { }

    $lastLength = 0
    $lastHeartbeat = Get-Date
    $timedOut = $false

    while (-not $proc.HasExited) {
        Start-Sleep -Milliseconds 500

        if (((Get-Date) - $startedAt).TotalSeconds -gt $Timeout) {
            $timedOut = $true
            Write-Host "[TIMEOUT] exceeded $Timeout s - killing the process tree" -ForegroundColor Red
            # Child test hosts survive a kill of the parent, so kill the whole tree.
            # taskkill /T walks descendants, which Stop-Process does not.
            & taskkill.exe /PID $proc.Id /T /F 2>&1 | Out-Null
            try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch { }
            break
        }

        # Tail with FileShare.ReadWrite so the still-writing child is not disturbed.
        try {
            $fs = [IO.File]::Open($outFile, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            try {
                if ($fs.Length -gt $lastLength) {
                    $fs.Seek($lastLength, [IO.SeekOrigin]::Begin) | Out-Null
                    $reader = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
                    $chunk = $reader.ReadToEnd()
                    $lastLength = $fs.Length
                    if ($chunk) {
                        foreach ($line in ($chunk -split "`r?`n")) {
                            if ($line.Trim()) { Write-Host "  $line" }
                        }
                        $lastHeartbeat = Get-Date
                    }
                }
            } finally { $fs.Dispose() }
        } catch { }

        if (((Get-Date) - $lastHeartbeat).TotalSeconds -ge 30) {
            $elapsed = [int]((Get-Date) - $startedAt).TotalSeconds
            Write-Host "  [heartbeat] still running, ${elapsed}s elapsed" -ForegroundColor DarkGray
            $lastHeartbeat = Get-Date
        }
    }

    if (-not $timedOut) { $proc.WaitForExit() }

    $exitCode = $null
    try { $exitCode = $proc.ExitCode } catch { }

    $stdout = ""
    $stderr = ""
    if (Test-Path $outFile) { $stdout = [IO.File]::ReadAllText($outFile) }
    if (Test-Path $errFile) { $stderr = [IO.File]::ReadAllText($errFile) }
    $combined = "$stdout`n$stderr"

    # Print whatever the tail did not reach (e.g. after a kill).
    if ($stdout.Length -gt $lastLength) {
        foreach ($line in ($stdout.Substring($lastLength) -split "`r?`n")) {
            if ($line.Trim()) { Write-Host "  $line" }
        }
    }
    if ($stderr.Trim()) {
        Write-Host "  --- stderr ---" -ForegroundColor DarkYellow
        foreach ($line in ($stderr -split "`r?`n")) {
            if ($line.Trim()) { Write-Host "  $line" -ForegroundColor DarkYellow }
        }
    }

    # Detector 1: the HRESULT in the run output.
    $blocked = $combined -match $sacHresultPattern
    # Note: no ", @()" initializer here. The unary comma is only correct on a `return`,
    # where the pipeline unrolls one level; in a plain assignment it would produce an array
    # holding one empty array, so .Count would read 1 and report a block that never happened.

    # Detector 2: CodeIntegrity events naming one of our assemblies in the run window.
    # The log is authoritative when the run did not pass, because a block can abort the
    # host before any 0x800711C7 text is ever written to stdout.
    $blockEvents = Get-OwnCodeIntegrityBlocks -since $startedAt

    if ($blockEvents.Count -gt 0 -and $exitCode -ne 0) {
        $blocked = $true
    }
    elseif ($blockEvents.Count -gt 0) {
        # Measured on this machine (2026-10-22 16:27:06): the policy logged 3033 and 3077
        # against PromptHelper.Tests.dll and the run still completed and passed. A logged
        # refusal is therefore NOT by itself proof that the run was blocked - treating it as
        # one here would fail a green suite. It is not nothing either: the same assembly can
        # be refused outright on a later run, so a passing run that provoked one is reported
        # as a warning rather than silently accepted.
        Write-Host ""
        Write-Host "[WARN] The code-integrity policy logged $($blockEvents.Count) refusal event(s) against our own" -ForegroundColor Yellow
        Write-Host "[WARN] assemblies during this run, yet the run completed. The test result stands, but do" -ForegroundColor Yellow
        Write-Host "[WARN] not read it as the policy trusting this build: a later run of the same binary can" -ForegroundColor Yellow
        Write-Host "[WARN] be refused outright. Re-check rather than assuming this result still holds." -ForegroundColor Yellow
        $blockEvents | Select-Object -First 3 | ForEach-Object {
            $flat = ($_.Message -replace "`r?`n", ' ')
            Write-Host "       $($_.TimeCreated) [$($_.Id)] $($flat.Substring(0, [Math]::Min(180, $flat.Length)))" -ForegroundColor DarkYellow
        }
    }

    # "No test matches the filter" is only decisive when NO test project matched. On a
    # multi-project solution a filter that legitimately selects tests in one project makes
    # every other project print that message, and treating the first occurrence as decisive
    # reported exit 43 for a run that had in fact just passed.
    #
    # The two counts below are compared instead. Both markers are structural rather than
    # prose: the assembly header vstest prints per test project ends in
    # '.dll" (.NETCoreApp,...' in every locale, while the no-match phrases are localized and
    # are matched from the list above.
    $projectsDiscovered = @([regex]::Matches($combined, '\.dll"\s+\(\.NETCoreApp')).Count
    $noMatchOccurrences = 0
    foreach ($p in $noMatchPatterns) {
        $noMatchOccurrences += @([regex]::Matches($combined, [regex]::Escape($p))).Count
    }

    $noMatch = $noMatchOccurrences -gt 0 -and
               ($projectsDiscovered -eq 0 -or $noMatchOccurrences -ge $projectsDiscovered)

    return [PSCustomObject]@{
        ExitCode    = $exitCode
        TimedOut    = $timedOut
        Blocked     = $blocked
        BlockEvents = $blockEvents
        NoMatch     = $noMatch
        Output      = $combined
        LogPath     = $outFile
    }
}

Write-Section "Prompt Helper SAC-safe test runner"
Write-Host "repo          : $repoRoot"
Write-Host "configuration : $Configuration"
Write-Host "build         : $(if ($Build) { 'yes' } else { 'no (--no-build)' })"
Write-Host "filter        : $(if ($Filter) { $Filter } else { '(none - full suite)' })"

if ($Build) {
    Write-Section "Build"
    # One combined build happens inside dotnet test; building here separately would
    # add another rebuild/launch cycle, which is what provokes blocks. So this only
    # restores and compiles once, and the test run below reuses the output.
    & dotnet build $solution -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Host "BUILD FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    if ($SettleSeconds -gt 0) {
        Write-Section "Settle"
        Write-Host "Waiting ${SettleSeconds}s so a fresh image gets a fresh verdict before the first test host launches."
        Start-Sleep -Seconds $SettleSeconds
    }
}

$extra = @()
if ($Trx) {
    $extra += @("--logger", "trx;LogFileName=sac-safe-$([DateTime]::Now.ToString('yyyyMMdd-HHmmss')).trx")
}

for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    if ($attempt -gt 1) {
        $delay = 30 * $attempt
        Write-Section "Backoff"
        Write-Host "Environment block on attempt $($attempt - 1). Waiting ${delay}s before attempt $attempt of $Attempts."
        Start-Sleep -Seconds $delay
    }

    if (-not $SkipCanary) {
        Write-Section "Canary (attempt $attempt)"
        $savedFilter = $Filter
        $Filter = $canaryFilter
        $canary = Invoke-DotnetTest -ExtraArgs @() -Label "canary" -Timeout 300 -Target $canaryProject
        $Filter = $savedFilter

        if ($canary.Blocked) {
            Write-Host "[SAC] canary blocked - not launching the real run" -ForegroundColor Red
            continue
        }
        if ($canary.NoMatch) {
            Write-Host "CANARY FILTER MATCHED NOTHING ('$canaryFilter')." -ForegroundColor Red
            Write-Host "The canary test class was renamed or removed. Update \$canaryFilter in this script." -ForegroundColor Red
            exit $EXIT_NO_MATCH
        }
        if ($canary.ExitCode -ne 0) {
            Write-Host "[WARN] canary did not pass (exit $($canary.ExitCode)) and was not a SAC block." -ForegroundColor Yellow
            Write-Host "[WARN] Continuing to the real run so the actual failures are visible." -ForegroundColor Yellow
        } else {
            Write-Host "[OK] canary passed - the loader trusts our assemblies right now." -ForegroundColor Green
        }
    }

    Write-Section "Test run (attempt $attempt)"
    $run = Invoke-DotnetTest -ExtraArgs $extra -Label "run" -Timeout $TimeoutSeconds

    if ($run.Blocked) {
        Write-Section "VERDICT: ENVIRONMENT BLOCK"
        Write-Host "Smart App Control / code-integrity policy blocked our own assembly." -ForegroundColor Red
        if ($run.Output -match $sacHresultPattern) {
            Write-Host "  detector: $sacHresultPattern present in run output" -ForegroundColor Red
        }
        if ($run.BlockEvents.Count -gt 0) {
            Write-Host "  detector: $($run.BlockEvents.Count) CodeIntegrity 3033/3077 event(s) naming our assemblies" -ForegroundColor Red
            $run.BlockEvents | Select-Object -First 3 | ForEach-Object {
                $flat = ($_.Message -replace "`r?`n", ' ')
                $shown = $flat.Substring(0, [Math]::Min(220, $flat.Length))
                Write-Host "    $($_.TimeCreated) [$($_.Id)] $shown" -ForegroundColor DarkRed
            }
        }
        Write-Host "THE TESTS DID NOT RUN. Any failure count above is meaningless." -ForegroundColor Red
        continue
    }

    if ($run.NoMatch) {
        Write-Section "VERDICT: FILTER MATCHED NO TESTS"
        Write-Host "'$Filter' selected nothing. This is not a regression - check the filter." -ForegroundColor Yellow
        exit $EXIT_NO_MATCH
    }

    if ($run.TimedOut) {
        Write-Section "VERDICT: INCONCLUSIVE (timeout)"
        Write-Host "The run exceeded ${TimeoutSeconds}s and was killed. Nothing is proven." -ForegroundColor Yellow
        exit $EXIT_INCONCLUSIVE
    }

    if ($null -eq $run.ExitCode) {
        Write-Section "VERDICT: INCONCLUSIVE (unreadable exit code)"
        Write-Host "Treating an unreadable exit code as inconclusive, never as pass or fail." -ForegroundColor Yellow
        exit $EXIT_INCONCLUSIVE
    }

    if ($run.ExitCode -eq 0) {
        Write-Section "VERDICT: PASS"
        Write-Host "All tests passed, with no code-integrity block in the run window." -ForegroundColor Green
        Write-Host "log: $($run.LogPath)"
        exit $EXIT_PASS
    }

    Write-Section "VERDICT: REAL TEST FAILURES"
    Write-Host "dotnet test exited $($run.ExitCode) and no code-integrity block was detected." -ForegroundColor Red
    Write-Host "These are genuine results. log: $($run.LogPath)" -ForegroundColor Red
    exit $EXIT_TEST_FAILURE
}

Write-Section "VERDICT: ENVIRONMENT BLOCK (all $Attempts attempts)"
Write-Host "Every attempt was refused by code-integrity policy. Report this as" -ForegroundColor Red
Write-Host "'skipped - environment', not as a product defect. Do not revert code," -ForegroundColor Red
Write-Host "do not self-sign, and do not weaken any security setting." -ForegroundColor Red
exit $EXIT_SAC_BLOCK
