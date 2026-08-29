#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Collects coverage and ranks this repository's riskiest methods by CRAP score.

.DESCRIPTION
    CRAP (Change Risk Anti-Patterns) combines cyclomatic complexity with test
    coverage:

        CRAP(m) = complexity(m)^2 * (1 - coverage(m))^3 + complexity(m)

    At 100% coverage CRAP equals complexity - the floor. At 0% coverage it is
    complexity^2 + complexity. ReportGenerator computes it natively in its Risk
    Hotspots section; nothing here recalculates it. See docs/crap.md.

    Three silent-failure modes this script exists to make loud:

      1. The CRAP score is computed from the COBERTURA files, not the OpenCover
         ones. That is the opposite of the usual advice and it was measured here:
         coverlet's Cobertura carries per-method `complexity` and ReportGenerator
         computes Crap Score from it, while coverlet's OpenCover omits the
         `crapScore` attribute that ReportGenerator's OpenCover parser reads. An
         OpenCover-driven run renders a Risk Hotspots table with no Crap Score
         column and exits 0. See the header of coverage.runsettings.

      2. So step 4 asserts the Cobertura files carry per-method complexity, and
         step 6 asserts the RENDERED report actually contains a Crap Score
         column. The second check is the one that catches a future regression in
         either tool, because it tests the output rather than the input.

      3. Stale coverage from a previous run produces plausible, wrong numbers.
         Step 1 deletes TestResults/ and coverage/. That deletion is the
         correctness guarantee, not a tidiness step.

    An ABSENT Risk Hotspots section on a green run means nothing crossed the
    thresholds, not that the setup is broken. Prove it by re-running with
    -CrapThreshold 1 -ComplexityThreshold 1.

.PARAMETER Solution
    The solution to test. Defaults to the single .sln at the repository root.

.PARAMETER SkipTests
    Reuse the coverage already in TestResults/ instead of re-running the suites.
    TestResults/ is then NOT deleted; coverage/ still is.

.PARAMETER CrapThreshold
    Minimum CRAP score for a method to be listed as a hotspot. Default 30, the
    conventional "crappy" line.

.PARAMETER ComplexityThreshold
    Minimum cyclomatic complexity for a method to be listed as a hotspot.
    Default 15.

.PARAMETER FailOnCrap
    Build-breaking maximum CRAP score. 0 (the default) disables the gate
    entirely. Deliberately NOT enabled in CI - calibrate first, see docs/crap.md.

.PARAMETER Open
    Open coverage/index.html when the run finishes.

.OUTPUTS
    Exit code 0 - success.
    Exit code 1 - a -FailOnCrap threshold was exceeded.
    Exit code 2 - setup or precondition failure (no coverage produced, wrong
                  format, missing tool, more than one solution).

.EXAMPLE
    pwsh ./scripts/Measure-Crap.ps1

.EXAMPLE
    # Prove the plumbing works by making everything a hotspot.
    pwsh ./scripts/Measure-Crap.ps1 -CrapThreshold 1 -ComplexityThreshold 1

.EXAMPLE
    # Re-render the report from the coverage already on disk.
    pwsh ./scripts/Measure-Crap.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Solution,

    [switch]$SkipTests,

    [ValidateRange(1, [int]::MaxValue)]
    [int]$CrapThreshold = 30,

    [ValidateRange(1, [int]::MaxValue)]
    [int]$ComplexityThreshold = 15,

    [ValidateRange(0, [int]::MaxValue)]
    [int]$FailOnCrap = 0,

    [switch]$Open
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned for the same reason build/ci/Invoke-UnitTests.ps1 pins it: this script
# inspects $LASTEXITCODE after `dotnet` itself, and a runner image that flips the
# default would turn those checks into thrown exceptions with different exit
# codes than the contract above promises.
$PSNativeCommandUseErrorActionPreference = $false

$ExitSetupFailure = 2
$ExitThresholdExceeded = 1

# ReportGenerator renders at most this many rows into the HTML Risk Hotspots
# table. It is hardcoded in the tool - there is no setting for it, and the page
# does not disclose the truncation - so it is stated here instead. Measured
# against 5.5.11; if a future version changes it, this only affects the wording
# of a warning, never the report itself.
$HotspotTableRowCap = 20

function Write-Section {
    param([string]$Title)
    Write-Host ''
    Write-Host "=== $Title" -ForegroundColor Cyan
}

function Stop-WithSetupFailure {
    param([string]$Message)
    Write-Host ''
    Write-Host "CRAP setup failure: $Message" -ForegroundColor Red
    exit $ExitSetupFailure
}

