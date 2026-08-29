#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs one architecture's determinism leg, and fails unless it actually compared
    the committed corpora.

.DESCRIPTION
    14 §8.2 asks CI to simulate 10 000 fixed (seed, build, enemy) triples on Linux
    x64 and Android ARM64 and fail on any divergence, and 14 §13 asks the same of
    the 1 000 client/server parity sequences. Both comparisons are made by the
    ordinary unit suites: every committed table in this repository is asserted
    against on whatever architecture the tests run on, so "the two architectures
    agree" IS "both legs are green against the same committed tables". There is no
    third artefact to diff, and deliberately so - a second comparison of the same
    numbers would be a second place for them to be written down.

    Which makes the one unacceptable outcome a leg that reports success having run
    nothing. `dotnet test` exits 0 on an assembly containing zero tests, and on a
    filter that matches nothing, which is the same thing from CI's point of view.
    So this script reads the real counts out of the TRX files and refuses:

      * a missing TRX                     - the run did not complete
      * a TRX with zero tests             - the filter matched nothing
      * a suite below ITS OWN floor       - the filter matched only part of it
      * a suite with no declared floor    - a count nothing bounds
      * any failed / errored / aborted    - a divergence, which is the point

.PARAMETER Run
    Run the tests as well as asserting on them. The x64 leg does this directly.
    The ARM64 leg cannot: it runs `dotnet test` inside an arm64 container that
    carries no PowerShell, then calls this script on the host to assert on the
    TRX files the container wrote to the mounted results directory.

.PARAMETER PrintFilter
    Write the test filter to stdout and exit. This is how the ARM64 leg gets the
    filter without a second copy of it living in the workflow YAML.

.PARAMETER MinimumTestsBySuite
    The floor each suite's own count has to clear. Per suite rather than one
    total: the two suites are wildly different sizes, so an aggregate floor is
    satisfied by the big one alone and the small one could collapse unnoticed.

.PARAMETER MinimumSuites
    How many TRX files the leg must have produced. A suite that wrote none does
    not appear in the per-file rows at all, so without this the leg would report
    on whichever suite did run and say nothing about the one that did not.

.EXAMPLE
    pwsh build/ci/Assert-DeterminismRun.ps1 -Run
    pwsh build/ci/Assert-DeterminismRun.ps1 -PrintFilter
    pwsh build/ci/Assert-DeterminismRun.ps1 -ResultsDirectory artifacts/determinism
#>
[CmdletBinding()]
param(
    [switch]$Run,

    [switch]$PrintFilter,

    [string]$Configuration = 'Release',

    [string]$RepositoryRoot,

    [string]$ResultsDirectory,

    # PER SUITE, never a single total. The two suites are wildly different sizes -
    # 816 cases in Core.Tests (the LogHash corpus, M2-17's DSL baseline, both hash
    # tables and the snapshot field-order pin) against 38 in Application.Tests (the
    # parity corpus and the wire pin) - so an aggregate floor is satisfied by the
    # big suite alone. 14 §13's parity corpus could collapse to one case and a
    # total-based floor would still clear. Floors rather than equalities so a later
    # milestone may add rows.
    [hashtable]$MinimumTestsBySuite = @{
        'SlayIdleRepeat.Core.Tests'        = 780
        'SlayIdleRepeat.Application.Tests' = 35
    },

    # Both suites must report. The per-TRX check below catches a suite that ran and
    # matched nothing; this catches one that produced no TRX at all, which reads
    # from the totals as though it had never been asked to run.
    [int]$MinimumSuites = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned for the same reason as Invoke-UnitTests.ps1: a non-zero native exit code
# must not throw, because the counts below are what decides this job and not the
# exit code.
$PSNativeCommandUseErrorActionPreference = $false

. (Join-Path $PSScriptRoot '_common.ps1')

<#
    Every committed determinism artefact this repository holds, selected by name.

    Matched on the fully qualified name rather than a trait, because this
    repository has no trait vocabulary and 14 §13's requirement is about WHICH
    tables are re-asserted off-host, not about a category somebody remembered to
    tag. The floor on the executed count is what stops a renamed class quietly
    shrinking this set.

      Determinism      - the LogHash corpus (M5-12) and the DSL baseline (M2-17),
                         plus the 4-dp rounding primitive's own cases
      ReferenceVector  - Hash64's 75 rows and CanonicalStateWriter's 28
      KnownAnswer      - xxHash64's and FNV-1a's published vectors
      Hash64           - the canonical byte-encoding cases
      Parity           - 14 §13's 1 000 command sequences
      FieldOrderPin    - the snapshot and wire-projection field orders

    ⚠️ The two wall-clock BUDGET cases live outside every one of these terms, in
    SlayIdleRepeat.Core.Tests.Cost and SlayIdleRepeat.Application.Tests.Cost, on
    purpose. They measure how long a corpus takes on the machine running it, and
    the ARM64 leg runs under emulation an order of magnitude slower than native -
    so sweeping them in here would turn a slow runner into a report that two
    architectures disagree about floating point. Do not widen a term to reach
    them. The leg's own cost is bounded by the job's timeout-minutes.
#>
$DeterminismFilter = @(
    'FullyQualifiedName~Determinism',
    'FullyQualifiedName~ReferenceVector',
    'FullyQualifiedName~KnownAnswer',
    'FullyQualifiedName~Hash64',
    'FullyQualifiedName~Parity',
    'FullyQualifiedName~FieldOrderPin'
) -join '|'

if ($PrintFilter) {
    Write-Output $DeterminismFilter
    exit 0
}

$root = Get-RepositoryRoot -Override $RepositoryRoot
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $root 'artifacts' 'testresults' }

