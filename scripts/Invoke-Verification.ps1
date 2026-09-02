#!/usr/bin/env pwsh
<#
.SYNOPSIS
    One command that runs this repository's three quality measurements —
    SonarQube static analysis, the CRAP score, and Stryker mutation testing —
    and aggregates them into a single readable summary.

.DESCRIPTION
    The three measurements already exist as scripts and configs. What did not
    exist is a way to run all of them, in an order that does not corrupt any of
    them, and read the answer in one place. That is all this script is.

    IT MEASURES NOTHING ITSELF. Every number in the summary is read back out of
    the artifact the owning tool produced:

      SonarQube   build/ci/Invoke-SonarAnalysis.ps1, then the server's own web
                  API for the gate status and the measures.
      CRAP        scripts/Measure-Crap.ps1, then coverage/Summary.json for the
                  coverage figures and the Risk Hotspots table rendered into
                  coverage/index.html for the ranking.
      Mutation    dotnet-stryker once per stryker-config*.json, then each run's
                  reports/mutation-report.json.

    That is deliberate. A second implementation of any of these metrics would be
    a second thing to be wrong, and the two scripts it calls are emphatic about
    the silent-failure modes they guard against. This script does not re-litigate
    any of it — it runs them and reports what they said.

    ── WHY SEQUENTIAL, AND WHY THIS ORDER ────────────────────────────────────
    Not a scheduling preference. The three stages fight over the same build
    output on disk: the Sonar stage does a Release build of the whole solution
    inside a scanner session, Measure-Crap.ps1 does a Debug build, and Stryker
    does its own Debug build and then rewrites assemblies per mutant. Run any two
    at once and the loser reports numbers about a tree that changed under it.
    README.md says as much for the two Stryker runs alone. Getting the ordering
    right is most of what this script buys you.

    ── WHY PREFLIGHT COMES FIRST, FOR ALL THREE ──────────────────────────────
    A full run is roughly half an hour before Stryker even starts. Discovering
    then that SONAR_TOKEN is unset, or that MSBUILD_EXE_PATH cannot be resolved,
    costs the whole run. So every precondition for every requested stage is
    checked before the first build. Same argument Invoke-SonarAnalysis.ps1 makes
    for its own preflight; this is that argument applied across the three.

    A stage whose preconditions fail is reported BLOCKED and the others still
    run — a dead SonarQube server is no reason to learn nothing about mutation
    coverage. But a blocked stage is never quiet: it appears in the summary with
    its reason and it makes the exit code 2, because "incomplete" must not be
    readable as "verified".

    ── WHAT THE STAGES DO NOT COVER ──────────────────────────────────────────
    SlayIdleRepeat.Architecture.Tests is excluded from both coverage runs, and
    that exclusion is about correctness rather than speed — its rules read the IL
    that coverlet rewrites. It runs uninstrumented in its own CI job. Both
    Invoke-SonarAnalysis.ps1 and Measure-Crap.ps1 carry the measurement.

    Coverage and mutation are measured for SlayIdleRepeat.Core and
    SlayIdleRepeat.Application only. That allow-list lives in
    coverage.runsettings and in the stryker-config*.json files, not here.

.PARAMETER Stages
    Which of the three to run. Defaults to all of them. Anything left out is
    reported SKIPPED and does not affect the exit code — asking for two stages is
    a choice, unlike a stage that was asked for and could not run.

.PARAMETER OutputDirectory
    Where the summary and the per-stage logs land. Defaults to
    artifacts/verification (gitignored). The tools' own reports stay where they
    already write them: coverage/ for CRAP, the SonarQube server for Sonar.

.PARAMETER SonarToken
    Defaults to $env:SONAR_TOKEN. Never defaulted to a literal, never logged.
    Also used to read the gate and the measures back afterwards.

.PARAMETER NoQualityGate
    Do not wait for SonarQube's quality gate.

    ⚠️ The wait is ON by default here, unlike in Invoke-SonarAnalysis.ps1, and
    the difference is the point of this script. That script's default suits an
    exploratory analysis; a verification run wants a verdict. Waiting also
    guarantees the server has FINISHED processing before the measures are read —
    without it the numbers in the summary can be the previous analysis'.
    -NoQualityGate keeps the correctness half: the summary is then read after
    polling the compute-engine queue instead, and says the gate was not waited
    on.

.PARAMETER ReuseCoverageForCrap
    Run Measure-Crap.ps1 with -SkipTests over the coverage the Sonar stage has
    just produced, instead of running the suites a second time. Saves roughly
    twelve minutes.

    ⚠️ Not the default, and the summary labels any run that used it. The Sonar
    stage collects its coverage from a RELEASE build; Measure-Crap.ps1 on its own
    uses Debug. Both come from the same coverlet collector and the same
    coverage.runsettings, so the reused files are the right shape — but "a Release
    build yields the same per-method complexity as a Debug one" is an assumption
    this repository has not measured, and the CRAP ranking is built on exactly
    that attribute. Opt in when you want the time back and treat the ranking as
    indicative; leave it off when you intend to quote it.

.PARAMETER FullMutation
    Mutate every mutant in each project instead of only what differs from
    -MutationSince. Hours rather than tens of minutes.

.PARAMETER MutationSince
    The committish Stryker diffs against in the default (diff) mode. 'main' by
    default. Uncommitted changes are included by Stryker itself.

.PARAMETER BreakOnMutationScore
    Mutation score below which the run counts as a failed gate. 0 disables it.
    Must not exceed the `thresholds.low` of every stryker-config*.json — that is
    Stryker's own constraint, and it is checked in preflight rather than
    discovered at the end of a mutation run.

.PARAMETER TopHotspots
    How many CRAP hotspots and mutation-survivor files to list in the summary.

.PARAMETER MsBuildPath
    Path to the MSBuild.dll Stryker builds with. Defaults to
    $env:MSBUILD_EXE_PATH, then to the MSBuild.dll of the SDK `dotnet --info`
    resolves in this repository — which is what global.json pins, so the default
    is normally right and nobody has to keep the README's literal path current.

.PARAMETER Open
    Open the reports when the run finishes: coverage/index.html, each Stryker
    HTML report, and the SonarQube dashboard.

.OUTPUTS
    Exit code 0 - every requested stage ran and none tripped a gate.
    Exit code 1 - every requested stage ran and at least one tripped its gate.
    Exit code 2 - at least one requested stage could not run, or failed for a
                  reason that is not a gate. The verification is INCOMPLETE, and
                  that outranks a red gate in the exit code because an unknown is
                  worse than a known bad. Both are listed in the summary either
                  way — read it rather than the code.

.EXAMPLE
    # The whole thing.
    $env:SONAR_TOKEN = 'squ_...'
    pwsh ./scripts/Invoke-Verification.ps1

.EXAMPLE
    # No SonarQube server to hand.
    pwsh ./scripts/Invoke-Verification.ps1 -Stages Crap,Mutation

.EXAMPLE
    # The long one, before a release.
    pwsh ./scripts/Invoke-Verification.ps1 -FullMutation -BreakOnMutationScore 60
