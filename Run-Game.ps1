#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds and launches Slay. Idle. Repeat. windowed, and reports the failures this repo has
    actually been bitten by rather than leaving them silent.

.DESCRIPTION
    The one supported way to see the game. It exists because the invocation was being re-derived
    every session from a Godot path nobody remembers, and because four of this project's failure
    modes look like success on the console:

      * Godot exits 0 on a fatal. A run that printed a stack trace and drew nothing still returns
        zero, so the exit code cannot be trusted and the log is read instead.
      * The engine rewrites SlayIdleRepeat.sln when it builds the C# solution itself, and it
        rewrites the hand-written project.godot when it opens the project. Both show up later as
        an unexplained working-tree change. So the assembly is built with `dotnet` here, and both
        files are hashed around everything the engine touches.
      * A save row written by an older SchemaVersion is REFUSED, by design (14 §16.6, and the M1
        kickoff's no-migrations-before-M18 ruling). The game boots, draws, and then fails every
        command. That reads as a broken build unless you know to look for it.
      * A checkout that has never been imported launches on a project whose every resource is
        missing. A fresh clone or a new worktree has a .godot holding only the mono build output,
        which looks enough like an imported project to launch and nothing like one on screen.

    Headless is deliberately not an option. Headless proves the scene parsed and the scripts bound;
    it cannot show whether anything is drawn, and seeing that is the point of asking.

.PARAMETER NoBuild
    Skip the dotnet build. For when only .tscn or game-data changed.

.PARAMETER Scene
    Launch one scene instead of the main one, e.g. -Scene game/scenes/Board.tscn. Note that a scene
    entered directly gets no composition root and so no run to read — most screens will show their
    own "cannot be shown" state.

.PARAMETER Godot
    Path to a Godot 4.7.1-mono binary, overriding discovery and $env:SIR_GODOT.

.EXAMPLE
    pwsh ./Run-Game.ps1
.EXAMPLE
    pwsh ./Run-Game.ps1 -NoBuild -Scene game/scenes/Board.tscn
#>
[CmdletBinding()]
param(
    [switch] $NoBuild,
    [string] $Scene,
    [string] $Godot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src/SlayIdleRepeat.Client'
$solution = Join-Path $root 'SlayIdleRepeat.sln'
$projectGodot = Join-Path $project 'project.godot'

if (-not (Test-Path $projectGodot)) {
    throw "No Godot project at '$project'. Run this from the repository root."
}

# ── the engine ──────────────────────────────────────────────────────────────────────────────────
# Discovery order: explicit flag, environment, then the known install. The last is a real path on
# this machine rather than a guess, and it is checked rather than assumed so a moved install fails
# here with a readable message instead of inside Godot.
$candidates = @(
    $Godot,
    $env:SIR_GODOT,
    'G:/tools/godot/4.7.1-mono/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe'
) | Where-Object { $_ }

$engine = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $engine) {
    throw @"
No Godot binary found. Tried:
$($candidates -join "`n")

This project needs 4.7.1-mono (project.godot's own requirement). Point at it with -Godot <path>
or set `$env:SIR_GODOT, and prefer the *_console.exe on Windows — the plain .exe detaches from the
terminal and its output never reaches this script.
"@
}

Write-Host "engine  : $engine"
Write-Host "project : $project"

# ── the tracked files the engine rewrites ───────────────────────────────────────────────────────
# Both are hand-maintained and both have been silently rewritten before: M7-03 found an editor
# rewrite that had DELETED the Client.Tests entry from the .sln. Hashed once, around the build AND
# the launch, because either can be the writer.
function Get-GuardHashes {
    @{
        Solution = if (Test-Path $solution) { (Get-FileHash $solution).Hash } else { $null }
        Project  = (Get-FileHash $projectGodot).Hash
    }
}

$before = Get-GuardHashes

# ── the assembly ────────────────────────────────────────────────────────────────────────────────
# Built with dotnet, never with Godot's --build-solutions: that flag implies --editor, the headless
# editor ignores --quit-after, and the process never terminates.
if (-not $NoBuild) {
    Write-Host 'building: dotnet build (client assembly)'
    & dotnet build (Join-Path $project 'SlayIdleRepeat.Client.csproj') --nologo -v q

    if ($LASTEXITCODE -ne 0) {
        throw "The client assembly did not build ($LASTEXITCODE). Godot would launch anyway and fail at the first script."
    }
}

# ── the import ──────────────────────────────────────────────────────────────────────────────────
# The presence of .godot/imported is the tell. A fresh checkout or worktree has a .godot holding
# only the mono build output, and launching that unimported opens a window on a project whose every
# resource is missing. Takes a minute or two, once per checkout.
if (-not (Test-Path (Join-Path $project '.godot/imported'))) {
    Write-Host 'importing: first run in this checkout (this takes a minute)'

    & $engine --headless --path $project --import 2>&1 |
        Where-Object { $_ -notmatch '^\[\s*\d+%\s*\]' }
}

# ── the launch ──────────────────────────────────────────────────────────────────────────────────
$engineArgs = @('--path', $project)
if ($Scene) { $engineArgs += $Scene }

$log = Join-Path ([IO.Path]::GetTempPath()) ("sir-run-" + [Guid]::NewGuid().ToString('N').Substring(0, 8) + ".log")

Write-Host "launching (windowed). log: $log`n"

& $engine @engineArgs 2>&1 | Tee-Object -FilePath $log
$exit = $LASTEXITCODE

# ── the guards ──────────────────────────────────────────────────────────────────────────────────
# Read the log, not the exit code: Godot returns 0 after a fatal.
$text = if (Test-Path $log) { Get-Content $log -Raw } else { '' }

if ($text -match 'NO MIGRATION EXISTS') {
    $version = if ($text -match 'SchemaVersion is (\d+)') { $Matches[1] } else { '??' }
    $userData = Join-Path $env:APPDATA 'Godot/app_userdata/Slay. Idle. Repeat'

    Write-Host ''
    Write-Warning @"
THE SAVE IS FROM AN OLDER SCHEMA (row says $version). The game boots and draws, and then refuses
every command -- that is 14 §16.6 working as designed, not a broken build. No migration is written
before M18, so the only way forward is a fresh profile.

The convention here is to move the rows ASIDE rather than delete them, so an old save is still on
disk if it is ever wanted:

    Get-ChildItem -LiteralPath '$userData' -File |
        Where-Object { `$_.Name -notlike '*-aside' } |
        ForEach-Object { Rename-Item -LiteralPath `$_.FullName -NewName (`$_.Name + '.schema$version-aside') }
"@
}

foreach ($pattern in @('SCRIPT ERROR', 'Failed to load script', 'ERROR: Cannot open file')) {
    if ($text -match [regex]::Escape($pattern)) {
        Write-Warning "'$pattern' appears in the log. Godot exits 0 on a fatal, so read the log above."
    }
}

$after = Get-GuardHashes

if ($before.Solution -ne $after.Solution) {
    Write-Warning 'SlayIdleRepeat.sln WAS REWRITTEN. Check that the Client.Tests entry survived (M7-03) and repair it with a targeted write, never a git checkout.'
}

if ($before.Project -ne $after.Project) {
    Write-Warning 'project.godot WAS REWRITTEN. It is hand-written and must stay hand-written — inspect the diff before keeping it.'
}

if ($exit -ne 0) {
    Write-Warning "Godot exited $exit."
}

Write-Host "`ndone. log kept at: $log"
