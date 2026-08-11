#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Validates every JSON file under SlayIdleRepeat.Data, and audits the 📐 TUNABLE
    markers in game-design/ against the schema keys.

.DESCRIPTION
    14 §6 🔒: "JSON is validated at build time against schemas in
    SlayIdleRepeat.Data/schema/. The build fails on unknown IDs, missing icons,
    out-of-range values, orphaned references or duplicate IDs."

    14 §6 🔒: "a build-time check enumerates every 📐 marker in the documentation
    set against the schema keys and fails on a mismatch. That check is what stops
    the tuning surface eroding over eighteen months."

    M0-02 authored this script as a structural floor (strict parse, duplicate-key
    detection, schema<->data orphan pairing) with the instruction:

        "M0-09 REPLACES THE BODY, NOT THE INTERFACE. Keep the parameters, the exit
         codes (0 pass / 1 fail) and the script path, and .github/workflows/ci.yml
         needs no edit when the real harness lands."

    That is exactly what happened. The body is now a call into tools/ContentValidator,
    which runs the SAME code the game loads content with - the loader, the JSON Schema
    validator, the cross-file invariants and the 📐 audit all live in
    SlayIdleRepeat.Application/Services/Content/ and are unit-tested in
    SlayIdleRepeat.Application.Tests against the in-memory fake. A CI-only validator
    written a second time in PowerShell would drift from the runtime one, and the day
    it did, CI would be green about content the game cannot load.

    What it enforces today:

      14 §6's five failure classes - unknown IDs, missing icons, out-of-range values,
      orphaned references, duplicate IDs - plus malformed JSON, duplicate object keys,
      unpaired schemas, and any JSON Schema keyword the validator does not implement
      (a hard failure, never a silent pass).

      The 📐 audit, in three directions: every marker must be claimed by a schema key,
      every numeric key in a tuning/ schema must carry a marker, and an economy-affecting
      📐 number must name a file under tuning/. Known mismatches are recorded, dated and
      reasoned in build/content/tunable-marker-baseline.json; the check fails on anything
      that file does not record, and equally on an entry it records that is no longer real.

    The one change this needed in .github/workflows/ci.yml is an actions/setup-dotnet
    step on the content-validation job: the check is now .NET rather than PowerShell.

.PARAMETER RepositoryRoot
    Defaults to the repository containing this script.

.PARAMETER DataRoot
    Defaults to <repo>/SlayIdleRepeat.Data.

.PARAMETER Configuration
    Build configuration for the validator tool. Defaults to Release.

.PARAMETER NoBuild
    Skip building the tool - use when a previous step already built the solution.

.EXAMPLE
    pwsh build/ci/Invoke-ContentValidation.ps1
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$DataRoot,
    [string]$Configuration = 'Release',
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned rather than inherited, for the same reason as Invoke-UnitTests.ps1: the
# `& dotnet` below must return its exit code, not throw. $false is the pwsh 7.6.3
# default and is what the runner has today, but the default has moved before, and
# a runner-image change must not be able to alter this script's control flow
# without anything going red.
$PSNativeCommandUseErrorActionPreference = $false

. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-RepositoryRoot -Override $RepositoryRoot
if (-not $DataRoot) { $DataRoot = Join-Path $root 'SlayIdleRepeat.Data' }

$designDocs = Join-Path $root 'game-design'
$baseline = Join-Path $root 'build/content/tunable-marker-baseline.json'
$project = Join-Path $root 'tools/ContentValidator/SlayIdleRepeat.ContentValidator.csproj'

Write-Section 'Content validation (14 §6, §13)'
Write-Host "Data root   : $DataRoot"
Write-Host "Design docs : $designDocs"
Write-Host "Baseline    : $baseline"
Write-Host "Validator   : $project"

$failures = [System.Collections.Generic.List[string]]::new()

foreach ($required in @(
        @{ Path = $DataRoot;    What = 'the data root' },
        @{ Path = $designDocs;  What = 'the design-doc set the 📐 audit reads' },
        @{ Path = $baseline;    What = 'the 📐 baseline (a missing one is not the same as a clean run)' },
        @{ Path = $project;     What = 'the validator project' })) {
    if (-not (Test-Path -LiteralPath $required.Path)) {
        $failures.Add("'$($required.Path)' does not exist - $($required.What).")
    }
}

if ($failures.Count -gt 0) {
    Exit-WithFailures -Failures $failures.ToArray() -CheckName 'Content validation'
}

$arguments = @(
    'run', '--project', $project, '--configuration', $Configuration
)
if ($NoBuild) { $arguments += '--no-build' }
$arguments += @(
    '--',
    '--repository-root', $root,
    '--data-root', $DataRoot,
    '--design-docs', $designDocs,
    '--baseline', $baseline
)

Write-Section 'Running the validator'
& dotnet @arguments
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    # One line, not a re-listing: the tool has already printed every finding with
    # its location and what to do about it, and repeating them here would only
    # make the log harder to read.
    $failures.Add("Content validation failed (exit $exitCode). Every finding is listed above, " +
        "each naming the document, the JSON pointer and the design-doc rule it breaks.")
}

# Exit-WithFailures rather than a hand-rolled exit 0/1. It is the single exit path
# every other check in build/ci uses, and going through it is what puts the same
# '=== <check> result ===' banner and OK/ERROR line in this job's log as in the
# other three. A reader scanning four CI logs for the one that failed should not
# have to know that this one reports itself differently.
Exit-WithFailures -Failures $failures.ToArray() -CheckName 'Content validation'
