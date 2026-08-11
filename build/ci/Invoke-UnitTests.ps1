#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs one group of test suites and fails on a suite that silently contains no
    tests.

.DESCRIPTION
    `dotnet test` returns exit code 0 for an assembly with zero tests. A CI job
    that only checks that exit code therefore reports success for a suite that
    was accidentally emptied, never wired up, or whose test adapter stopped being
    registered. This script reads the real per-suite test count out of the TRX
    and compares it against build/ci/test-suites.json, where every currently-empty
    suite is declared with the milestone that fills it.

    Doc sources: 14 §13 (the testing requirements CI must run), 23 §6 (the
    architecture suite fails the build).

.PARAMETER Group
    unit          - domain/application/contract suites (the `test` job)
    architecture  - the 23 §6 rules (the `architecture-tests` job)

    There is deliberately no `integration` group: this repository has no
    integration or end-to-end tier. See $noIntegrationTier in test-suites.json.

.EXAMPLE
    pwsh build/ci/Invoke-UnitTests.ps1 -Group unit
    pwsh build/ci/Invoke-UnitTests.ps1 -Group architecture -NoBuild
#>
[CmdletBinding()]
param(
    [ValidateSet('unit', 'architecture')]
    [string]$Group = 'unit',

    [string]$Configuration = 'Release',

    [string]$RepositoryRoot,

    [string]$ResultsDirectory,

    # Overridable so the empty-suite and stale-exemption rules can themselves be
    # exercised against a throwaway manifest.
    [string]$ManifestPath,

    # Reuse the output of a previous `dotnet build`. The workflow builds once per
    # job, so this saves a rebuild; locally, leave it off.
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned, not inherited. This script deliberately runs `& dotnet` for EVERY suite
# and collects the failures, so one red suite still tells you about the next one
# instead of costing a CI round trip per problem (same reasoning as
# Exit-WithFailures in _common.ps1). That only works while a non-zero native exit
# code does not throw. `$PSNativeCommandUseErrorActionPreference` is $false by
# default on pwsh 7.6.3, which is what the runner has today — but it is an
# experimental-feature-turned-preference whose default has moved before, and if a
# runner image flips it, every script here silently degrades to first-failure-only
# without anything going red to say so. State it.
$PSNativeCommandUseErrorActionPreference = $false

. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-RepositoryRoot -Override $RepositoryRoot
if (-not $ManifestPath) { $ManifestPath = Join-Path $PSScriptRoot 'test-suites.json' }
$manifestPath = $ManifestPath
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path $root 'artifacts' 'testresults' }

Write-Section "Test group '$Group'"
Write-Host "Repository root : $root"
Write-Host "Configuration   : $Configuration"
Write-Host "Results         : $ResultsDirectory"

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

if ($Group -notin @($manifest.groups.PSObject.Properties.Name)) {
    throw "$manifestPath declares no group '$Group'."
}
$groupSpec = $manifest.groups.$Group

# ---------------------------------------------------------------- discovery
# Glob-based on purpose: a suite added by a later milestone is picked up without
# anyone remembering to edit a list in a YAML file.
$discovered = @(
    Get-ChildItem -Path (Join-Path $root $manifest.discoveryGlob) -File |
        Sort-Object -Property Name
)
if ($discovered.Count -eq 0) {
    Write-CiError "No test projects matched '$($manifest.discoveryGlob)' under $root."
    exit 1
}

$groupKeys = @($groupSpec.PSObject.Properties.Name)
$hasInclude = 'include' -in $groupKeys
$hasExclude = 'exclude' -in $groupKeys

$discoveredNames = @($discovered | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) })

