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
    Hotspots section; nothing here recalculates it.

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

    SCOPE: only SlayIdleRepeat.Core and SlayIdleRepeat.Application are measured.
    That allow-list lives in coverage.runsettings and is applied at COLLECTION
    time, so the other fifteen assemblies are never instrumented rather than
    being instrumented and then filtered out of the report.

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
    entirely. Deliberately NOT enabled in CI - calibrate first.

.PARAMETER ExcludeSuites
    Test suites that must not run under coverage. Defaults to the architecture
    suite, whose IL-scanning rules report false violations when coverlet has
    instrumented the assemblies they read. See the parameter block for the
    measurement.

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

    # Suites that must not run under coverage instrumentation.
    #
    # 🔴 The architecture suite is not here for speed, it is here for
    # CORRECTNESS. Its rules read the IL of Core and Application with Mono.Cecil
    # and NetArchTest; coverlet instruments those same two assemblies on disk for
    # the duration of a run. So the rules end up scanning coverlet's injected
    # tracking code and report violations that do not exist in the source:
    # ambient time and randomness, culture-sensitive formatting, types outside a
    # documented namespace. Measured, not guessed - 173/173 pass without
    # coverage, exactly 4 fail with it.
    #
    # Nothing is lost by skipping it. Those rules READ assemblies rather than
    # executing them, so the suite contributes essentially no covered lines; the
    # comparison recorded at the time had Core and Application landing on the same
    # percentages either way. It still runs, unaffected, in its own CI job.
    [string[]]$ExcludeSuites = @('SlayIdleRepeat.Architecture.Tests'),

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

# 🔴 And then MOVE there. Not for the paths below - those are all absolute - but
# for `dotnet reportgenerator` in step 5. A local tool is located by walking UP
# FROM THE CURRENT DIRECTORY looking for .config/dotnet-tools.json: where the
# manifest actually sits on disk is irrelevant, and the invocation takes no flag
# to point at one. Run this script by absolute path from anywhere outside the
# repository and the tool simply does not exist - discovered only after the test
# suite has already burned ten minutes. Measured here, not theorised.
Push-Location $repoRoot

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

# -------------------------------------------------------- suite selection
# Computed whether or not tests run, because step 3 compares the number of
# coverage files against the number of suites that were SUPPOSED to produce one.
# Counting every project under tests/ there would report a phantom missing file
# for every deliberately excluded suite.
$allSuites = @(
    Get-ChildItem -Path (Join-Path $repoRoot 'tests') -Filter '*.csproj' -Recurse -File |
        Sort-Object -Property Name
)
$suites = @($allSuites | Where-Object { $ExcludeSuites -notcontains [IO.Path]::GetFileNameWithoutExtension($_.Name) })
if ($suites.Count -eq 0) {
    Stop-WithSetupFailure "No test project under $repoRoot\tests\ survived -ExcludeSuites ($($ExcludeSuites -join ', '))."
}