# The two suites that hold every committed table. Named rather than globbed: the
# other suites carry no determinism artefact, and running them here would make
# this job's cost and its floor depend on work that has nothing to do with 14 §8.2.
$suites = @(
    'tests/SlayIdleRepeat.Core.Tests',
    'tests/SlayIdleRepeat.Application.Tests'
)

Write-Section 'Determinism run'
Write-Host "Repository root : $root"
Write-Host "Results         : $ResultsDirectory"
Write-Host "Minimum suites  : $MinimumSuites"
foreach ($entry in $MinimumTestsBySuite.GetEnumerator() | Sort-Object -Property Key) {
    Write-Host ("  floor {0,-36} {1}" -f $entry.Key, $entry.Value)
}

Write-Host "Filter          : $DeterminismFilter"

$failures = [System.Collections.Generic.List[string]]::new()

if ($Run) {
    $null = New-Item -ItemType Directory -Path $ResultsDirectory -Force

    foreach ($suite in $suites) {
        $name = Split-Path -Leaf $suite
        Write-Section "dotnet test $name"

        $trxPath = Join-Path $ResultsDirectory "$name.trx"
        if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath -Force }

        & dotnet test (Join-Path $root $suite) `
            --configuration $Configuration `
            --filter $DeterminismFilter `
            --logger "trx;LogFileName=$name.trx" `
            --results-directory $ResultsDirectory

        if ($LASTEXITCODE -ne 0) {
            $failures.Add("$name : dotnet test exited $LASTEXITCODE. See the per-TRX rows below.")
        }
    }
}

if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    Exit-WithFailures -CheckName 'Determinism run' -Failures @(
        "No results directory at $ResultsDirectory. The leg produced no TRX at all, so nothing " +
        "compared the committed corpora against this architecture - which is the one outcome this " +
        "job exists to prevent. A skipped or misfiltered test step is not a passing determinism run.")
}

$trxFiles = @(Get-ChildItem -Path (Join-Path $ResultsDirectory '*.trx') -File | Sort-Object -Property Name)

if ($trxFiles.Count -eq 0) {
    Exit-WithFailures -CheckName 'Determinism run' -Failures @(
        "No TRX files under $ResultsDirectory. dotnet test either did not run or wrote its results " +
        "somewhere this script does not read. Either way nothing was compared.")
}

$summary = [System.Collections.Generic.List[object]]::new()
$executed = 0

foreach ($trx in $trxFiles) {
    $counts = Read-TrxCounters -Path $trx.FullName

    if (-not $counts.Measured) {
        $failures.Add(
            "$($trx.Name) : carries no counters. The run did not complete, so its zero is 'not " +
            "measured' rather than 'measured zero'.")
    }
    elseif ($counts.Total -eq 0) {
        $failures.Add(
            "$($trx.Name) : ran successfully and contains ZERO tests. dotnet test exits 0 on an " +
            "empty assembly and on a filter that matches nothing, so this would otherwise be a " +
            "green determinism tick over nothing at all.")
    }

    if ($counts.Failed -gt 0) {
        $failures.Add(
            "$($trx.Name) : $($counts.Failed) test(s) failed of $($counts.Total). On this job that " +
            "is a DIVERGENCE, not a flake: the corpora are pure functions of a committed seed, so a " +
            "red case here means this architecture computed a different answer from the committed " +
            "table. 14 §8.2: fail on any divergence. If it cannot be fixed by additional rounding, " +
            "the documented escalation is fixed-point Q32.32 in Core/Combat only - never a " +
            "regenerated table.")
    }

    # The floor this suite has to clear on its own. Keyed off the TRX's own name,
    # which the run step writes as "<suite>.trx", so a suite nobody wrote a floor
    # for is named rather than quietly waved through.
    $suite = [IO.Path]::GetFileNameWithoutExtension($trx.Name)
    if (-not $MinimumTestsBySuite.ContainsKey($suite)) {
        $failures.Add(
            "$($trx.Name) : no per-suite floor is declared for '$suite'. A suite whose count nothing " +
            "bounds can shrink to one case and still clear an aggregate. Add it to " +
            "-MinimumTestsBySuite with the reviewed count, or stop running it on this leg.")
    }
    elseif ($counts.Total -lt $MinimumTestsBySuite[$suite]) {
        $failures.Add(
            "$($trx.Name) : executed $($counts.Total) determinism case(s), below this suite's floor " +
            "of $($MinimumTestsBySuite[$suite]). The filter has stopped matching part of the corpora " +
            "- a renamed class, a moved namespace, or a suite that no longer builds on this " +
            "architecture. A leg that compares a fraction of the tables and reports success is the " +
            "failure 14 §8.2 is written against.")
    }

    $executed += $counts.Total
    $summary.Add([pscustomobject]@{
        Trx    = $trx.Name
        Total  = $counts.Total
        Passed = $counts.Passed
        Failed = $counts.Failed
        Status = if (-not $counts.Measured) { 'DID NOT RUN' }
                 elseif ($counts.Total -eq 0) { 'EMPTY' }
                 elseif ($counts.Failed -gt 0) { 'DIVERGED' }
                 else { 'passed' }
    })
}

Write-Section 'Determinism summary'
$summary | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
Write-Host "Determinism cases executed: $executed"

if ($trxFiles.Count -lt $MinimumSuites) {
    $failures.Add(
        "This leg produced $($trxFiles.Count) TRX file(s), below the floor of $MinimumSuites. A suite " +
        "that wrote no results at all does not show up in the per-file rows above, so without this " +
        "the leg would report on whichever suite did run and say nothing about the one that did not.")
}

Exit-WithFailures -Failures $failures.ToArray() -CheckName 'Determinism run'