# Every name the manifest mentions must actually be out there. Neither `include`
# nor `exclude` errors on a name that matches nothing: a typo'd or renamed entry
# in `include` quietly shrinks the group (in the worst case to nothing an
# unaffected group would notice), and a typo'd entry in `exclude` quietly stops
# excluding. Both leave the job green while running a different set of tests than
# the manifest says it runs.
$manifestFailures = [System.Collections.Generic.List[string]]::new()
foreach ($key in @('include', 'exclude')) {
    if ($key -notin $groupKeys) { continue }
    foreach ($name in $groupSpec.$key) {
        if ($discoveredNames -notcontains $name) {
            $manifestFailures.Add(
                "Group '$Group' $key names '$name', which matched none of the $($discovered.Count) suite(s) " +
                "discovered by '$($manifest.discoveryGlob)'. A name that matches nothing changes which tests " +
                "run without changing anything that goes red. Fix the name in $manifestPath, or delete it. " +
                "Discovered: $($discoveredNames -join ', ')")
        }
    }
}

# Same rule for knownEmpty: an exemption for a project that does not exist is
# dead text that nobody will ever be forced to revisit, and it silently pre-arms
# the exemption for whatever future suite lands on that name.
foreach ($entry in $manifest.knownEmpty) {
    $entryKeys = @($entry.PSObject.Properties.Name)
    if ('project' -notin $entryKeys -or -not $entry.project) {
        $manifestFailures.Add("A knownEmpty entry in $manifestPath has no 'project'.")
        continue
    }
    if ($discoveredNames -notcontains $entry.project) {
        $manifestFailures.Add(
            "knownEmpty names '$($entry.project)', which is not one of the discovered suites. " +
            "Either the project was renamed or removed - in both cases the exemption must go with it.")
    }
    if ('turnsOn' -notin $entryKeys -or $entry.turnsOn -notmatch '^M\d+-\d+') {
        $manifestFailures.Add(
            "knownEmpty entry for '$($entry.project)' has no well-formed 'turnsOn' milestone (got " +
            "'$(if ('turnsOn' -in $entryKeys) { $entry.turnsOn })'). An exemption with no milestone attached " +
            "is an exemption with no expiry.")
    }
    if ('reason' -notin $entryKeys -or -not $entry.reason) {
        $manifestFailures.Add("knownEmpty entry for '$($entry.project)' has no 'reason'.")
    }
}

if ($manifestFailures.Count -gt 0) {
    Exit-WithFailures -Failures $manifestFailures.ToArray() -CheckName "Test manifest ($manifestPath)"
}

$selected = @($discovered | Where-Object {
    $name = [IO.Path]::GetFileNameWithoutExtension($_.Name)
    if ($hasInclude) { return $groupSpec.include -contains $name }
    if ($hasExclude) { return -not ($groupSpec.exclude -contains $name) }
    return $true
})

Write-Host "Discovered      : $($discovered.Count) suite(s); $($selected.Count) in group '$Group'"

if ($selected.Count -eq 0) {
    Write-CiError "Group '$Group' selected no test project. Either test-suites.json is stale or a project was renamed."
    exit 1
}

$knownEmpty = @{}
foreach ($entry in $manifest.knownEmpty) { $knownEmpty[$entry.project] = $entry }

$null = New-Item -ItemType Directory -Path $ResultsDirectory -Force

# ------------------------------------------------------------------- execute
$failures = [System.Collections.Generic.List[string]]::new()
$summary = [System.Collections.Generic.List[object]]::new()
$groupHadUnmeasuredSuite = $false
$exemptSuitesInGroup = 0