#>
[CmdletBinding()]
param(
    [ValidateSet('Sonar', 'Crap', 'Mutation')]
    [string[]]$Stages = @('Sonar', 'Crap', 'Mutation'),

    [string]$OutputDirectory,

    [string]$RepositoryRoot,

    # ---------------------------------------------------------------- SonarQube
    [string]$SonarProjectKey = 'slay-idle-repeat',
    [string]$SonarHostUrl,
    [string]$SonarToken,
    [switch]$NoQualityGate,
    [ValidateRange(30, 3600)]
    [int]$QualityGateTimeout = 300,

    # --------------------------------------------------------------------- CRAP
    [ValidateRange(1, [int]::MaxValue)]
    [int]$CrapThreshold = 30,
    [ValidateRange(1, [int]::MaxValue)]
    [int]$ComplexityThreshold = 15,
    [ValidateRange(0, [int]::MaxValue)]
    [int]$FailOnCrap = 0,
    [switch]$ReuseCoverageForCrap,

    # ----------------------------------------------------------------- Mutation
    [switch]$FullMutation,
    [string]$MutationSince = 'main',
    [ValidateRange(0, 100)]
    [int]$BreakOnMutationScore = 0,
    [string]$MsBuildPath,

    # ------------------------------------------------------------------- output
    [ValidateRange(1, 100)]
    [int]$TopHotspots = 10,

    [switch]$Open
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned for the same reason build/ci/Invoke-UnitTests.ps1, Measure-Crap.ps1 and
# Invoke-SonarAnalysis.ps1 pin it: this script reads $LASTEXITCODE after every
# stage and turns it into a status. A runner image that flips this preference
# would turn those reads into thrown exceptions and lose the distinction between
# "the gate is red" and "the tool broke".
$PSNativeCommandUseErrorActionPreference = $false

$ExitGateFailed = 1
$ExitIncomplete = 2

# ----------------------------------------------------------------------- locate
# $PSScriptRoot/.. rather than Get-Location, and then Push-Location onto it: the
# two stage scripts locate their own root the same way, but `dotnet-stryker`
# resolves the paths inside stryker-config*.json against the CURRENT directory.
# Run this from anywhere else without moving and Stryker looks for the solution
# somewhere it is not.
$repoRoot = if ($RepositoryRoot) {
    (Resolve-Path -LiteralPath $RepositoryRoot).Path
} else {
    (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
}

. (Join-Path $repoRoot 'build' 'ci' '_common.ps1')

Push-Location $repoRoot
try {

# ============================================================ small helpers ===

function Get-Prop {
    <#
        Property access that returns a default instead of throwing.

        Needed because this script runs under StrictMode and reads JSON written
        by three tools it does not control: a renamed or absent field must
        degrade to "not reported" in the summary, not kill the run after forty
        minutes of measuring.
    #>
    param($Object, [Parameter(Mandatory)][string]$Name, $Default = $null)

    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    if ($null -eq $property.Value) { return $Default }
    return $property.Value
}

function Format-Duration {
    param([TimeSpan]$Span)

    if ($Span.TotalHours -ge 1) { return '{0}h{1:00}m' -f [int]$Span.TotalHours, $Span.Minutes }
    if ($Span.TotalMinutes -ge 1) { return '{0}m{1:00}s' -f [int]$Span.TotalMinutes, $Span.Seconds }
    return '{0}s' -f [int]$Span.TotalSeconds
}

function ConvertTo-InvariantNumber {
    <#
        Parses a number out of tool output that may have been rendered in the
        machine's own culture.

        What was actually measured, on a German-locale machine against
        ReportGenerator 5.5.11: the Crap Score column renders as a ROUNDED
        INTEGER ("59", not "58,5") and the percentages next to it use '.', so no
        separator ambiguity arises today. This is therefore insurance rather than
        a fix, and it is cheap insurance for an expensive failure: if a future
        version renders "58,5", an invariant-only parse reads it as 585 — an
        order of magnitude, silently, in the one column this whole exercise
        exists to produce. Try invariant, then current, then report nothing at
        all rather than a wrong number.
    #>
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $clean = ($Text -replace '[^\d,.\-]', '')
    if ([string]::IsNullOrWhiteSpace($clean)) { return $null }

    [double]$parsed = 0
    foreach ($culture in @([Globalization.CultureInfo]::InvariantCulture, [Globalization.CultureInfo]::CurrentCulture)) {
        if ([double]::TryParse($clean, [Globalization.NumberStyles]::Any, $culture, [ref]$parsed)) { return $parsed }
    }
    return $null
}

function Convert-RatingToLetter {
    <# SonarQube reports its ratings as "1.0".."5.0". Nobody reads those. #>
    param($Value)

    $number = ConvertTo-InvariantNumber ([string]$Value)
    if ($null -eq $number) { return [string]$Value }
    $letters = @('A', 'B', 'C', 'D', 'E')
    $index = [int]$number - 1
    if ($index -lt 0 -or $index -ge $letters.Count) { return [string]$Value }
    return $letters[$index]
}

function Format-Debt {
    <# sqale_index is technical debt in minutes. Render it as an eight-hour day. #>
    param($Minutes)

    $number = ConvertTo-InvariantNumber ([string]$Minutes)
    if ($null -eq $number) { return $null }
    $days = [Math]::Floor($number / 480)
    $hours = [Math]::Floor(($number % 480) / 60)
    if ($days -gt 0) { return "${days}d ${hours}h" }
    if ($hours -gt 0) { return "${hours}h" }
    return "$([int]$number)min"
}

function New-StageResult {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Title)

    return [pscustomobject]@{
        Name     = $Name
        Title    = $Title
        Status   = 'Skipped'
        Reason   = $null
        ExitCode = $null
        Duration = [TimeSpan]::Zero
        Headline = 'not requested'
        LogPath  = $null
        Reports  = [System.Collections.Generic.List[string]]::new()
        Blockers = [System.Collections.Generic.List[string]]::new()
        Notes    = [System.Collections.Generic.List[string]]::new()
        Metrics  = [ordered]@{}
        Details  = [ordered]@{}
    }
}

function Get-StageLogLabel {
    <#
        A stage's log path as a repo-relative string.

        Built from the log DIRECTORY plus the file name rather than by resolving
        the file, because the mutation stage's label is a placeholder standing for
        one log per mutated project — there is no single file to resolve.
    #>
    param([Parameter(Mandatory)]$Stage)

    if (-not $Stage.LogPath) { return $null }
    return "$(Get-RelativePath -Root $repoRoot -Path $logDirectory)/$(Split-Path -Leaf $Stage.LogPath)"
}

function Invoke-Stage {
    <#
        Runs one child process, streams its output to the console AND to a log,
        and returns nothing but the exit code.

        🔴 CHILD PROCESS, NOT AN IN-PROCESS CALL. Both stage scripts publish
        their result by calling `exit`, and both set their own StrictMode,
        $ErrorActionPreference, $PSNativeCommandUseErrorActionPreference and
        working directory. A separate process is what makes those exit codes
        readable here and their state changes un-leakable — and it is how CI
        invokes them, so a stage run here is the same run.

        Out-Host, not a bare pipeline: Tee-Object PASSES ITS INPUT THROUGH, so
        without it every line of a forty-minute build would become part of this
        function's return value and the exit code would be buried in it.
    #>
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory)][string]$LogPath,
        [hashtable]$Environment
    )

    $restore = @{}
    if ($Environment) {
        foreach ($name in $Environment.Keys) {
            $restore[$name] = [Environment]::GetEnvironmentVariable($name)
            [Environment]::SetEnvironmentVariable($name, $Environment[$name])
        }
    }
    try {
        & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $LogPath | Out-Host
        return $LASTEXITCODE
    } finally {
        foreach ($name in $restore.Keys) {
            [Environment]::SetEnvironmentVariable($name, $restore[$name])
        }
    }
}

# ================================================================== preflight ===

Write-Section 'Verification preflight'

$requested = @{
    Sonar    = $Stages -contains 'Sonar'
    Crap     = $Stages -contains 'Crap'
    Mutation = $Stages -contains 'Mutation'
}

$results = [ordered]@{
    Sonar    = New-StageResult -Name 'Sonar'    -Title 'SonarQube static analysis'
    Crap     = New-StageResult -Name 'Crap'     -Title 'CRAP score'
    Mutation = New-StageResult -Name 'Mutation' -Title 'Mutation testing (Stryker)'
}

$strykerCommand = $null
$strykerConfigs = @()

# --- things every stage needs; a failure here is fatal rather than per-stage ---
$fatal = [System.Collections.Generic.List[string]]::new()

# The pwsh that is running THIS script, not whichever one happens to be on PATH.
$pwshPath = $null
try { $pwshPath = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName } catch { $pwshPath = $null }
if (-not $pwshPath -or -not (Test-Path -LiteralPath $pwshPath)) {
    $onPath = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($onPath) { $pwshPath = $onPath.Source } else { $pwshPath = $null }
}
if (-not $pwshPath) {
    $fatal.Add('Could not determine the PowerShell executable to launch the stage scripts with. Install PowerShell 7+ and make `pwsh` resolvable.')
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $fatal.Add('No `dotnet` on PATH. Every stage needs the .NET SDK.')
}