# ------------------------------------------------------------------- locate
# $PSScriptRoot/.. rather than Get-Location: the script must produce the same
# report whichever directory it is invoked from, and every path below is built
# from $repoRoot for that reason.
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testResultsDir = Join-Path $repoRoot 'TestResults'
$coverageDir = Join-Path $repoRoot 'coverage'
$runSettings = Join-Path $repoRoot 'coverage.runsettings'

if (-not $Solution) {
    $solutionFiles = @(Get-ChildItem -Path $repoRoot -Filter '*.sln' -File)
    if ($solutionFiles.Count -eq 0) {
        Stop-WithSetupFailure "No .sln found at $repoRoot. Pass -Solution explicitly."
    }
    if ($solutionFiles.Count -gt 1) {
        Stop-WithSetupFailure (
            "$($solutionFiles.Count) .sln files at $repoRoot " +
            "($($solutionFiles.Name -join ', ')). Pass -Solution to say which one.")
    }
    $Solution = $solutionFiles[0].FullName
}
if (-not (Test-Path -LiteralPath $Solution)) {
    Stop-WithSetupFailure "Solution '$Solution' does not exist."
}
if (-not (Test-Path -LiteralPath $runSettings)) {
    Stop-WithSetupFailure "$runSettings is missing. It is what turns coverage collection on at all and selects the formats, without which there is no complexity data and therefore no CRAP score."
}

Write-Section 'CRAP score collection'
Write-Host "Repository root      : $repoRoot"
Write-Host "Solution             : $Solution"
Write-Host "CRAP threshold       : $CrapThreshold"
Write-Host "Complexity threshold : $ComplexityThreshold"
Write-Host "Fail on CRAP         : $(if ($FailOnCrap -gt 0) { $FailOnCrap } else { 'disabled' })"

# -------------------------------------------------- 1. delete stale artifacts
# Mandatory, not an optimisation. Coverlet writes into a fresh GUID directory per
# run, so a leftover TestResults/ from an earlier run is silently globbed in by
# step 5 and averaged into every number the report shows.
Write-Section 'Clearing stale artifacts'
if ($SkipTests) {
    Write-Host 'TestResults/ : KEPT (-SkipTests; reusing the coverage already there)'
} elseif (Test-Path -LiteralPath $testResultsDir) {
    Remove-Item -LiteralPath $testResultsDir -Recurse -Force
    Write-Host 'TestResults/ : deleted'
} else {
    Write-Host 'TestResults/ : absent'
}
if (Test-Path -LiteralPath $coverageDir) {
    Remove-Item -LiteralPath $coverageDir -Recurse -Force
    Write-Host 'coverage/    : deleted'
} else {
    Write-Host 'coverage/    : absent'
}

# ------------------------------------------------------------ 2. dotnet test
if ($SkipTests) {
    Write-Section 'Skipping tests (-SkipTests)'
} else {
    Write-Section 'dotnet test'
    & dotnet test $Solution --settings $runSettings --results-directory $testResultsDir
    $testExitCode = $LASTEXITCODE

    # A failing suite is reported but NOT fatal: coverlet still writes coverage
    # for everything that ran, and a partial CRAP ranking is more useful than
    # none. The warning is loud because the scores are then understated - an
    # assertion that failed still counted the lines it executed on the way there.
    if ($testExitCode -ne 0) {
        Write-Host ''
        Write-Host "WARNING: dotnet test exited $testExitCode. Coverage below is from a run with failing tests and is therefore OPTIMISTIC; fix the suites before trusting the ranking." -ForegroundColor Yellow
    }
}

# --------------------------------------------- 3. assert coverage was produced
Write-Section 'Verifying collected coverage'
if (-not (Test-Path -LiteralPath $testResultsDir)) {
    $hint = if ($SkipTests) { ' - -SkipTests has nothing to reuse. Run once without it.' } else { '. dotnet test produced no results directory at all.' }
    Stop-WithSetupFailure "$testResultsDir does not exist$hint"
}

$coberturaFiles = @(
    Get-ChildItem -Path $testResultsDir -Filter 'coverage.cobertura.xml' -Recurse -File |
        Sort-Object -Property FullName
)

