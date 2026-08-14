#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Asset provenance gate: no delivered asset without a provenance record, and no
    provenance record for an unknown asset id.

.DESCRIPTION
    15 §G: "Confirm the current commercial terms in writing before the first batch and
    keep provenance records (job ID, prompt, seed, --sref, date) for every asset. Legal
    prerequisite, not a formality."

    20 §2.1: "keep provenance records (tool, version, prompt, date) for every generated
    file, exactly as for art (doc 15 §G) ... This is a legal prerequisite, not a
    formality."

    20 §6's QA list: "Provenance record exists for every generated file" and "Commercial
    licence for each tool confirmed in writing".

    A thin shell over tools/AssetProvenance, for the same reason
    Invoke-ContentValidation.ps1 is a thin shell over tools/ContentValidator: every rule
    lives in the tool, where SlayIdleRepeat.AssetProvenance.Tests exercises it. A CI-only
    copy of these rules written a second time in PowerShell would drift, and the day it
    did, CI would be green about assets nobody can prove the licence for.

    ⚠️ WHAT THIS ASSERTS TODAY, AND WHAT IT WILL ASSERT LATER.

    Zero assets are delivered right now: every generating task in M8 is capability-
    blocked (M8 kickoff, 2026-08-12). So the FORWARD direction - every delivered asset
    has a record - quantifies over an empty set and cannot fail on its own. Three things
    stop that being a vacuous green:

      1. The register floor. The gate asserts M8-09's register still holds >= 1000 ids
         and still contains five named canary ids and one named cut id. A register that
         failed to load, or shrank, is RED - it is not silently "nothing to check".
      2. The reverse direction is live from the first record ever written, and so are the
         record-field rules and the 20 §6 licence rule.
      3. DeliveryDeclaration.AwaitingFirstDelivery. While it is true the gate reports
         "AWAITING FIRST DELIVERY - 0 of 1048 uncut asset slots ... 0 pairings verified"
         rather than a bare pass, and the moment one asset lands under assets/ the build
         FAILS until somebody flips it. It fails in the other direction too: flipped
         early, with nothing delivered, it is equally a failure.

    Once assets exist, the forward direction becomes the load-bearing one: every file
    under assets/ must name a register slot, carry a record, and have been made with a
    tool whose commercial licence is confirmed in writing.

.PARAMETER RepositoryRoot
    Defaults to the repository containing this script.

.PARAMETER DataRoot
    Defaults to <repo>/game-data - where M8-09's register lives.

.PARAMETER StoreRoot
    Defaults to <repo>/assets/provenance. 🔒 Ruling A8: the store is NOT under game-data,
    because everything under game-data enters the ContentSnapshot and would move the
    content version stamp once per generated asset.

.PARAMETER DeliveryRoot
    Defaults to <repo>/assets - scanned recursively, minus the provenance store.

.PARAMETER Configuration
    Build configuration for the tool. Defaults to Release.

.PARAMETER NoBuild
    Skip building the tool - use when a previous step already built the solution.

.EXAMPLE
    pwsh build/ci/Invoke-ProvenanceGate.ps1
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$DataRoot,
    [string]$StoreRoot,
    [string]$DeliveryRoot,
    [string]$Configuration = 'Release',
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned rather than inherited, for the same reason as Invoke-ContentValidation.ps1: the
# `& dotnet` below must return its exit code, not throw.
$PSNativeCommandUseErrorActionPreference = $false

. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-RepositoryRoot -Override $RepositoryRoot
if (-not $DataRoot)     { $DataRoot     = Join-Path $root 'game-data' }
if (-not $StoreRoot)    { $StoreRoot    = Join-Path $root 'assets/provenance' }
if (-not $DeliveryRoot) { $DeliveryRoot = Join-Path $root 'assets' }

$project = Join-Path $root 'tools/AssetProvenance/SlayIdleRepeat.AssetProvenance.csproj'

Write-Section 'Asset provenance gate (15 §B0, §G · 20 §2.1, §6)'
Write-Host "Register : $DataRoot"
Write-Host "Store    : $StoreRoot"
Write-Host "Delivery : $DeliveryRoot"
Write-Host "Tool     : $project"

$failures = [System.Collections.Generic.List[string]]::new()

foreach ($required in @(
        @{ Path = $DataRoot;     What = "M8-09's asset register" },
        @{ Path = $StoreRoot;    What = 'the provenance store (a missing store is not an empty one)' },
        @{ Path = $DeliveryRoot; What = 'the delivery root (a missing root would scan as zero deliveries)' },
        @{ Path = $project;      What = 'the provenance tool' })) {
    if (-not (Test-Path -LiteralPath $required.Path)) {
        $failures.Add("'$($required.Path)' does not exist - $($required.What).")
    }
}

if ($failures.Count -gt 0) {
    Exit-WithFailures -Failures $failures.ToArray() -CheckName 'Asset provenance gate'
}

$arguments = @('run', '--project', $project, '--configuration', $Configuration)
if ($NoBuild) { $arguments += '--no-build' }
$arguments += @(
    '--',
    'check',
    '--repository-root', $root,
    '--data-root', $DataRoot,
    '--store', $StoreRoot,
    '--delivery-root', $DeliveryRoot
)

Write-Section 'Running the gate'
& dotnet @arguments
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    # One line, not a re-listing: the tool has already printed every violation with its
    # code, its subject and what to do about it.
    $failures.Add("The asset provenance gate failed (exit $exitCode). Every violation is listed " +
        "above, each naming the rule that fired, the asset or file it fired on, and the design-doc " +
        "requirement behind it.")
}

Exit-WithFailures -Failures $failures.ToArray() -CheckName 'Asset provenance gate'