# ------------------------------------------------------------ 2. dotnet test
if ($SkipTests) {
    Write-Section 'Skipping tests (-SkipTests)'
} else {
    Write-Section 'dotnet build'

    # Built once here so every suite below can run with --no-build.
    & dotnet build $Solution
    if ($LASTEXITCODE -ne 0) {
        Stop-WithSetupFailure "dotnet build exited $LASTEXITCODE. Nothing was measured; fix the build first."
    }

    if (@($ExcludeSuites).Count -gt 0) {
        Write-Host ''
        Write-Host "Excluded from coverage: $($ExcludeSuites -join ', ')"
    }

    # Per suite rather than one `dotnet test` over the solution, because a single
    # suite has to be left out. That trade has a cost: `dotnet test <sln>` runs
    # test projects CONCURRENTLY, and a naive foreach here serialises them. On
    # this repository that measured 12m04s sequential against roughly ten minutes
    # parallel - Application.Tests alone is 8m12s, so it is the critical path and
    # everything else should be running alongside it, not after it. Hence the
    # explicit throttle rather than a plain loop.
    #
    # Safe to parallelise: each test project instruments the copies of Core and
    # Application in its OWN bin directory, and each writes to its own results
    # directory below. Nothing is shared.
    $throttle = [Math]::Max(2, [Math]::Min(6, [int]([Environment]::ProcessorCount / 2)))
    Write-Section "dotnet test - $($suites.Count) suite(s), up to $throttle at a time"

    $suiteResults = $suites | ForEach-Object -ThrottleLimit $throttle -Parallel {
        # Parallel runspaces do NOT inherit the caller's preference variables, and
        # both of these are load-bearing here: `dotnet` writes ordinary progress to
        # stderr, and 2>&1 below would turn that into a terminating error under the
        # script's own 'Stop' setting. Restated rather than assumed.
        $ErrorActionPreference = 'Continue'
        $PSNativeCommandUseErrorActionPreference = $false

        $project = $_
        $name = [IO.Path]::GetFileNameWithoutExtension($project.Name)

        # One results directory per suite, so a coverage file can be traced back
        # to the suite that produced it. Coverlet still nests its own GUID folder
        # inside, and step 5's recursive glob is unaffected.
        $suiteResultsDir = Join-Path $using:testResultsDir $name

        $output = & dotnet test $project.FullName `
            --no-build `
            --settings $using:runSettings `
            --results-directory $suiteResultsDir 2>&1 | Out-String

        [pscustomobject]@{ Name = $name; ExitCode = $LASTEXITCODE; Output = $output }
    }

    # Printed after the fact, grouped per suite. Live interleaving of six
    # concurrent `dotnet test` runs is unreadable, and this output is what
    # somebody reads when a suite fails.
    $testExitCode = 0
    foreach ($result in ($suiteResults | Sort-Object -Property Name)) {
        Write-Section "dotnet test $($result.Name)"
        Write-Host $result.Output
        if ($result.ExitCode -ne 0) { $testExitCode = $result.ExitCode }
    }

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
# Each file sits under TestResults/<SuiteName>/<guid>/, so the suite that
# produced it is recoverable from the path. An empty file - one carrying no
# <package> at all - means that suite exercised none of the included assemblies.
$emptyMarker = '<packages />'
$bySuite = foreach ($file in $coberturaFiles) {
    $relative = [IO.Path]::GetRelativePath($testResultsDir, $file.FullName)
    $content = Get-Content -Raw -LiteralPath $file.FullName
    [pscustomobject]@{
        Suite       = ($relative -split '[\\/]')[0]
        Path        = [IO.Path]::GetRelativePath($repoRoot, $file.FullName)
        Contributes = $content -notmatch [regex]::Escape($emptyMarker) -and $content -match '<package\b'
    }
}

Write-Host "Cobertura files : $($coberturaFiles.Count) (from $($suites.Count) suite(s) run)"
foreach ($entry in $bySuite) {
    Write-Host "  - $($entry.Suite)$(if (-not $entry.Contributes) { '  [no coverage of the included assemblies]' })"
}

# Fewer files than suites means one did not emit coverage at all, and the ranking
# then covers less than it appears to. Compared against the suites actually RUN,
# not every project under tests/ - otherwise every deliberate -ExcludeSuites entry
# reads as a missing file.
if ($coberturaFiles.Count -lt $suites.Count) {
    Write-Host ''
    Write-Host "WARNING: $($coberturaFiles.Count) coverage file(s) from $($suites.Count) suite(s) that ran. One emitted nothing, so the ranking below covers less of the codebase than it appears to." -ForegroundColor Yellow
}

# Not a fault - the allow-list in coverage.runsettings is narrow on purpose, so
# suites that only exercise adapters legitimately produce an empty
# file. Named anyway: these are the suites whose runtime buys this report
# nothing, and -ExcludeSuites is how you stop paying for them.
$idleSuites = @($bySuite | Where-Object { -not $_.Contributes } | Select-Object -ExpandProperty Suite -Unique)
if ($idleSuites.Count -gt 0) {
    Write-Host ''
    Write-Host "NOTE: $($idleSuites.Count) suite(s) produced no coverage of the measured assemblies: $($idleSuites -join ', ')." -ForegroundColor DarkGray
    Write-Host "      They cost run time and contribute nothing to this report. Pass -ExcludeSuites to skip them, but re-check that decision whenever the allow-list in coverage.runsettings widens." -ForegroundColor DarkGray
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

# Restore the pinned tool before invoking it. A committed manifest is not an
# installation: on a fresh clone, or after a cleared tool cache, the
# reportgenerator command does not exist yet and the run dies HERE - at the last
# step, with the whole test suite already paid for. The goal is one command, so
# the script does this rather than leaving it to the reader to remember. Costs
# about a second once the tool is present.
& dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    Stop-WithSetupFailure "dotnet tool restore failed (exit $LASTEXITCODE). $repoRoot\.config\dotnet-tools.json is what pins ReportGenerator to the version CI renders with; without it there is no reportgenerator command to run."
}

# Forward slashes and a recursive glob: ReportGenerator does its own globbing,
# not the shell's, and a backslash pattern does not match on Linux agents. The
# script must behave identically on both.
$reportsGlob = ($testResultsDir -replace '\\', '/') + '/**/coverage.cobertura.xml'
$targetDir = $coverageDir -replace '\\', '/'

$reportGeneratorArgs = @(
    'reportgenerator'
    "-reports:$reportsGlob"
    "-targetdir:$targetDir"
    # JsonSummary sits beside the three human-readable ones so that
    # scripts/Invoke-Verification.ps1 can read the coverage figures back as
    # culture-invariant JSON numbers instead of regexing percentages out of
    # Summary.txt, which ReportGenerator renders in the machine's own locale.
    # It carries the summary ONLY - no Risk Hotspots section, measured against
    # 5.5.11 - so the ranking still has to be read out of the rendered HTML.
    '-reporttypes:Html;TextSummary;MarkdownSummaryGithub;JsonSummary'
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