if ($coberturaFiles.Count -eq 0) {
    Stop-WithSetupFailure (
        "No coverage.cobertura.xml under $testResultsDir.`n" +
        "  The most likely cause is that the test projects do not reference the coverlet.collector package.`n" +
        "  The 'XPlat code coverage' data collector named in coverage.runsettings is PROVIDED BY that package:`n" +
        "  with no reference, dotnet test accepts --settings, runs green, and writes no coverage whatsoever.`n" +
        "  Fix: add a coverlet.collector PackageReference to every suite under tests/. This repository uses`n" +
        "  Central Package Management, so the version belongs in Directory.Packages.props, not in the .csproj.")
}
Write-Host "Cobertura files : $($coberturaFiles.Count)"
foreach ($file in $coberturaFiles) {
    Write-Host "  - $([IO.Path]::GetRelativePath($repoRoot, $file.FullName))"
}

# Every suite should contribute one file. Fewer means the glob in step 5 is
# ranking a subset of the codebase while looking exactly like a full run.
$testsDir = Join-Path $repoRoot 'tests'
if (Test-Path -LiteralPath $testsDir) {
    $testProjectCount = @(Get-ChildItem -Path $testsDir -Filter '*.csproj' -Recurse -File).Count
    if ($testProjectCount -gt 0 -and $coberturaFiles.Count -lt $testProjectCount) {
        Write-Host ''
        Write-Host "WARNING: $($coberturaFiles.Count) coverage file(s) for $testProjectCount test project(s) under tests/. Some suite did not emit coverage, so the ranking below covers less of the codebase than it appears to." -ForegroundColor Yellow
    }
}

# ------------------------------- 4. assert the format actually carries complexity
# Without a per-method `complexity` attribute ReportGenerator has nothing to
# compute a Crap Score from, and it renders the Risk Hotspots table WITHOUT that
# column rather than failing. SkipAutoProps drops trivial members, so match the
# attribute on a <method ...> element specifically.
$withComplexity = @(
    $coberturaFiles | Where-Object {
        (Get-Content -Raw -LiteralPath $_.FullName) -match '<method\b[^>]*\scomplexity='
    }
)
if ($withComplexity.Count -eq 0) {
    Stop-WithSetupFailure (
        "None of the $($coberturaFiles.Count) Cobertura file(s) carries a per-method complexity attribute.`n" +
        "  ReportGenerator computes the Crap Score from exactly that attribute, and without it renders the`n" +
        "  Risk Hotspots table with no Crap Score column instead of failing.`n" +
        "  Check <Format> in coverage.runsettings - it must include cobertura.")
}
Write-Host "Per-method complexity present in $($withComplexity.Count) of $($coberturaFiles.Count) file(s)."

# ---------------------------------------------------------- 5. ReportGenerator
Write-Section 'ReportGenerator'

# Forward slashes and a recursive glob: ReportGenerator does its own globbing,
# not the shell's, and a backslash pattern does not match on Linux agents. The
# script must behave identically on both.
$reportsGlob = ($testResultsDir -replace '\\', '/') + '/**/coverage.cobertura.xml'
$targetDir = $coverageDir -replace '\\', '/'

$reportGeneratorArgs = @(
    'reportgenerator'
    "-reports:$reportsGlob"
    "-targetdir:$targetDir"
    '-reporttypes:Html;TextSummary;MarkdownSummaryGithub'
    '-riskhotspotclassfilters:-*.Migrations.*;-*Generated*'
    "riskHotspotsAnalysisThresholds:metricThresholdForCyclomaticComplexity=$ComplexityThreshold"
    "riskHotspotsAnalysisThresholds:metricThresholdForCrapScore=$CrapThreshold"
)
if ($FailOnCrap -gt 0) {
    $reportGeneratorArgs += "riskHotspotsAnalysisThresholds:maximumThresholdForCrapScore=$FailOnCrap"
}

Write-Host "dotnet $($reportGeneratorArgs -join ' ')"
& dotnet @reportGeneratorArgs
$reportGeneratorExitCode = $LASTEXITCODE

if ($reportGeneratorExitCode -ne 0 -and $FailOnCrap -le 0) {
    # With no -FailOnCrap gate armed there is no threshold ReportGenerator could
    # have tripped on, so a non-zero code here is a tool or input problem and it
    # belongs in the setup-failure bucket rather than the gate bucket.
    Stop-WithSetupFailure "ReportGenerator exited $reportGeneratorExitCode with no -FailOnCrap gate armed, so this is a tool or input error rather than an exceeded threshold. Its output is above. If it could not be found at all, run 'dotnet tool restore' first."
}

