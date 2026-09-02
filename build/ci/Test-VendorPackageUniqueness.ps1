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
    [string]$LocationExceptionPath,

    # Projects allowed to hold vendor SDKs. Adapter projects, and nothing else.
    [string[]]$AdapterPathPrefix = @('src/adapters/')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-RepositoryRoot -Override $RepositoryRoot
if (-not $AllowListPath) { $AllowListPath = Join-Path $PSScriptRoot 'non-vendor-packages.json' }
if (-not $LocationExceptionPath) { $LocationExceptionPath = Join-Path $PSScriptRoot 'vendor-location-exceptions.json' }

Write-Section 'Vendor package uniqueness (14 §1.1 / A9)'
Write-Host "Repository root : $root"
Write-Host "Allow-list      : $AllowListPath"
Write-Host "Location pins   : $LocationExceptionPath"

$allowList = (Get-Content -Raw -LiteralPath $AllowListPath | ConvertFrom-Json).packages

# A9-LOCATION pins. Deliberately NOT part of $allowList: a pinned package stays a vendor package and
# stays subject to A9-UNIQUE. See the $comment block in vendor-location-exceptions.json.
$locationExceptions = @((Get-Content -Raw -LiteralPath $LocationExceptionPath | ConvertFrom-Json).exceptions)
$usedLocationExceptions = [System.Collections.Generic.HashSet[string]]::new()

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
# Directories that hold .csproj files which are not part of this product's build.
#
#   bin/, obj/     build output; a copy of a project already scanned.
#   artifacts/     this repo's own output root (see Directory.Build.props).
#   .nuget/        .github/workflows/ci.yml sets NUGET_PACKAGES inside the
#                  workspace, so the restored package cache — which is full of
#                  third-party .csproj files — lands under the repository root.
#                  Scanning it would report every NuGet package on earth as a
#                  vendor SDK in a non-adapter location.
#   spikes/        throwaway export probes. They are not in
#                  SlayIdleRepeat.sln, they are not shipped, and 14 §1.1's
#                  "no vendor SDK outside an adapter" is a rule about the
#                  product's dependency graph, not about a scratch project whose
#                  entire purpose is to try a vendor toolchain.
#   .claude       Agent WORKTREES. .claude/worktrees/<name>/ is a full checkout
#                  of this repository, so every .csproj in one duplicates a
#                  .csproj already scanned - and this project's own dispatch
#                  skill MANDATES worktrees, so any conductor with agents in
#                  flight made A9-UNIQUE fire on Sentry, Npgsql and every other
#                  vendor package at once, plus A9-LOCATION on every path that
#                  does not start with src/adapters/. Measured at the M1 review:
#                  674 projects scanned with 12 stale worktrees present, 278
#                  with 7, against 228 real ones. CI on a fresh clone never saw
#                  it, which is what made it a trap locally.
$excludedDirectorySegments = @('bin', 'obj', '.nuget', 'artifacts', 'spikes', '.claude')

# The segments are regex-ESCAPED before joining: '.nuget' and '.claude' both
# carry a metacharacter the old concatenation passed through raw, so '.' matched
# any character rather than a literal dot.
$escapedSegments = $excludedDirectorySegments | ForEach-Object { [regex]::Escape($_) }
$excludePattern = '[\\/](' + ($escapedSegments -join '|') + ')[\\/]'

$projects = @(Get-ChildItem -Path $root -Filter '*.csproj' -Recurse -File |
    Where-Object { $_.FullName -notmatch $excludePattern } |
    Sort-Object FullName)

if ($projects.Count -eq 0) {
    Write-CiError "No .csproj files found under $root. The check cannot be meaningful - refusing to report success."
    exit 1
}

Write-Host "Projects scanned: $($projects.Count)  (excluding $($excludedDirectorySegments -join ', '))"

# package id -> list of relative project paths
$usage = [ordered]@{}

# How many projects yielded at least one PackageReference. See the vacuity guard
# below for why this is counted rather than assumed.
$projectsWithAnyPackageReference = 0