$solutionFiles = @(Get-ChildItem -Path $repoRoot -Filter '*.sln' -File)
if ($solutionFiles.Count -ne 1) {
    $fatal.Add("Expected exactly one .sln at $repoRoot, found $($solutionFiles.Count). Every stage below assumes the single-solution layout.")
}

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts' 'verification' }
$logDirectory = Join-Path $OutputDirectory 'logs'
$strykerRoot = Join-Path $OutputDirectory 'stryker'
$summaryMarkdown = Join-Path $OutputDirectory 'summary.md'
$summaryJson = Join-Path $OutputDirectory 'summary.json'

if ($fatal.Count -gt 0) {
    foreach ($problem in $fatal) { Write-CiError -Message $problem }
    Write-Host ''
    Write-Host "$($fatal.Count) fatal precondition(s). Nothing was run." -ForegroundColor Red
    exit $ExitIncomplete
}

$sonarScript = Join-Path $repoRoot 'build' 'ci' 'Invoke-SonarAnalysis.ps1'
$crapScript = Join-Path $repoRoot 'scripts' 'Measure-Crap.ps1'
$runSettings = Join-Path $repoRoot 'coverage.runsettings'
$coverageDirectory = Join-Path $repoRoot 'coverage'
$testResultsDirectory = Join-Path $repoRoot 'TestResults'

# ------------------------------------------------------------- Sonar preflight
if ($requested.Sonar) {
    $stage = $results.Sonar

    if (-not (Test-Path -LiteralPath $sonarScript)) {
        $stage.Blockers.Add("$sonarScript is missing.")
    }
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'SonarQube.Analysis.xml'))) {
        # Existence only. Whether the file is SHAPED so the scanner can read it is
        # Invoke-SonarAnalysis.ps1's own preflight, which has a measured story
        # about comments truncating property values; a second copy of that check
        # here would be a second thing to keep in step.
        $stage.Blockers.Add('SonarQube.Analysis.xml is missing. It is the only place this repository''s analysis scope lives.')
    }
    if (-not (Test-Path -LiteralPath $runSettings)) {
        $stage.Blockers.Add("$runSettings is missing; it is what turns coverage collection on at all.")
    }

    if (-not $SonarHostUrl) { $SonarHostUrl = $env:SONAR_HOST_URL }
    # 127.0.0.1 and not localhost, for the reason Invoke-SonarAnalysis.ps1 gives:
    # on Windows the latter can resolve to ::1 first, and the published port is
    # IPv4-only.
    if (-not $SonarHostUrl) { $SonarHostUrl = 'http://127.0.0.1:9000' }
    $SonarHostUrl = $SonarHostUrl.TrimEnd('/')

    if (-not $SonarToken) { $SonarToken = $env:SONAR_TOKEN }
    if (-not $SonarToken) {
        $stage.Blockers.Add("No SonarQube token. Set `$env:SONAR_TOKEN or pass -SonarToken. Generate one at $SonarHostUrl/account/security (infra/sonarqube/README.md).")
    }

    # Asked now rather than after a full Release build, which is the whole
    # argument for a preflight.
    if ($stage.Blockers.Count -eq 0) {
        try {
            $status = Invoke-RestMethod -Method Get -Uri "$SonarHostUrl/api/system/status" -TimeoutSec 15
            if ($status.status -ne 'UP') {
                $stage.Blockers.Add("$SonarHostUrl reports status '$($status.status)', not 'UP'. It is starting, migrating, or degraded.")
            } else {
                Write-Host "sonarqube       : $SonarHostUrl (SonarQube $($status.version), UP)"
            }
        } catch {
            $stage.Blockers.Add("$SonarHostUrl is not answering /api/system/status ($($_.Exception.Message)). Start it with: docker compose -f docker-compose.sonarqube.yml up -d --wait")
        }
    }
}

# -------------------------------------------------------------- CRAP preflight
if ($requested.Crap) {
    $stage = $results.Crap

    if (-not (Test-Path -LiteralPath $crapScript)) {
        $stage.Blockers.Add("$crapScript is missing.")
    }
    if (-not (Test-Path -LiteralPath $runSettings)) {
        $stage.Blockers.Add("$runSettings is missing; without it there is no per-method complexity and therefore no CRAP score.")
    }

    if ($ReuseCoverageForCrap) {
        # -SkipTests reuses whatever is in TestResults/, and Measure-Crap.ps1 is
        # explicit that stale coverage there produces plausible, wrong numbers. So
        # there has to be a run in THIS session that put it there.
        if (-not $requested.Sonar) {
            $stage.Blockers.Add('-ReuseCoverageForCrap needs the Sonar stage to produce the coverage it reuses, and Sonar is not in -Stages. Drop the switch, or add Sonar.')
        } elseif ($results.Sonar.Blockers.Count -gt 0) {
            $stage.Blockers.Add('-ReuseCoverageForCrap was asked for, but the Sonar stage is blocked and will produce no coverage to reuse. Drop the switch.')
        }
    }
}

# ---------------------------------------------------------- Mutation preflight
if ($requested.Mutation) {
    $stage = $results.Mutation

    $strykerOnPath = Get-Command dotnet-stryker -ErrorAction SilentlyContinue
    if ($strykerOnPath) {
        $strykerCommand = $strykerOnPath.Source
    } else {
        $stage.Blockers.Add('No `dotnet-stryker` on PATH. It is a GLOBAL tool here, not one of the two pinned in .config/dotnet-tools.json, so `dotnet tool restore` will not produce it: install it with `dotnet tool install -g dotnet-stryker`.')
    }

    # --- MSBUILD_EXE_PATH -----------------------------------------------------
    # 🔴 MANDATORY, NOT A CONVENIENCE. Without it Stryker's solution analysis ends
    # in "No project found" before a single mutant is created — README.md says so.
    # Resolved rather than hardcoded: `dotnet --info` reports the Base Path of the
    # SDK global.json actually selects in this repository, so this default follows
    # the pin instead of going stale next to it.
    if (-not $MsBuildPath) { $MsBuildPath = $env:MSBUILD_EXE_PATH }
    if (-not $MsBuildPath) {
        try {
            $basePathLine = & dotnet --info 2>&1 | Select-String -Pattern '^\s*Base Path:\s*(.+)$' | Select-Object -First 1
            if ($basePathLine) {
                $candidate = Join-Path ($basePathLine.Matches[0].Groups[1].Value.Trim()) 'MSBuild.dll'
                if (Test-Path -LiteralPath $candidate) { $MsBuildPath = $candidate }
            }
        } catch {
            # Left to the blocker below, which words it better than this catch could.
        }
    }
    if (-not $MsBuildPath) {
        $stage.Blockers.Add('Could not resolve an MSBuild.dll for Stryker. Set $env:MSBUILD_EXE_PATH or pass -MsBuildPath; it is the difference between a mutation run and "No project found".')
    } elseif (-not (Test-Path -LiteralPath $MsBuildPath)) {
        $stage.Blockers.Add("MSBuild path '$MsBuildPath' does not exist. Point -MsBuildPath at the MSBuild.dll of an installed SDK.")
    } else {
        Write-Host "msbuild         : $MsBuildPath"
    }

    # --- the configs ----------------------------------------------------------
    # Discovered rather than hardcoded, so mutating a third project is a new
    # config file and no edit here.
    $configFiles = @(Get-ChildItem -Path $repoRoot -Filter 'stryker-config*.json' -File | Sort-Object -Property Name)
    if ($configFiles.Count -eq 0) {
        $stage.Blockers.Add("No stryker-config*.json at $repoRoot.")
    }

    foreach ($configFile in $configFiles) {
        try {
            $configJson = Get-Content -Raw -LiteralPath $configFile.FullName | ConvertFrom-Json
        } catch {
            $stage.Blockers.Add("$($configFile.Name) is not valid JSON ($($_.Exception.Message)).")
            continue
        }

        $section = Get-Prop $configJson 'stryker-config'
        $project = Get-Prop $section 'project'

        # 🔴 THE TRIPWIRE THIS STAGE EXISTS TO CARRY. Stryker mutates ONE project
        # per run; a config with no `project` key puts it in SOLUTION MODE and it
        # mutates all thirty-six instead — a run that does not fail, just never
        # finishes. README.md warns about it in prose. Here it is a check.
        if (-not $project) {
            $stage.Blockers.Add("$($configFile.Name) declares no 'stryker-config.project'. Stryker would fall into solution mode and mutate every project in the solution instead of one.")
            continue
        }

        $low = Get-Prop (Get-Prop $section 'thresholds') 'low'
        if ($BreakOnMutationScore -gt 0 -and $null -ne $low -and $BreakOnMutationScore -gt [int]$low) {
            # Stryker's own constraint: break must be <= threshold-low, and it
            # refuses the run rather than clamping. Caught here so the refusal is
            # not the thing that ends a forty-minute run.
            $stage.Blockers.Add("-BreakOnMutationScore $BreakOnMutationScore exceeds 'thresholds.low' ($low) in $($configFile.Name). Stryker requires break <= low; lower the switch or raise the threshold in the config.")
        }

        $strykerConfigs += [pscustomobject]@{
            File  = $configFile.FullName
            Name  = $configFile.Name
            # "SlayIdleRepeat.Core.csproj" -> "SlayIdleRepeat.Core". Used for the
            # per-run output directory and for every label in the summary.
            Label = [IO.Path]::GetFileNameWithoutExtension([string]$project)
        }
    }

    $duplicateLabels = @($strykerConfigs | Group-Object -Property Label | Where-Object { $_.Count -gt 1 })
    if ($duplicateLabels.Count -gt 0) {
        $stage.Blockers.Add("Two stryker configs name the same project ($(($duplicateLabels | ForEach-Object { $_.Name }) -join ', ')). One run's report would overwrite the other's.")
    }

    if (-not $FullMutation) {
        & git rev-parse --verify --quiet $MutationSince *> $null
        if ($LASTEXITCODE -ne 0) {
            $stage.Blockers.Add("-MutationSince '$MutationSince' is not a ref this repository can resolve, so Stryker's diff mode has nothing to compare against. Fetch it, or pass -FullMutation.")
        }
    }
}

