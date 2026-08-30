#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails unless every live architecture of 14 §8.2 left determinism results
    behind.

.DESCRIPTION
    A matrix leg that is skipped, cancelled or never scheduled leaves no red tick
    anywhere: `needs` alone would skip the guard too, and the workflow would go
    green having compared one architecture, or none. 14 §8.2 exists because ARM
    and x64 disagree about floating point, so a single-architecture determinism
    run asserts the opposite of what a green tick would say.

    This reads the uploaded artifacts rather than the matrix job's result,
    because that result is one value for the whole matrix and collapses a leg
    that ran into a leg that did not. Each live platform must have left at least
    one .trx behind, by name.

    The iOS ARM64 leg is deliberately NOT expected: it is gated off with iOS
    itself (16 D34). On the day it is re-enabled, add it to -Platforms.

.PARAMETER LegsDirectory
    Where the downloaded artifacts were unpacked. Each leg is a subdirectory
    named determinism-<platform>.

.PARAMETER Platforms
    The platforms that must have reported. Defaults to 14 §8.2's two live legs.

.PARAMETER MatrixResult
    The determinism matrix job's own result, as GitHub reports it. Anything but
    'success' fails here too, so a leg that ran and went red cannot be masked by
    a leg that ran and passed.

.EXAMPLE
    pwsh build/ci/Assert-DeterminismLegs.ps1 -LegsDirectory artifacts/legs -MatrixResult success
#>
[CmdletBinding()]
param(
    [string]$LegsDirectory = 'artifacts/legs',

    [string[]]$Platforms = @('linux-x64', 'android-arm64'),

    [string]$MatrixResult = 'success'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '_common.ps1')

Write-Section 'Determinism legs'
Write-Host "Legs directory : $LegsDirectory"
Write-Host "Expected legs  : $($Platforms -join ', ')"
Write-Host "Matrix result  : $MatrixResult"

$failures = [System.Collections.Generic.List[string]]::new()

foreach ($platform in $Platforms) {
    $directory = Join-Path $LegsDirectory "determinism-$platform"
    $trx = @(if (Test-Path -LiteralPath $directory) {
        Get-ChildItem -Path (Join-Path $directory '*.trx') -File
    })

    Write-Host ("  {0,-16} {1}" -f $platform, $(if ($trx.Count -gt 0) { "$($trx.Count) result file(s)" } else { 'NOTHING' }))

    if ($trx.Count -eq 0) {
        $failures.Add(
            "The '$platform' leg left no determinism results behind. It was skipped, cancelled, or " +
            "never scheduled - and a skipped leg leaves no red tick anywhere, so without this check " +
            "the workflow would report success having compared fewer architectures than 14 §8.2 asks " +
            "for. A single-architecture determinism run asserts the opposite of what it appears to.")
    }
}

if ($MatrixResult -ne 'success') {
    $failures.Add(
        "The determinism matrix reported '$MatrixResult'. Evidence on disk is not enough on its own: " +
        "a leg can upload its results and still have failed, and 14 §8.2 says fail on any divergence.")
}

Exit-WithFailures -Failures $failures.ToArray() -CheckName 'Determinism legs'
