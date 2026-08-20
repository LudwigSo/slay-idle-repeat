#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Removes the worktrees and local branches that a target ref has already
    absorbed, and reports — rather than deletes — everything it is not certain
    about.

.DESCRIPTION
    This repository's workflow fans out: `kickoff-milestone` gives most tasks
    their own worktree and every task its own `feature-M<N>-<nn>-<slug>` branch,
    `milestone-review` adds a `review/M<N>`, and each run leaves an integration
    branch behind. Nothing reaped them, so by the end of M7 the repository held
    **74 merged branches and 13 worktree directories**, five of which git had
    already forgotten. The debris is not free: `git branch` stops being readable,
    and a stale worktree is a second copy of the repo that quietly answers
    questions about an old commit.

    So: after work lands on `main`, everything `main` now contains goes away.

    🔒 **It deletes only what the target ref already has.** Branch removal goes
    through `git branch -d`, which refuses an unmerged branch itself, and every
    worktree's branch is checked with `merge-base --is-ancestor` first. A
    `--force` path does not exist here on purpose: the one thing this script must
    never do is be the reason work disappeared.

    🔒 **A dirty worktree is kept, whatever its branch says.** The uncommitted
    change is the work — the merged branch underneath it says nothing about the
    edit sitting on top. Untracked files count as dirty: the enemy-health-bar
    worktree that prompted this script held its only copy of `Run-Game.ps1` as an
    untracked file.

    ⚠️ **Directories git no longer knows about are reported, never removed.** An
    unregistered worktree directory cannot be asked what it contains — its
    `.git` file may point at a path that no longer exists (the repository was
    moved from `G:` to `C:` and five orphans were left pointing at the old drive).
    Verifying one is a judgement call, so it is handed to a human with its file
    count rather than deleted on a guess.

    Remotes are never touched. Nothing is pushed, and no remote-tracking branch
    is deleted: this is a local-hygiene script.

.PARAMETER Into
    The ref that decides what is finished. Defaults to `main`.

.PARAMETER Protect
    Branches never deleted regardless of merge status. `main`/`master` plus
    whatever is checked out in any worktree are protected unconditionally.

.PARAMETER DryRun
    Report what would happen and change nothing.

.PARAMETER Quiet
    Print only the summary line and anything that was kept. Used by the
    post-merge hook, where a wall of text after every merge is its own problem.

.EXAMPLE
    pwsh build/git/Remove-MergedRefs.ps1

.EXAMPLE
    pwsh build/git/Remove-MergedRefs.ps1 -Into milestone/M8 -DryRun
#>
[CmdletBinding()]
param(
    [string]$Into = 'main',
    [string[]]$Protect = @('main', 'master'),
    [switch]$DryRun,
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Note {
    param([string]$Message, [string]$Colour = 'Gray')
    if (-not $Quiet) { Write-Host $Message -ForegroundColor $Colour }
}

function Invoke-Git {
    <#
        Native git, with stderr folded in and the exit code preserved.

        🔒 Takes ONE array and is deliberately not an advanced function. Written
        first with `[Parameter(ValueFromRemainingArguments)]`, it turned every
        short git flag into a PowerShell binding question: `-d` is a unique prefix
        of the common parameter `-Debug`, so `Invoke-Git branch -d $b` reached git
        as `git branch $b` — which CREATES a branch. It reported "already exists"
        on every branch it was asked to delete, and deleted none.
    #>
    param([string[]]$GitArgs)
    $output = & git @GitArgs 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join [Environment]::NewLine) }
}

$repoRoot = (Invoke-Git @('rev-parse', '--show-toplevel'))
if ($repoRoot.ExitCode -ne 0) { throw "Not inside a git repository: $($repoRoot.Output)" }
$root = $repoRoot.Output.Trim()

if ((Invoke-Git @('rev-parse', '--verify', '--quiet', "$Into^{commit}")).ExitCode -ne 0) {
    throw "No such ref: '$Into'. Nothing can be judged finished without one."
}

# An UNFINISHED merge or rebase means HEAD is mid-flight and "merged into $Into"
# is not yet a settled question for anything.
#
# 🔴 Tested by unresolved index entries and the rebase state directories, NOT by
# the presence of MERGE_HEAD. MERGE_HEAD still exists while the `post-merge` hook
# runs — git clears it afterwards — so a MERGE_HEAD guard makes this script abort
# on exactly the occasion it was written for, and abort silently under -Quiet. It
# did, and the first end-to-end test of the hook removed nothing and said nothing.
$conflicts = @(& git ls-files --unmerged)
$gitDir = (Invoke-Git @('rev-parse', '--git-dir')).Output.Trim()
$rebasing = @('rebase-merge', 'rebase-apply') | Where-Object { Test-Path (Join-Path $gitDir $_) }

if ($conflicts.Count -gt 0 -or $rebasing) {
    $what = if ($conflicts.Count -gt 0) { 'unresolved conflicts' } else { 'a rebase in progress' }
    Write-Host "cleanup skipped: $what. Nothing removed." -ForegroundColor Yellow
    return
}

$removedWorktrees = [System.Collections.Generic.List[string]]::new()
$removedBranches = [System.Collections.Generic.List[string]]::new()
$kept = [System.Collections.Generic.List[string]]::new()

