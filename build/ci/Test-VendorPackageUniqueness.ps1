#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails if a vendor SDK is referenced by more than one project, or by a project
    that is not an adapter.

.DESCRIPTION
    14 §1.1 🔒, the no-lock-in rule:

        "No driver or vendor SDK anywhere outside an adapter ... Enforced by
         architecture tests (23 §6) plus a CI check that fails if a vendor
         PackageReference appears in more than one .csproj."

    Two independent assertions, because they catch different mistakes:

      A9-UNIQUE    A vendor package in two projects means two places know how to
                   talk to that vendor, and replacing it is now a two-site change
                   instead of a one-site change. This is the check 14 §1.1 names.

      A9-LOCATION  A vendor package in a non-adapter project means the dependency
                   rule (14 §4.1) has been breached at the package level: Core,
                   Application, Contracts, the composition roots, the tools and
                   the test suites must reach a vendor only through an adapter.

    Everything not listed in build/ci/non-vendor-packages.json counts as a vendor
    SDK. Adding to that list is a deliberate, reviewable diff.

    ⚠️ Deliberate overlap: M0-08's SlayIdleRepeat.Architecture.Tests may assert
    the same rule by reflection over assembly references. Both are wanted. The
    architecture test sees what an assembly actually binds; this script sees what
    the build was told to fetch, and it still runs when the solution does not
    compile. Do not delete one because the other exists.

.EXAMPLE
    pwsh build/ci/Test-VendorPackageUniqueness.ps1
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$AllowListPath,

    # Projects allowed to hold vendor SDKs. Adapter projects, and nothing else.
    [string[]]$AdapterPathPrefix = @('src/adapters/')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-RepositoryRoot -Override $RepositoryRoot
if (-not $AllowListPath) { $AllowListPath = Join-Path $PSScriptRoot 'non-vendor-packages.json' }

Write-Section 'Vendor package uniqueness (14 §1.1 / A9)'
Write-Host "Repository root : $root"
Write-Host "Allow-list      : $AllowListPath"

$allowList = (Get-Content -Raw -LiteralPath $AllowListPath | ConvertFrom-Json).packages

function Test-IsAllowed {
    param([Parameter(Mandatory)][string]$PackageId)
    foreach ($entry in $allowList) {
        if ($entry.id.EndsWith('*')) {
            if ($PackageId.StartsWith($entry.id.TrimEnd('*'), [StringComparison]::OrdinalIgnoreCase)) { return $true }
        } elseif ($PackageId -eq $entry.id) {
            return $true
        }
    }
    return $false
}

# ------------------------------------------------------------------ gather
$projects = @(Get-ChildItem -Path $root -Filter '*.csproj' -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object FullName)

if ($projects.Count -eq 0) {
    Write-CiError "No .csproj files found under $root. The check cannot be meaningful - refusing to report success."
    exit 1
}

Write-Host "Projects scanned: $($projects.Count)"

# package id -> list of relative project paths
$usage = [ordered]@{}

foreach ($project in $projects) {
    $relative = Get-RelativePath -Root $root -Path $project.FullName
    $xml = [xml](Get-Content -Raw -LiteralPath $project.FullName)

    # SDK-style projects carry no default namespace, so a plain XPath is enough.
    # Update= (rather than Include=) retunes an existing reference and does not
    # introduce a dependency, so it is not counted.
    foreach ($node in $xml.SelectNodes('//PackageReference[@Include]')) {
        $id = $node.GetAttribute('Include')
        if (-not $usage.Contains($id)) { $usage[$id] = [System.Collections.Generic.List[string]]::new() }
        $usage[$id].Add($relative)
    }
}

# ------------------------------------------------------------------- assert
$failures = [System.Collections.Generic.List[string]]::new()
$report = [System.Collections.Generic.List[object]]::new()

foreach ($id in $usage.Keys) {
    $projectPaths = $usage[$id]
    $isVendor = -not (Test-IsAllowed -PackageId $id)

    if ($isVendor -and $projectPaths.Count -gt 1) {
        $failures.Add(
            "A9-UNIQUE: vendor package '$id' is referenced by $($projectPaths.Count) projects " +
            "($($projectPaths -join ', ')). 14 §1.1 allows a vendor SDK in exactly one adapter. " +
            "If this package is test infrastructure rather than a vendor SDK, add it to " +
            "build/ci/non-vendor-packages.json with the reason.")
    }

    if ($isVendor) {
        foreach ($projectPath in $projectPaths) {
            $inAdapter = $false
            foreach ($prefix in $AdapterPathPrefix) {
                if ($projectPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $inAdapter = $true }
            }
            if (-not $inAdapter) {
                $failures.Add(
                    "A9-LOCATION: vendor package '$id' is referenced by '$projectPath', which is not an " +
                    "adapter. 14 §1.1: no driver or vendor SDK anywhere outside an adapter. Move it behind " +
                    "a port, or add it to build/ci/non-vendor-packages.json if it is test infrastructure.")
            }
        }
    }

    $report.Add([pscustomobject]@{
        Package  = $id
        Kind     = if ($isVendor) { 'vendor' } else { 'allow-listed' }
        Projects = $projectPaths.Count
        Where    = ($projectPaths -join ', ')
    })
}

Write-Section 'Package inventory'
$report | Sort-Object Kind, Package | Format-Table -AutoSize | Out-String -Width 220 | Write-Host

$vendorCount = @($report | Where-Object { $_.Kind -eq 'vendor' }).Count
Write-Host "Vendor SDKs: $vendorCount   Allow-listed: $($report.Count - $vendorCount)"

Exit-WithFailures -Failures $failures.ToArray() -CheckName 'Vendor package uniqueness'
