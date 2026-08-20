#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Points this checkout's git hooks at `build/git/hooks`, so the tracked hooks
    are the ones that run.

.DESCRIPTION
    `.git/hooks` is not versioned, which is why a hook nobody installs is a hook
    nobody has. Setting `core.hooksPath` to a tracked directory makes the hooks
    arrive with the clone and change with a reviewable diff.

    Run once per clone. Idempotent — running it again re-reports the same state
    and changes nothing.

    ⚠️ `core.hooksPath` replaces `.git/hooks` wholesale rather than adding to it.
    If that directory ever holds a hook of its own, move it here first; this
    script refuses to proceed while one is there, rather than silently disabling
    it.

.PARAMETER Uninstall
    Unset `core.hooksPath`, returning the checkout to `.git/hooks`.

.EXAMPLE
    pwsh build/git/Install-GitHooks.ps1
#>
[CmdletBinding()]
param([switch]$Uninstall)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (& git rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0) { throw 'Not inside a git repository.' }
$root = $root.Trim()

if ($Uninstall) {
    & git config --unset core.hooksPath 2>&1 | Out-Null
    Write-Host 'core.hooksPath unset — this checkout is back on .git/hooks.' -ForegroundColor Cyan
    return
}

$gitDir = (& git rev-parse --git-common-dir).Trim()
if (-not [IO.Path]::IsPathRooted($gitDir)) { $gitDir = Join-Path $root $gitDir }

$existing = @(Get-ChildItem -LiteralPath (Join-Path $gitDir 'hooks') -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -ne '.sample' })

if ($existing.Count -gt 0) {
    throw ("$gitDir/hooks holds $($existing.Count) active hook(s): $($existing.Name -join ', '). " +
        'core.hooksPath would disable them without saying so. Move them into build/git/hooks first.')
}

& git config core.hooksPath 'build/git/hooks'

# Written on a Windows machine where the execute bit does not survive the working
# tree, so it is set in the index — which is the copy a Linux clone reads.
& git update-index --chmod=+x build/git/hooks/post-merge 2>&1 | Out-Null

Write-Host "core.hooksPath = build/git/hooks" -ForegroundColor Cyan
Write-Host 'Installed: post-merge (reaps branches and worktrees that main has absorbed).'