# ── worktrees ───────────────────────────────────────────────────────────────────────────────────
# Parsed from --porcelain rather than the human listing: a path with a space in it
# makes the human listing ambiguous and the porcelain one exact.
$worktrees = @()
$current = $null
foreach ($line in (& git worktree list --porcelain)) {
    if ($line -match '^worktree (.+)$') {
        if ($current) { $worktrees += $current }
        $current = [pscustomobject]@{ Path = $Matches[1]; Branch = $null; Detached = $false }
    }
    elseif ($line -match '^branch refs/heads/(.+)$' -and $current) { $current.Branch = $Matches[1] }
    elseif ($line -eq 'detached' -and $current) { $current.Detached = $true }
}
if ($current) { $worktrees += $current }

$mainWorktree = $worktrees | Select-Object -First 1
$checkedOut = @($worktrees | Where-Object { $_.Branch } | ForEach-Object { $_.Branch })

foreach ($wt in ($worktrees | Select-Object -Skip 1)) {
    $label = (Split-Path $wt.Path -Leaf)

    if ($wt.Detached -or -not $wt.Branch) {
        $kept.Add("worktree $label - detached HEAD, so there is no branch to call finished")
        continue
    }

    if ((Invoke-Git @('merge-base', '--is-ancestor', $wt.Branch, $Into)).ExitCode -ne 0) {
        $kept.Add("worktree $label - its branch $($wt.Branch) is NOT in $Into")
        continue
    }

    $dirt = @(& git -C $wt.Path status --porcelain)
    if ($dirt.Count -gt 0) {
        $kept.Add("worktree $label - $($dirt.Count) uncommitted change(s), branch merged or not")
        continue
    }

    if ($DryRun) {
        Write-Note "would remove worktree: $label ($($wt.Branch))"
    }
    else {
        $result = Invoke-Git @('worktree', 'remove', $wt.Path)
        if ($result.ExitCode -ne 0) {
            $kept.Add("worktree $label - git refused to remove it: $($result.Output)")
            continue
        }
        Write-Note "removed worktree: $label"
    }
    $removedWorktrees.Add($label)
    # Its branch is no longer checked out anywhere, so the branch pass below is
    # free to take it. Without this the branch survives until the next run, and
    # the next run is a merge that may never come.
    $checkedOut = @($checkedOut | Where-Object { $_ -ne $wt.Branch })
}

if (-not $DryRun) { $null = Invoke-Git @('worktree', 'prune') }

# ── branches ────────────────────────────────────────────────────────────────────────────────────
# `git branch --merged` is the same question `git branch -d` asks, so the two
# agree by construction; the -d is still what performs the deletion, and it is
# still allowed to refuse.
$protected = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($p in ($Protect + $checkedOut + @($Into))) { if ($p) { $null = $protected.Add($p) } }

foreach ($branch in (& git branch --format='%(refname:short)' --merged $Into)) {
    if ([string]::IsNullOrWhiteSpace($branch)) { continue }
    if ($protected.Contains($branch)) { continue }

    if ($DryRun) {
        Write-Note "would delete branch: $branch"
        $removedBranches.Add($branch)
        continue
    }

    $result = Invoke-Git @('branch', '-d', $branch)
    if ($result.ExitCode -ne 0) {
        $kept.Add("branch $branch - git refused to delete it: $($result.Output)")
        continue
    }
    Write-Note "deleted branch: $branch"
    $removedBranches.Add($branch)
}

# ── directories git has forgotten ───────────────────────────────────────────────────────────────
$orphans = @()
$worktreeHome = Join-Path $root '.claude/worktrees'
if (Test-Path $worktreeHome) {
    # Resolved one at a time and guarded: a worktree removed moments ago no longer
    # resolves, and under StrictMode a missing path is an exception rather than a
    # null. Its directory is gone too, so dropping it from the known set is right.
    $known = @()
    foreach ($wt in $worktrees) {
        $resolved = Resolve-Path -LiteralPath $wt.Path -ErrorAction SilentlyContinue
        if ($resolved) { $known += $resolved.Path }
    }
    foreach ($dir in (Get-ChildItem -LiteralPath $worktreeHome -Directory -ErrorAction SilentlyContinue)) {
        if ($known -contains $dir.FullName) { continue }

        $files = @(Get-ChildItem -LiteralPath $dir.FullName -Recurse -File -Force -ErrorAction SilentlyContinue)
        if ($files.Count -eq 0) {
            if (-not $DryRun) { Remove-Item -LiteralPath $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue }
            Write-Note "removed empty orphan directory: $($dir.Name)"
            continue
        }

        $orphans += "$($dir.Name) ($($files.Count) files)"
    }
}

# ── the report ──────────────────────────────────────────────────────────────────────────────────
$verb = if ($DryRun) { 'would remove' } else { 'removed' }
$summary = "cleanup vs $Into : $verb $($removedBranches.Count) branch(es), $($removedWorktrees.Count) worktree(s)"
if ($kept.Count -gt 0 -or $orphans.Count -gt 0) {
    $summary += ", kept $($kept.Count + $orphans.Count)"
}
Write-Host $summary -ForegroundColor Cyan

foreach ($k in $kept) { Write-Host "  kept: $k" -ForegroundColor Yellow }

foreach ($o in $orphans) {
    Write-Host "  kept: unregistered directory .claude/worktrees/$o - git cannot vouch for its contents; check it and remove it by hand" -ForegroundColor Yellow
}