foreach ($project in $selected) {
    $name = [IO.Path]::GetFileNameWithoutExtension($project.Name)
    Write-Section "dotnet test $name"

    $trxName = "$name.trx"
    $trxPath = Join-Path $ResultsDirectory $trxName
    if (Test-Path -LiteralPath $trxPath) { Remove-Item -LiteralPath $trxPath -Force }

    $arguments = @(
        'test', $project.FullName,
        '--configuration', $Configuration,
        '--logger', "trx;LogFileName=$trxName",
        '--results-directory', $ResultsDirectory
    )
    if ($NoBuild) { $arguments += '--no-build' }

    & dotnet @arguments
    $testExitCode = $LASTEXITCODE

    # ---- read the real counts, not the exit code
    $total = 0; $passed = 0; $failed = 0
    $countsAreReal = $true
    if (Test-Path -LiteralPath $trxPath) {
        $xml = [xml](Get-Content -Raw -LiteralPath $trxPath)
        $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
        $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $counters = $xml.SelectSingleNode('//t:Counters', $ns)
        if ($counters) {
            $total = [int]$counters.total
            $passed = [int]$counters.passed
            $failed = [int]$counters.failed + [int]$counters.error + [int]$counters.aborted + [int]$counters.timeout
        }
    } else {
        # $total stays 0, but that 0 means "not measured", not "measured zero".
        # Without this flag the run below reports the same suite twice — once for
        # the missing TRX and once for a ZERO-test count it never actually read —
        # and the second message sends the reader off to add a knownEmpty
        # exemption for a suite whose real problem is that it did not run.
        $countsAreReal = $false
        $failures.Add("$name : dotnet test produced no TRX at $trxPath. The run did not complete.")
    }

    $exempt = $knownEmpty.ContainsKey($name)

    if ($testExitCode -ne 0) {
        $failures.Add("$name : dotnet test exited $testExitCode ($failed failing of $total).")
    }

    if ($countsAreReal -and $total -eq 0 -and -not $exempt) {
        $failures.Add(
            "$name : ran successfully but contains ZERO tests. dotnet test exits 0 on an empty " +
            "assembly, so this would otherwise be a green tick over nothing. Either add tests, or - " +
            "if the suite is deliberately empty for now - declare it in $manifestPath " +
            "under knownEmpty with the milestone that fills it.")
    }

    if ($total -gt 0 -and $exempt) {
        $entry = $knownEmpty[$name]
        $failures.Add(
            "$name : STALE EXEMPTION. It now has $total test(s) but is still listed under knownEmpty " +
            "in $manifestPath (turnsOn: $($entry.turnsOn)). Delete that entry - it is what " +
            "keeps the suite allowed to go empty again unnoticed.")
    }

    $summary.Add([pscustomobject]@{
        Suite    = $name
        Total    = $total
        Passed   = $passed
        Failed   = $failed
        ExitCode = $testExitCode
        Status   = if (-not $countsAreReal) { 'DID NOT RUN - no TRX' }
                   elseif ($total -eq 0 -and $exempt) { "empty (allowed until $($knownEmpty[$name].turnsOn))" }
                   elseif ($total -eq 0) { 'EMPTY - not declared' }
                   elseif ($failed -gt 0 -or $testExitCode -ne 0) { 'FAILED' }
                   else { 'passed' }
    })

    if (-not $countsAreReal) { $groupHadUnmeasuredSuite = $true }
    if ($exempt) { $exemptSuitesInGroup++ }
}

Write-Section 'Suite summary'
$summary | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$executed = ($summary | Measure-Object -Property Total -Sum).Sum
Write-Host "Tests executed across group '$Group': $executed"

if ($executed -eq 0) {
    # A whole CI job that asserted nothing is exactly the vacuous pass this script
    # exists to prevent, so it is only ever a WARNING when every suite in the
    # group is a declared, milestone-tagged knownEmpty — i.e. when somebody has
    # already written down why, and signed up to a milestone that deletes the
    # note. Any other route to zero is a failure, not a yellow line in a log that
    # nobody reads on a green run.
    $everySuiteExempt = ($exemptSuitesInGroup -eq $selected.Count) -and -not $groupHadUnmeasuredSuite

    if ($everySuiteExempt) {
        Write-CiWarning "Group '$Group' executed 0 tests. Every suite in it is a declared knownEmpty in $manifestPath, so this is allowed - but the job asserts nothing about behaviour until those milestones land."
    } else {
        $failures.Add(
            "Group '$Group' executed 0 tests across $($selected.Count) suite(s), and they are NOT all declared " +
            "knownEmpty in $manifestPath. The job would otherwise be a green tick over nothing, which is the " +
            "one outcome this script exists to prevent. See the per-suite rows above for which suite is empty " +
            "or did not run.")
    }
}

Exit-WithFailures -Failures $failures.ToArray() -CheckName "Test group '$Group'"