foreach ($project in $projects) {
    $relative = Get-RelativePath -Root $root -Path $project.FullName
    $xml = [xml](Get-Content -Raw -LiteralPath $project.FullName)

    # local-name() rather than a plain '//PackageReference'. No .csproj in this
    # repo declares a default xmlns today, and SDK-style projects normally do
    # not — but a hand-edited or tool-migrated project can carry the legacy
    # http://schemas.microsoft.com/developer/msbuild/2003 namespace, and a
    # namespace-sensitive XPath returns ZERO nodes for it. That would leave
    # $usage empty and this script would report "0 vendor SDKs, passed" over a
    # project it never actually read — a vacuous pass on the one check whose
    # entire job is catching a vendor SDK outside an adapter. A gate is allowed
    # to fail falsely; it is never allowed to pass emptily.
    #
    # Update= (rather than Include=) retunes an existing reference and does not
    # introduce a dependency, so it is not counted.
    $nodes = @($xml.SelectNodes("//*[local-name()='PackageReference'][@Include]"))
    if ($nodes.Count -gt 0) { $projectsWithAnyPackageReference++ }

    foreach ($node in $nodes) {
        $id = $node.GetAttribute('Include')
        if (-not $usage.Contains($id)) { $usage[$id] = [System.Collections.Generic.List[string]]::new() }
        $usage[$id].Add($relative)
    }
}

# Vacuity guard. Even with local-name(), a future change to how references are
# declared (Central Package Management moving them wholesale into
# Directory.Packages.props, an XML reader change, a broken glob) could empty
# $usage without emptying $projects. A .NET repository of this size in which not
# one project references one package is not a clean repository; it is a broken
# scan. Fail rather than tick.
if ($projectsWithAnyPackageReference -eq 0) {
    Write-CiError (
        "Scanned $($projects.Count) project(s) and found not a single <PackageReference Include=... />. " +
        "That is not a plausible state for this repository, so it is far more likely the scan is broken " +
        "than that the dependency graph is empty - and a broken scan here reports 'no vendor SDKs, passed'. " +
        "Check the XPath in this script and whether references have moved (e.g. to Central Package Management).")
    exit 1
}

Write-Host "Projects with >=1 PackageReference: $projectsWithAnyPackageReference"

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

            # A pinned location is NOT an allow-list entry: the package stays a vendor package, so
            # A9-UNIQUE above still governs it in full. Only its permitted location moves.
            $pinned = $false
            foreach ($exception in $locationExceptions) {
                if ($exception.id -eq $id -and
                    $exception.project.Replace('\', '/').Equals($projectPath, [StringComparison]::OrdinalIgnoreCase)) {
                    $pinned = $true
                    $usedLocationExceptions.Add("$($exception.id)|$($exception.project.Replace('\', '/'))") | Out-Null
                }
            }

            if (-not $inAdapter -and -not $pinned) {
                $failures.Add(
                    "A9-LOCATION: vendor package '$id' is referenced by '$projectPath', which is not an " +
                    "adapter. 14 §1.1: no driver or vendor SDK anywhere outside an adapter. Move it behind " +
                    "a port, add it to build/ci/non-vendor-packages.json if it is test infrastructure, or - " +
                    "only for a build-time tool that ships in no artifact - pin it in " +
                    "build/ci/vendor-location-exceptions.json with a written reason.")
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

# S4 - a declared exception must expire by itself. A pin whose (id, project) pair is no longer in the
# scan is stale: the package moved, was dropped, or the project was renamed. Left unchecked it would
# sit here pre-armed for whatever lands on that name next, which is the failure mode rule 5 in
# build/ci/test-suites.json exists to prevent. Removing the package forces its pin to go with it.
foreach ($exception in $locationExceptions) {
    $key = "$($exception.id)|$($exception.project.Replace('\', '/'))"
    if (-not $usedLocationExceptions.Contains($key)) {
        $failures.Add(
            "A9-STALE-PIN: build/ci/vendor-location-exceptions.json pins '$($exception.id)' to " +
            "'$($exception.project)', and the scan found no such vendor PackageReference there. Either " +
            "the package is gone, the project was renamed, or the package is now allow-listed as a " +
            "non-vendor package - in every case delete this pin. An exception that outlives the thing " +
            "it excepts is worse than no exception.")
    }
}

Write-Section 'Package inventory'
$report | Sort-Object Kind, Package | Format-Table -AutoSize | Out-String -Width 220 | Write-Host

$vendorCount = @($report | Where-Object { $_.Kind -eq 'vendor' }).Count
Write-Host "Vendor SDKs: $vendorCount   Allow-listed: $($report.Count - $vendorCount)   Location-pinned: $($usedLocationExceptions.Count)"

Exit-WithFailures -Failures $failures.ToArray() -CheckName 'Vendor package uniqueness'