# --- report the preflight and decide what is still going to run ---------------
foreach ($key in @($results.Keys)) {
    $stage = $results[$key]
    if (-not $requested[$key]) { continue }
    if ($stage.Blockers.Count -eq 0) { continue }

    $stage.Status = 'Blocked'
    $stage.Headline = 'blocked in preflight'
    $stage.Reason = $stage.Blockers[0]
    foreach ($blocker in $stage.Blockers) { Write-CiError -Message "$($stage.Title): $blocker" }
}

$runnable = @($results.Keys | Where-Object { $requested[$_] -and $results[$_].Status -ne 'Blocked' })
if ($runnable.Count -eq 0) {
    Write-Host ''
    Write-Host 'No requested stage can run. Nothing was measured.' -ForegroundColor Red
    exit $ExitIncomplete
}

# --------------------------------------------------------------- the run plan
New-Item -ItemType Directory -Force -Path $OutputDirectory, $logDirectory | Out-Null

# Targeted removal, not a recursive delete of $OutputDirectory: a stale summary
# read as a fresh one is the failure mode worth preventing, and a caller who
# pointed -OutputDirectory somewhere unexpected should not lose its contents.
foreach ($stale in @($summaryMarkdown, $summaryJson)) {
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force }
}
if (Test-Path -LiteralPath $logDirectory) {
    Get-ChildItem -Path $logDirectory -Filter '*.log' -File | Remove-Item -Force
}

Write-Host "repository root : $repoRoot"
Write-Host "solution        : $($solutionFiles[0].Name)"
Write-Host "output          : $(Get-RelativePath -Root $repoRoot -Path $OutputDirectory)"
Write-Host "stages          : $($Stages -join ', ')"
if ($requested.Crap -and $results.Crap.Status -ne 'Blocked') {
    $gateText = 'no gate'
    if ($FailOnCrap -gt 0) { $gateText = "gate at $FailOnCrap" }
    Write-Host "crap thresholds : CRAP >= $CrapThreshold at complexity >= $ComplexityThreshold, $gateText"
    $coverageSource = 'collected by its own run'
    if ($ReuseCoverageForCrap) { $coverageSource = 'REUSED from the Sonar stage (-ReuseCoverageForCrap)' }
    Write-Host "crap coverage   : $coverageSource"
}
if ($requested.Mutation -and $results.Mutation.Status -ne 'Blocked') {
    $mutationMode = "diff against '$MutationSince'"
    if ($FullMutation) { $mutationMode = 'FULL (every mutant)' }
    Write-Host "mutation mode   : $mutationMode"
    Write-Host "mutated projects: $((@($strykerConfigs | ForEach-Object { $_.Label })) -join ', ')"
}
Write-Host ''
Write-Host 'Rough cost, from this repository''s own measurements: Sonar ~15 min (a Release build of the' -ForegroundColor DarkGray
Write-Host 'solution plus six suites under coverage), CRAP ~12 min (or seconds with' -ForegroundColor DarkGray
Write-Host '-ReuseCoverageForCrap), mutation tens of minutes in diff mode and hours with' -ForegroundColor DarkGray
Write-Host '-FullMutation. The stages run strictly one after another: they share build output on' -ForegroundColor DarkGray
Write-Host 'disk and would corrupt one another in parallel.' -ForegroundColor DarkGray

$runStarted = Get-Date
$stageNumber = 0
$stageCount = $runnable.Count

# ============================================================ stage: SonarQube ===

