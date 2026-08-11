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
    integration   - end-to-end against compose (the `compose-boot` job)

.EXAMPLE
    pwsh build/ci/Invoke-UnitTests.ps1 -Group unit
    pwsh build/ci/Invoke-UnitTests.ps1 -Group architecture -NoBuild
#>
[CmdletBinding()]
param(
    [ValidateSet('unit', 'architecture', 'integration')]
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
        $failures.Add("$name : dotnet test produced no TRX at $trxPath. The run did not complete.")
    }

    $exempt = $knownEmpty.ContainsKey($name)

    if ($testExitCode -ne 0) {
        $failures.Add("$name : dotnet test exited $testExitCode ($failed failing of $total).")
    }

    if ($total -eq 0 -and -not $exempt) {
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
        Status   = if ($total -eq 0 -and $exempt) { "empty (allowed until $($knownEmpty[$name].turnsOn))" }
                   elseif ($total -eq 0) { 'EMPTY - not declared' }
                   elseif ($failed -gt 0 -or $testExitCode -ne 0) { 'FAILED' }
                   else { 'passed' }
    })
}

Write-Section 'Suite summary'
$summary | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$executed = ($summary | Measure-Object -Property Total -Sum).Sum
Write-Host "Tests executed across group '$Group': $executed"
if ($executed -eq 0) {
    Write-CiWarning "Group '$Group' executed 0 tests. This run proves only that the suites compile and the test runner is wired - it asserts nothing about behaviour. See knownEmpty in build/ci/test-suites.json for which milestone fills each suite."
}

Exit-WithFailures -Failures $failures.ToArray() -CheckName "Test group '$Group'"