# ------------------------------ 6. assert the rendered report really has CRAP
# The strongest check in this script, because it tests the OUTPUT. Everything
# upstream can be individually correct while the number this whole exercise
# exists to produce is quietly missing from the report - which is exactly what
# an OpenCover-driven run does. Assert the column, not the inputs to it.
#
# The section is only rendered when at least one method crosses BOTH thresholds,
# so an absent section is legitimate on a healthy codebase at the default 30/15.
# A section that IS present but carries no Crap Score column never is.
$indexPath = Join-Path $coverageDir 'index.html'
if (Test-Path -LiteralPath $indexPath) {
    $indexHtml = Get-Content -Raw -LiteralPath $indexPath
    $hotspotSection = [regex]::Match($indexHtml, '(?s)<risk-hotspots>(.*?)</risk-hotspots>')

    if (-not $hotspotSection.Success -or $hotspotSection.Groups[1].Value -notmatch '<t[dh]') {
        Write-Host ''
        Write-Host "No Risk Hotspots table in the report: nothing reached CRAP $CrapThreshold at complexity $ComplexityThreshold. That is a pass, not a fault - re-run with -CrapThreshold 1 -ComplexityThreshold 1 to see the plumbing work." -ForegroundColor Yellow
    } elseif ($hotspotSection.Groups[1].Value -notmatch 'Crap Score') {
        Stop-WithSetupFailure (
            "The report has a populated Risk Hotspots table with NO 'Crap Score' column.`n" +
            "  Every method in it was ranked by complexity alone, so the one number this script exists to produce`n" +
            "  is missing while everything else looks healthy.`n" +
            "  Cause: ReportGenerator was fed coverage that carries complexity but no Crap Score it can compute or`n" +
            "  read. coverlet's OpenCover output does exactly this - it omits the crapScore attribute that`n" +
            "  ReportGenerator's OpenCover parser expects. Point -reports at the coverage.cobertura.xml files.")
    } else {
        Write-Host ''
        Write-Host 'Crap Score column present in the Risk Hotspots table.' -ForegroundColor Green

        # ReportGenerator's HTML Risk Hotspots table is capped at 20 rows and says
        # so nowhere on the page - measured here at 20 rows against 844 qualifying
        # methods. A reader who takes the table for the whole list is off by an
        # order of magnitude, so say it out loud. Counting rendered rows rather
        # than recomputing CRAP keeps this honest without reimplementing the
        # metric, which is explicitly not this script's job.
        $renderedRows = @([regex]::Matches($hotspotSection.Groups[1].Value, '(?s)<tr>(?<r>.*?)</tr>') |
            Where-Object { $_.Groups['r'].Value -match '<td' }).Count
        Write-Host "Hotspots listed in the report: $renderedRows"
        if ($renderedRows -ge $HotspotTableRowCap) {
            Write-Host "NOTE: the HTML Risk Hotspots table caps at $HotspotTableRowCap rows and does not say so on the page. More methods cross CRAP $CrapThreshold / complexity $ComplexityThreshold than are shown - raise the thresholds to see the next tier down, rather than reading this table as the complete list." -ForegroundColor Yellow
        }
    }
}

# ----------------------------------------------------- 7. print the summary
$summaryPath = Join-Path $coverageDir 'Summary.txt'
if (Test-Path -LiteralPath $summaryPath) {
    Write-Section 'Summary'
    Get-Content -LiteralPath $summaryPath | Write-Host
} else {
    Write-Host ''
    Write-Host "WARNING: $summaryPath was not produced." -ForegroundColor Yellow
}

Write-Section 'Report'
Write-Host "HTML report : $indexPath"
Write-Host ''
Write-Host "An absent Risk Hotspots section means nothing scored at or above CRAP $CrapThreshold / complexity $ComplexityThreshold - not that the run is broken."
Write-Host 'Prove the plumbing with: pwsh ./scripts/Measure-Crap.ps1 -SkipTests -CrapThreshold 1 -ComplexityThreshold 1'

if ($Open) {
    if (Test-Path -LiteralPath $indexPath) {
        # No bare Start-Process on the .html path - that is Windows-only
        # shell-verb behaviour. Each branch names the opener its platform has.
        if ($IsWindows) { Start-Process -FilePath $indexPath }
        elseif ($IsMacOS) { & open $indexPath }
        else { & xdg-open $indexPath }
    } else {
        Write-Host "WARNING: -Open was requested but $indexPath does not exist." -ForegroundColor Yellow
    }
}

# ------------------------------------------ 8. propagate ReportGenerator's code
if ($reportGeneratorExitCode -ne 0) {
    Write-Host ''
    Write-Host "FAILED: at least one method scored above the -FailOnCrap maximum of $FailOnCrap. See the Risk Hotspots section of $indexPath." -ForegroundColor Red
    exit $ExitThresholdExceeded
}

exit 0