if ($requested.Sonar -and $results.Sonar.Status -ne 'Blocked') {
    $stage = $results.Sonar
    $stage.LogPath = Join-Path $logDirectory 'sonar.log'
    $stageNumber++
    Write-Section "Stage $stageNumber/$stageCount - SonarQube static analysis"

    $sonarArguments = @(
        '-NoProfile', '-File', $sonarScript
        '-ProjectKey', $SonarProjectKey
        '-HostUrl', $SonarHostUrl
        '-Token', $SonarToken
        '-RepositoryRoot', $repoRoot
    )
    if (-not $NoQualityGate) {
        $sonarArguments += @('-WaitForQualityGate', '-QualityGateTimeout', "$QualityGateTimeout")
    }

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $exitCode = Invoke-Stage -FilePath $pwshPath -Arguments $sonarArguments -LogPath $stage.LogPath
    $stopwatch.Stop()

    $stage.Duration = $stopwatch.Elapsed
    $stage.ExitCode = $exitCode
    # The exit contract Invoke-SonarAnalysis.ps1 documents: 1 is a red gate — the
    # tool working — and 2 is the plumbing. Keeping them apart is what lets the
    # summary say "measured, and bad" rather than "unknown".
    switch ($exitCode) {
        0 { $stage.Status = 'Passed' }
        1 {
            $stage.Status = 'GateFailed'
            $stage.Reason = 'SonarQube quality gate is red.'
        }
        default {
            $stage.Status = 'Failed'
            $stage.Reason = "Invoke-SonarAnalysis.ps1 exited $exitCode. Nothing was uploaded, or the server rejected the upload."
        }
    }
    $stage.Reports.Add("$SonarHostUrl/dashboard?id=$SonarProjectKey")

    # ------------------------------------------------- read the numbers back
    # Only if something was actually uploaded. Reading measures after a failed
    # upload would report the PREVIOUS analysis' numbers as this run's, which is
    # exactly the class of quiet wrongness the two stage scripts spend their
    # length preventing.
    if ($stage.Status -eq 'Passed' -or $stage.Status -eq 'GateFailed') {
        $headers = @{ Authorization = "Bearer $SonarToken" }

        # 🔴 WAIT FOR THE COMPUTE ENGINE FIRST. `sonarscanner end` returns when the
        # report has been UPLOADED, not when the server has PROCESSED it. Query
        # measures before that finishes and the answer is the previous analysis,
        # with nothing anywhere saying so. -WaitForQualityGate already implies the
        # wait; this covers -NoQualityGate and costs one call when it is already
        # done.
        $deadline = (Get-Date).AddSeconds($QualityGateTimeout)
        $engineSettled = $false
        while ((Get-Date) -lt $deadline) {
            $activity = $null
            try {
                $activity = Invoke-RestMethod -Method Get -Headers $headers -TimeoutSec 30 `
                    -Uri "$SonarHostUrl/api/ce/component?component=$([uri]::EscapeDataString($SonarProjectKey))"
            } catch {
                $stage.Notes.Add("Could not poll the compute-engine queue ($($_.Exception.Message)); the measures below may lag this analysis.")
                break
            }
            $queue = @(Get-Prop $activity 'queue' @())
            $current = Get-Prop $activity 'current'
            $currentStatus = Get-Prop $current 'status'
            if ($queue.Count -eq 0 -and $null -ne $currentStatus -and @('SUCCESS', 'FAILED', 'CANCELED') -contains $currentStatus) {
                $engineSettled = $true
                if ($currentStatus -ne 'SUCCESS') {
                    $stage.Notes.Add("The server's last background task for this project ended '$currentStatus'. The measures below are whatever survived it.")
                }
                break
            }
            Start-Sleep -Seconds 3
        }
        if (-not $engineSettled) {
            $stage.Notes.Add("The server was still processing after ${QualityGateTimeout}s; the measures below may belong to the previous analysis.")
        }

        # --- the gate -------------------------------------------------------
        try {
            $gate = Invoke-RestMethod -Method Get -Headers $headers -TimeoutSec 30 `
                -Uri "$SonarHostUrl/api/qualitygates/project_status?projectKey=$([uri]::EscapeDataString($SonarProjectKey))"
            $projectStatus = Get-Prop $gate 'projectStatus'
            $gateStatus = Get-Prop $projectStatus 'status' 'UNKNOWN'
            $stage.Metrics['quality_gate'] = $gateStatus

            $failing = @(
                @(Get-Prop $projectStatus 'conditions' @()) |
                    Where-Object { (Get-Prop $_ 'status') -eq 'ERROR' } |
                    ForEach-Object {
                        "$(Get-Prop $_ 'metricKey') = $(Get-Prop $_ 'actualValue') (fails $(Get-Prop $_ 'comparator') $(Get-Prop $_ 'errorThreshold'))"
                    }
            )
            if ($failing.Count -gt 0) { $stage.Details['Failing gate conditions'] = $failing }

            # The gate can be red while the scanner exited 0 — that is precisely
            # what -NoQualityGate means. Trust the server over the exit code.
            if ($gateStatus -eq 'ERROR' -and $stage.Status -eq 'Passed') {
                $stage.Status = 'GateFailed'
                $stage.Reason = 'SonarQube quality gate is red (read back from the server; the run itself did not wait for it).'
            }
        } catch {
            $stage.Notes.Add("Could not read the quality gate status ($($_.Exception.Message)).")
        }

        # --- the measures ---------------------------------------------------
        # 🔴 Intersected with what the server actually publishes rather than
        # requested blind: /api/measures/component rejects the WHOLE request with a
        # 400 if a single metric key is unknown, and SonarQube renames and retires
        # metric keys between versions. One extra call buys a summary an upgrade
        # cannot empty.
        $wanted = @(
            'ncloc', 'coverage', 'line_coverage', 'branch_coverage',
            'duplicated_lines_density', 'violations', 'blocker_violations',
            'critical_violations', 'bugs', 'vulnerabilities', 'code_smells',
            'security_hotspots', 'sqale_index', 'sqale_debt_ratio',
            'sqale_rating', 'reliability_rating', 'security_rating',
            'complexity', 'cognitive_complexity'
        )
        try {
            $published = @((Invoke-RestMethod -Method Get -Headers $headers -TimeoutSec 60 `
                        -Uri "$SonarHostUrl/api/metrics/search?ps=500").metrics.key)
            $available = @($wanted | Where-Object { $published -contains $_ })
            $unavailable = @($wanted | Where-Object { $published -notcontains $_ })
            if ($unavailable.Count -gt 0) {
                $stage.Notes.Add("This SonarQube does not publish $($unavailable -join ', '); those rows are absent rather than zero.")
            }

            if ($available.Count -gt 0) {
                $measures = Invoke-RestMethod -Method Get -Headers $headers -TimeoutSec 60 `
                    -Uri "$SonarHostUrl/api/measures/component?component=$([uri]::EscapeDataString($SonarProjectKey))&metricKeys=$($available -join ',')"
                foreach ($measure in @(Get-Prop (Get-Prop $measures 'component') 'measures' @())) {
                    $key = Get-Prop $measure 'metric'
                    $value = Get-Prop $measure 'value'
                    if ($null -eq $key -or $null -eq $value) { continue }
                    if ($key -like '*_rating') {
                        $stage.Metrics[$key] = Convert-RatingToLetter $value
                    } else {
                        $stage.Metrics[$key] = $value
                    }
                }
                if ($stage.Metrics.Contains('sqale_index')) {
                    $stage.Metrics['technical_debt'] = Format-Debt $stage.Metrics['sqale_index']
                }
            }
        } catch {
            $stage.Notes.Add("Could not read the measures back ($($_.Exception.Message)). The dashboard has them.")
        }

        $headlineParts = [System.Collections.Generic.List[string]]::new()
        if ($stage.Metrics.Contains('quality_gate')) {
            $headlineParts.Add("gate $($stage.Metrics['quality_gate'])")
        } else {
            $headlineParts.Add('gate unknown')
        }
        if ($stage.Metrics.Contains('coverage')) { $headlineParts.Add("$($stage.Metrics['coverage'])% coverage") }
        if ($stage.Metrics.Contains('violations')) { $headlineParts.Add("$($stage.Metrics['violations']) issues") }
        if ($stage.Metrics.Contains('duplicated_lines_density')) { $headlineParts.Add("$($stage.Metrics['duplicated_lines_density'])% duplicated") }
        $stage.Headline = $headlineParts -join ' | '
    } else {
        $stage.Headline = 'analysis did not complete'
    }
}

# ================================================================= stage: CRAP ===

if ($requested.Crap -and $results.Crap.Status -ne 'Blocked') {
    $stage = $results.Crap
    $stage.LogPath = Join-Path $logDirectory 'crap.log'
    $stageNumber++
    Write-Section "Stage $stageNumber/$stageCount - CRAP score"

    # Guard the reuse rather than trusting the switch: if the Sonar stage did not
    # actually leave Cobertura behind, -SkipTests would rank whatever happens to be
    # in TestResults/ — including a run from days ago.
    $skipTests = $false
    if ($ReuseCoverageForCrap) {
        $reusable = @(Get-ChildItem -Path $testResultsDirectory -Filter 'coverage.cobertura.xml' -Recurse -File -ErrorAction SilentlyContinue)
        $sonarProduced = ($results.Sonar.Status -eq 'Passed' -or $results.Sonar.Status -eq 'GateFailed')
        if ($sonarProduced -and $reusable.Count -gt 0) {
            $skipTests = $true
            $stage.Notes.Add("Coverage REUSED from the Sonar stage's Release run ($($reusable.Count) Cobertura file(s)); the suites were not run again. That assumes a Release build yields the same per-method complexity as a Debug one, which this repository has not measured — treat the ranking as indicative.")
        } else {
            $stage.Notes.Add('-ReuseCoverageForCrap was asked for, but the Sonar stage left no usable Cobertura behind, so the suites ran normally. These are the stronger numbers.')
        }
    }

    $crapArguments = @(
        '-NoProfile', '-File', $crapScript
        '-CrapThreshold', "$CrapThreshold"
        '-ComplexityThreshold', "$ComplexityThreshold"
    )
    if ($FailOnCrap -gt 0) { $crapArguments += @('-FailOnCrap', "$FailOnCrap") }
    if ($skipTests) { $crapArguments += '-SkipTests' }

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $exitCode = Invoke-Stage -FilePath $pwshPath -Arguments $crapArguments -LogPath $stage.LogPath
    $stopwatch.Stop()

    $stage.Duration = $stopwatch.Elapsed
    $stage.ExitCode = $exitCode
    switch ($exitCode) {
        0 { $stage.Status = 'Passed' }
        1 {
            $stage.Status = 'GateFailed'
            $stage.Reason = "At least one method scored above the -FailOnCrap maximum of $FailOnCrap."
        }
        default {
            $stage.Status = 'Failed'
            $stage.Reason = "Measure-Crap.ps1 exited $exitCode; it produced no trustworthy report."
        }
    }

    # -------------------------------------------------- read the numbers back
    # Summary.json rather than Summary.txt: it carries the same figures as
    # culture-invariant JSON numbers, where the text report renders percentages in
    # the machine's own locale.
    $summaryPath = Join-Path $coverageDirectory 'Summary.json'
    if (Test-Path -LiteralPath $summaryPath) {
        try {
            $coverageSummary = Get-Prop (Get-Content -Raw -LiteralPath $summaryPath | ConvertFrom-Json) 'summary'
            foreach ($key in @('linecoverage', 'branchcoverage', 'methodcoverage', 'coveredlines', 'coverablelines', 'totalmethods', 'assemblies', 'classes')) {
                $value = Get-Prop $coverageSummary $key
                if ($null -ne $value) { $stage.Metrics[$key] = $value }
            }
        } catch {
            $stage.Notes.Add("coverage/Summary.json could not be parsed ($($_.Exception.Message)).")
        }
    } else {
        $stage.Notes.Add('coverage/Summary.json was not produced, so no coverage figures could be read back. Its absence means ReportGenerator did not run.')
    }

    # The Risk Hotspots ranking.
    #
    # 🔴 ReportGenerator publishes it in the RENDERED HTML ONLY. Its JsonSummary
    # report carries the coverage summary and no hotspots whatsoever — measured
    # against 5.5.11 — so the table inside <risk-hotspots> is the sole
    # machine-readable source there is. Measure-Crap.ps1 already reads the same
    # element to assert the Crap Score column exists; this reads the rows out of
    # it.
    #
    # ⚠️ That table is capped at twenty rows by ReportGenerator and the page does
    # not disclose it. A count taken from it is never "the total", and the note
    # below says so whenever the cap is in play.
    $indexPath = Join-Path $coverageDirectory 'index.html'
    if (Test-Path -LiteralPath $indexPath) {
        $indexHtml = Get-Content -Raw -LiteralPath $indexPath
        $hotspotSection = [regex]::Match($indexHtml, '(?s)<risk-hotspots>(.*?)</risk-hotspots>')
        if (-not $hotspotSection.Success -or $hotspotSection.Groups[1].Value -notmatch '<td') {
            $stage.Metrics['hotspots_listed'] = 0
            $stage.Notes.Add("No Risk Hotspots table in the report: nothing reached CRAP $CrapThreshold at complexity $ComplexityThreshold. That is a pass, not a fault.")
        } else {
            $hotspots = foreach ($row in [regex]::Matches($hotspotSection.Groups[1].Value, '(?s)<tr>(.*?)</tr>')) {
                $cells = @(
                    [regex]::Matches($row.Groups[1].Value, '(?s)<td[^>]*>(.*?)</td>') |
                        ForEach-Object { ([regex]::Replace($_.Groups[1].Value, '<[^>]+>', '')).Trim() }
                )
                # The header row is <th>, so it yields no cells and drops out here.
                if ($cells.Count -lt 5) { continue }
                [pscustomobject]@{
                    Class      = $cells[1]
                    Method     = $cells[2]
                    Crap       = $cells[3]
                    Complexity = $cells[4]
                    CrapValue  = (ConvertTo-InvariantNumber $cells[3])
                }
            }
            $hotspots = @($hotspots | Sort-Object -Property CrapValue -Descending)
            $stage.Metrics['hotspots_listed'] = $hotspots.Count
            $stage.Details['Riskiest methods by CRAP'] = @(
                $hotspots | Select-Object -First $TopHotspots | ForEach-Object {
                    "CRAP $($_.Crap) | complexity $($_.Complexity) | $($_.Class).$($_.Method)"
                }
            )
            if ($hotspots.Count -ge 20) {
                $stage.Notes.Add('ReportGenerator caps its Risk Hotspots table at 20 rows and does not say so on the page, so "20" here means "at least 20". Raise the thresholds to see the next tier down.')
            }
        }
    }

    if ($stage.Status -ne 'Failed') {
        $coveragePart = 'coverage unread'
        if ($stage.Metrics.Contains('linecoverage')) { $coveragePart = "$($stage.Metrics['linecoverage'])% line coverage" }
        $hotspotPart = 'hotspots unread'
        if ($stage.Metrics.Contains('hotspots_listed')) { $hotspotPart = "$($stage.Metrics['hotspots_listed']) hotspot(s) >= $CrapThreshold/$ComplexityThreshold" }
        $stage.Headline = "$coveragePart | $hotspotPart"
        if (Test-Path -LiteralPath $indexPath) {
            $stage.Reports.Add((Get-RelativePath -Root $repoRoot -Path $indexPath))
        }
    } else {
        $stage.Headline = 'no report produced'
    }
}

# ============================================================= stage: Mutation ===

if ($requested.Mutation -and $results.Mutation.Status -ne 'Blocked') {
    $stage = $results.Mutation
    $stageNumber++
    Write-Section "Stage $stageNumber/$stageCount - Mutation testing (Stryker)"

    if (-not $FullMutation) {
        Write-Host "Diff mode against '$MutationSince'. Everything outside the diff is reported Ignored, so the" -ForegroundColor DarkGray
        Write-Host 'score below is about the CHANGED files, not about the project. --since shortens the' -ForegroundColor DarkGray
        Write-Host 'mutant-testing phase only: solution analysis, the build, the initial test run and the' -ForegroundColor DarkGray
        Write-Host 'coverage capture all still happen in full.' -ForegroundColor DarkGray
    }

    $perProject = [System.Collections.Generic.List[object]]::new()
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()

    foreach ($config in $strykerConfigs) {
        Write-Host ''
        Write-Host "--- $($config.Label) ($($config.Name))" -ForegroundColor Cyan

        # One output directory per project, named after the project. Stryker's own
        # default is StrykerOutput/<timestamp>/, which would leave this summary
        # guessing which of several timestamps belonged to this run; -O writes
        # reports/ straight into the directory given. Measured against 4.16.0.
        $projectOutput = Join-Path $strykerRoot $config.Label
        if (Test-Path -LiteralPath $projectOutput) { Remove-Item -LiteralPath $projectOutput -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $projectOutput | Out-Null

        $logPath = Join-Path $logDirectory "mutation-$($config.Label).log"
        $strykerArguments = @(
            '-f', $config.File
            '-O', $projectOutput
            # Json alongside the config's own html+progress. The HTML report is for
            # a human walking the survivors; the JSON is the only thing this
            # summary can count without scraping a rendered page.
            '-r', 'Json', '-r', 'Html', '-r', 'Progress'
            '--skip-version-check'
        )
        if (-not $FullMutation) { $strykerArguments += "--since:$MutationSince" }
        if ($BreakOnMutationScore -gt 0) { $strykerArguments += @('-b', "$BreakOnMutationScore") }

        $projectStopwatch = [Diagnostics.Stopwatch]::StartNew()
        $exitCode = Invoke-Stage -FilePath $strykerCommand -Arguments $strykerArguments -LogPath $logPath `
            -Environment @{ MSBUILD_EXE_PATH = $MsBuildPath }
        $projectStopwatch.Stop()

        # --------------------------------------------- read the report back
        # mutation-testing-elements schema: files -> <path> -> mutants[] -> status.
        #
        # Verified against Stryker 4.16.0 on a throwaway project rather than taken
        # from the schema docs: Killed 6, Survived 2, NoCoverage 1, Ignored 1 read
        # back out of this file, and (Killed+Timeout)/(Killed+Timeout+Survived+
        # NoCoverage) = 66.67%, which is the score Stryker itself printed. The
        # formula below is that one — Ignored is outside the denominator,
        # NoCoverage is inside it.
        $reportFile = @(
            Get-ChildItem -Path $projectOutput -Filter 'mutation-report.json' -Recurse -File -ErrorAction SilentlyContinue |
                Sort-Object -Property LastWriteTime -Descending
        ) | Select-Object -First 1

        $counts = [ordered]@{}
        $score = $null
        $coveredScore = $null
        $survivorsByFile = @()
        $htmlReport = $null

        if ($reportFile) {
            try {
                $report = Get-Content -Raw -LiteralPath $reportFile.FullName | ConvertFrom-Json
                $files = Get-Prop $report 'files'
                $rows = @()
                if ($null -ne $files) {
                    $rows = @(
                        foreach ($file in @($files.PSObject.Properties)) {
                            foreach ($mutant in @(Get-Prop $file.Value 'mutants' @())) {
                                [pscustomobject]@{
                                    File   = $file.Name
                                    Status = [string](Get-Prop $mutant 'status' 'Unknown')
                                }
                            }
                        }
                    )
                }

                foreach ($group in @($rows | Group-Object -Property Status | Sort-Object -Property Name)) {
                    $counts[$group.Name] = $group.Count
                }
                $countOf = {
                    param([string]$Name)
                    if ($counts.Contains($Name)) { return [int]$counts[$Name] }
                    return 0
                }
                $killed = & $countOf 'Killed'
                $timeout = & $countOf 'Timeout'
                $survived = & $countOf 'Survived'
                $noCoverage = & $countOf 'NoCoverage'

                $denominator = $killed + $timeout + $survived + $noCoverage
                if ($denominator -gt 0) {
                    $score = [Math]::Round((($killed + $timeout) / $denominator) * 100, 2)
                }
                # Stryker's second figure: the same ratio over covered code only.
                # A wide gap between the two is a coverage problem, not an
                # assertion problem, and they need different fixes.
                $coveredDenominator = $killed + $timeout + $survived
                if ($coveredDenominator -gt 0) {
                    $coveredScore = [Math]::Round((($killed + $timeout) / $coveredDenominator) * 100, 2)
                }

                # Where the work is. A file with survivors is a file whose tests
                # execute code without asserting on what it did.
                $survivorsByFile = @(
                    $rows | Where-Object { $_.Status -eq 'Survived' -or $_.Status -eq 'NoCoverage' } |
                        Group-Object -Property File |
                        Sort-Object -Property Count -Descending |
                        Select-Object -First $TopHotspots |
                        ForEach-Object {
                            $relative = $_.Name
                            try { $relative = Get-RelativePath -Root $repoRoot -Path $_.Name } catch { }
                            "$($_.Count) unkilled | $relative"
                        }
                )

                $htmlCandidate = Join-Path (Split-Path -Parent $reportFile.FullName) 'mutation-report.html'
                if (Test-Path -LiteralPath $htmlCandidate) {
                    $htmlReport = Get-RelativePath -Root $repoRoot -Path $htmlCandidate
                }
            } catch {
                $stage.Notes.Add("$($config.Label): mutation-report.json exists but could not be parsed ($($_.Exception.Message)). The HTML report is intact.")
            }
        }

        # 🔴 Stryker exits 1 BOTH for "the score is below --break-at" and for "the
        # run never happened" — the latter measured on a probe with a bad solution
        # path. Same ambiguity Invoke-SonarAnalysis.ps1 resolves for its own `end`
        # step, and the same resolution: ask the artifact, not the exit code. A
        # parsed score means it ran.
        $projectStatus = 'Passed'
        if ($exitCode -ne 0) {
            if ($null -ne $score) { $projectStatus = 'GateFailed' } else { $projectStatus = 'Failed' }
        }

        $perProject.Add([pscustomobject]@{
            Label        = $config.Label
            Status       = $projectStatus
            ExitCode     = $exitCode
            Duration     = $projectStopwatch.Elapsed
            Score        = $score
            CoveredScore = $coveredScore
            Counts       = $counts
            Survivors    = $survivorsByFile
            ReportPath   = $htmlReport
        })
    }

    $stopwatch.Stop()
    $stage.Duration = $stopwatch.Elapsed
    $stage.LogPath = Join-Path $logDirectory 'mutation-<project>.log'
    $stage.Metrics['mode'] = if ($FullMutation) { 'full' } else { "diff against $MutationSince" }

    foreach ($project in $perProject) {
        $scoreText = 'no score'
        if ($null -ne $project.Score) { $scoreText = "$($project.Score)%" }
        $coveredText = ''
        if ($null -ne $project.CoveredScore) { $coveredText = " (covered code $($project.CoveredScore)%)" }
        $countText = 'no mutants reported'
        if ($project.Counts.Count -gt 0) {
            $countText = (@($project.Counts.Keys | ForEach-Object { "$_ $($project.Counts[$_])" }) -join ', ')
        }

        $stage.Metrics["$($project.Label)_score"] = $project.Score
        $stage.Details["$($project.Label) — $scoreText$coveredText, in $(Format-Duration $project.Duration)"] =
            @($countText) + @($project.Survivors)
        if ($project.ReportPath) { $stage.Reports.Add($project.ReportPath) }
    }

    # The stage is as bad as its worst project: a green Core does not make a
    # broken Application run acceptable.
    $projectStatuses = @($perProject | ForEach-Object { $_.Status })
    if ($projectStatuses -contains 'Failed') {
        $stage.Status = 'Failed'
        $stage.Reason = "Stryker produced no report for $((@($perProject | Where-Object { $_.Status -eq 'Failed' } | ForEach-Object { $_.Label })) -join ', '). See the logs."
    } elseif ($projectStatuses -contains 'GateFailed') {
        $stage.Status = 'GateFailed'
        $stage.Reason = "Mutation score below the -BreakOnMutationScore floor of $BreakOnMutationScore."
    } else {
        $stage.Status = 'Passed'
    }

    $stage.ExitCode = @($perProject | ForEach-Object { $_.ExitCode } | Sort-Object -Descending | Select-Object -First 1)
    $stage.Headline = (@($perProject | ForEach-Object {
        $text = 'n/a'
        if ($null -ne $_.Score) { $text = "$($_.Score)%" }
        "$($_.Label) $text"
    }) -join ' | ')
    if (-not $FullMutation) { $stage.Headline += ' (diff only)' }
}

# ================================================================= aggregation ===

$runDuration = (Get-Date) - $runStarted

$statusLabels = @{
    Passed     = 'PASSED'
    GateFailed = 'GATE FAILED'
    Failed     = 'FAILED'
    Blocked    = 'BLOCKED'
    Skipped    = 'SKIPPED'
}
$statusColours = @{
    Passed     = 'Green'
    GateFailed = 'Red'
    Failed     = 'Red'
    Blocked    = 'Yellow'
    Skipped    = 'DarkGray'
}

$ordered = @($results.Keys | ForEach-Object { $results[$_] })

Write-Section 'Verification summary'

$titleWidth = (@($ordered | ForEach-Object { $_.Title.Length }) | Measure-Object -Maximum).Maximum
$statusWidth = (@($statusLabels.Values | ForEach-Object { $_.Length }) | Measure-Object -Maximum).Maximum

$headlineWidth = (@(@('Headline') + @($ordered | ForEach-Object { $_.Headline })) | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum

Write-Host ('{0}  {1}  {2}  {3}' -f 'Stage'.PadRight($titleWidth), 'Status'.PadRight($statusWidth), 'Duration'.PadRight(8), 'Headline')
Write-Host ('-' * ($titleWidth + $statusWidth + $headlineWidth + 14))
foreach ($stage in $ordered) {
    Write-Host ('{0}  ' -f $stage.Title.PadRight($titleWidth)) -NoNewline
    Write-Host ('{0}  ' -f $statusLabels[$stage.Status].PadRight($statusWidth)) -NoNewline -ForegroundColor $statusColours[$stage.Status]
    Write-Host ('{0}  {1}' -f (Format-Duration $stage.Duration).PadRight(8), $stage.Headline)
}

foreach ($stage in $ordered) {
    if ($stage.Status -eq 'Skipped') { continue }
    Write-Host ''
    Write-Host "$($stage.Title) — $($statusLabels[$stage.Status])" -ForegroundColor $statusColours[$stage.Status]
    if ($stage.Reason) { Write-Host "  why      : $($stage.Reason)" }
    foreach ($blocker in $stage.Blockers) { Write-Host "  blocked  : $blocker" -ForegroundColor Yellow }
    if ($stage.Metrics.Count -gt 0) {
        Write-Host "  measured : $((@($stage.Metrics.Keys | ForEach-Object { "$_=$($stage.Metrics[$_])" })) -join '  ')"
    }
    foreach ($heading in $stage.Details.Keys) {
        Write-Host "  $heading"
        foreach ($line in @($stage.Details[$heading])) { Write-Host "    - $line" }
    }
    foreach ($note in $stage.Notes) { Write-Host "  note     : $note" -ForegroundColor DarkGray }
    foreach ($report in $stage.Reports) { Write-Host "  report   : $report" }
    if ($stage.LogPath) { Write-Host "  log      : $(Get-StageLogLabel $stage)" }
}

# ------------------------------------------------------------- the exit verdict
$incomplete = @($ordered | Where-Object { $_.Status -eq 'Blocked' -or $_.Status -eq 'Failed' })
$gateFailed = @($ordered | Where-Object { $_.Status -eq 'GateFailed' })
$skipped = @($ordered | Where-Object { $_.Status -eq 'Skipped' })

if ($incomplete.Count -gt 0) {
    $verdict = "INCOMPLETE — $((@($incomplete | ForEach-Object { $_.Title })) -join ', ') produced no result"
} elseif ($gateFailed.Count -gt 0) {
    $verdict = "GATE FAILED — $((@($gateFailed | ForEach-Object { $_.Title })) -join ', ')"
} elseif ($skipped.Count -gt 0) {
    $verdict = "PASSED, PARTIAL — $($ordered.Count - $skipped.Count) of $($ordered.Count) stages were requested"
} else {
    $verdict = 'PASSED — all three stages ran and none tripped a gate'
}

$branch = & git rev-parse --abbrev-ref HEAD 2>$null
$commit = & git rev-parse --short HEAD 2>$null

# -------------------------------------------------------------- write the files
# Markdown for a human who wants to keep or paste the result, JSON for anything
# that wants to compare two runs. Both are regenerated in full every run — there
# is no merging, so neither can go half-stale.
$markdown = [System.Collections.Generic.List[string]]::new()
$markdown.Add('# Verification summary')
$markdown.Add('')
$markdown.Add("**$verdict**")
$markdown.Add('')
$markdown.Add('| | |')
$markdown.Add('|:---|:---|')
$markdown.Add("| Generated | $($runStarted.ToString('yyyy-MM-dd HH:mm:ss')) |")
$markdown.Add("| Duration | $(Format-Duration $runDuration) |")
$markdown.Add("| Branch | $branch |")
$markdown.Add("| Commit | $commit |")
$markdown.Add("| Stages requested | $($Stages -join ', ') |")
$markdown.Add('')
$markdown.Add('| Stage | Status | Duration | Headline |')
$markdown.Add('|:---|:---|---:|:---|')
foreach ($stage in $ordered) {
    $markdown.Add("| $($stage.Title) | $($statusLabels[$stage.Status]) | $(Format-Duration $stage.Duration) | $($stage.Headline) |")
}

foreach ($stage in $ordered) {
    if ($stage.Status -eq 'Skipped') { continue }
    $markdown.Add('')
    $markdown.Add("## $($stage.Title) — $($statusLabels[$stage.Status])")
    if ($stage.Reason) {
        $markdown.Add('')
        $markdown.Add($stage.Reason)
    }
    if ($stage.Blockers.Count -gt 0) {
        $markdown.Add('')
        foreach ($blocker in $stage.Blockers) { $markdown.Add("- **Blocked:** $blocker") }
    }
    if ($stage.Metrics.Count -gt 0) {
        $markdown.Add('')
        $markdown.Add('| Metric | Value |')
        $markdown.Add('|:---|---:|')
        foreach ($key in $stage.Metrics.Keys) { $markdown.Add("| $key | $($stage.Metrics[$key]) |") }
    }
    foreach ($heading in $stage.Details.Keys) {
        $markdown.Add('')
        $markdown.Add("### $heading")
        foreach ($line in @($stage.Details[$heading])) { $markdown.Add("- $line") }
    }
    if ($stage.Notes.Count -gt 0) {
        $markdown.Add('')
        foreach ($note in $stage.Notes) { $markdown.Add("> $note") }
    }
    if ($stage.Reports.Count -gt 0) {
        $markdown.Add('')
        foreach ($report in $stage.Reports) { $markdown.Add("- Report: $report") }
    }
}

$markdown.Add('')
$markdown.Add('## What this run does not cover')
$markdown.Add('')
$markdown.Add('- `SlayIdleRepeat.Architecture.Tests` runs in neither coverage stage, on purpose: its rules read the IL that coverlet rewrites, and instrumenting it produces violations that are not in the source. It runs uninstrumented in its own CI job.')
$markdown.Add('- Coverage and mutation cover `SlayIdleRepeat.Core` and `SlayIdleRepeat.Application` only. That allow-list lives in `coverage.runsettings` and in the `stryker-config*.json` files.')
$markdown.Add('- SonarQube Community Build has no branch or pull-request analysis. Whatever branch this ran on, the result landed on the project''s single `main`, so read it as "what the project would look like if this branch were main".')

Set-Content -LiteralPath $summaryMarkdown -Value ($markdown -join [Environment]::NewLine) -Encoding utf8

$json = [ordered]@{
    generated = $runStarted.ToString('o')
    seconds   = [int]$runDuration.TotalSeconds
    verdict   = $verdict
    branch    = $branch
    commit    = $commit
    requested = $Stages
    stages    = [ordered]@{}
}
foreach ($stage in $ordered) {
    $json.stages[$stage.Name] = [ordered]@{
        title    = $stage.Title
        status   = $stage.Status
        reason   = $stage.Reason
        exitCode = $stage.ExitCode
        seconds  = [int]$stage.Duration.TotalSeconds
        headline = $stage.Headline
        metrics  = $stage.Metrics
        details  = $stage.Details
        notes    = @($stage.Notes)
        blockers = @($stage.Blockers)
        reports  = @($stage.Reports)
        log      = Get-StageLogLabel $stage
    }
}
Set-Content -LiteralPath $summaryJson -Value ($json | ConvertTo-Json -Depth 8) -Encoding utf8

Write-Section 'Verdict'
$verdictColour = 'Green'
if ($incomplete.Count -gt 0) { $verdictColour = 'Yellow' }
elseif ($gateFailed.Count -gt 0) { $verdictColour = 'Red' }
Write-Host $verdict -ForegroundColor $verdictColour
Write-Host ''
Write-Host "total           : $(Format-Duration $runDuration)"
Write-Host "summary         : $(Get-RelativePath -Root $repoRoot -Path $summaryMarkdown)"
Write-Host "machine-readable: $(Get-RelativePath -Root $repoRoot -Path $summaryJson)"
Write-Host "logs            : $(Get-RelativePath -Root $repoRoot -Path $logDirectory)"

if ($Open) {
    # No bare Start-Process on a path — that is Windows-only shell-verb behaviour.
    # Each branch names the opener its platform has, the way Measure-Crap.ps1 does.
    $toOpen = [System.Collections.Generic.List[string]]::new()
    if ($results.Crap.Status -eq 'Passed' -or $results.Crap.Status -eq 'GateFailed') {
        $toOpen.Add((Join-Path $coverageDirectory 'index.html'))
    }
    foreach ($report in $results.Mutation.Reports) { $toOpen.Add((Join-Path $repoRoot $report)) }
    if ($results.Sonar.Status -eq 'Passed' -or $results.Sonar.Status -eq 'GateFailed') {
        $toOpen.Add("$SonarHostUrl/dashboard?id=$SonarProjectKey")
    }

    foreach ($target in $toOpen) {
        if ($target -notmatch '^https?://' -and -not (Test-Path -LiteralPath $target)) { continue }
        if ($IsWindows) { Start-Process -FilePath $target }
        elseif ($IsMacOS) { & open $target }
        else { & xdg-open $target }
    }
}

if ($incomplete.Count -gt 0) { exit $ExitIncomplete }
if ($gateFailed.Count -gt 0) { exit $ExitGateFailed }
exit 0

} finally {
    Pop-Location
}
