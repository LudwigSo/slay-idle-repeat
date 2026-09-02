#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Applies this repository's proposed SonarQube server-side configuration:
    the project, the quality gate and its conditions, and the new-code period.

.DESCRIPTION
    SonarQube keeps a project's quality gate and new-code definition in ITS OWN
    database, not in the repository. That is the one part of this setup a git
    checkout cannot reproduce — so it is written down here as a script rather
    than as a numbered list in a README that drifts the first time somebody
    clicks something in the UI.

    Idempotent: run it against a fresh server or an existing one and the result
    is the same. It reports every change it makes and every one it did not need
    to make.

    ⚠️ WHAT THIS DOES NOT DO. It does not arm anything. Creating the gate and
    attaching it to the project makes the verdict VISIBLE; what makes a red gate
    stop a change is passing -WaitForQualityGate to
    build/ci/Invoke-SonarAnalysis.ps1. Those are deliberately two decisions —
    same reasoning as -FailOnCrap in the `crap` CI job, which is likewise not
    passed: a threshold nobody has read a report against is a threshold nobody
    agreed to.

.PARAMETER AdminToken
    A SonarQube USER token belonging to an administrator (defaults to
    $env:SONAR_ADMIN_TOKEN). A GLOBAL_ANALYSIS_TOKEN is NOT enough — it can
    submit an analysis and nothing else, which is exactly why the analysis run
    uses one and this script does not.

.PARAMETER NewCodeDays
    The rolling window that defines "new code". See the block comment on the
    default below before changing it — the alternative ("previous version")
    is not better or worse, it depends on how often <Version> gets bumped.

.OUTPUTS
    Exit code 0 - the server matches the configuration below.
    Exit code 2 - could not reach or could not configure the server.

.EXAMPLE
    $env:SONAR_ADMIN_TOKEN = 'squ_...'
    pwsh ./infra/sonarqube/Set-SonarQubeDefaults.ps1
#>
[CmdletBinding()]
param(
    [string]$HostUrl,

    [string]$AdminToken,

    [string]$ProjectKey = 'slay-idle-repeat',

    [string]$ProjectName = 'Slay Idle Repeat',

    # Named, not "Sonar way". The built-in gate is read-only and shared by every
    # project on the server; a copy under our own name is the only way to change
    # a threshold later without a migration, and the name says whose decision it
    # is when somebody finds it in the UI a year from now.
    [string]$GateName = 'Slay Idle Repeat way',

    # 🔴 30 DAYS, NOT "PREVIOUS VERSION" — and this is the one setting here that
    # is genuinely arguable.
    #
    # SonarQube's default is "previous version", which reads <Version> from the
    # analysis. Directory.Build.props is emphatic that the assembly SemVer is
    # hand-bumped and stays 0.x.y until soft launch — so on this repository
    # "since the previous version" currently means "since 0.1.0", i.e. the entire
    # history. Clean-as-you-code with a new-code window covering everything is
    # just a whole-repository gate wearing a disguise, and it fails on day one
    # for reasons nobody in the current change can act on.
    #
    # A 30-day rolling window covers roughly a milestone and always answers the
    # question the gate is actually for: "is what we wrote recently clean?"
    #
    # Switch this to previous_version the moment <Version> starts being bumped
    # per milestone. That is strictly better when it is true, and misleading
    # while it is not.
    [ValidateRange(1, 365)]
    [int]$NewCodeDays = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' '..' 'build' 'ci' '_common.ps1')

$ExitSetupFailure = 2

function Stop-WithSetupFailure {
    param([Parameter(Mandatory)][string]$Message)
    Write-CiError -Message $Message
    exit $ExitSetupFailure
}

if (-not $HostUrl) { $HostUrl = $env:SONAR_HOST_URL }
if (-not $HostUrl) { $HostUrl = 'http://127.0.0.1:9000' }
$HostUrl = $HostUrl.TrimEnd('/')

if (-not $AdminToken) { $AdminToken = $env:SONAR_ADMIN_TOKEN }
if (-not $AdminToken) {
    Stop-WithSetupFailure "No admin token. Set `$env:SONAR_ADMIN_TOKEN or pass -AdminToken. Generate a USER token at $HostUrl/account/security."
}

$headers = @{ Authorization = "Bearer $AdminToken" }

function Invoke-Sonar {
    <#
        One place where a SonarQube API call is made, so the token is attached
        once and a failure reports the endpoint that produced it rather than a
        bare status code from somewhere in the middle of the script.
    #>
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST')][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [hashtable]$Body,
        # Statuses that are an expected answer rather than a fault — 404 for
        # "does not exist yet" is the whole basis of the idempotency below.
        [int[]]$AllowStatus = @()
    )

    $uri = "$HostUrl$Path"
    try {
        if ($Method -eq 'GET') {
            return Invoke-RestMethod -Method Get -Uri $uri -Headers $headers -TimeoutSec 30
        }
        return Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $Body -TimeoutSec 30
    } catch {
        $status = 0
        if ($_.Exception.PSObject.Properties.Name -contains 'Response' -and $_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
        }
        if ($AllowStatus -contains $status) { return $null }
        Stop-WithSetupFailure "$Method $Path failed (HTTP $status): $($_.Exception.Message)"
    }
}

Write-Section 'SonarQube defaults'
Write-Host "server        : $HostUrl"
Write-Host "project       : $ProjectKey"
Write-Host "quality gate  : $GateName"
Write-Host "new code      : last $NewCodeDays days"

$status = Invoke-Sonar -Method GET -Path '/api/system/status'
if (-not $status -or $status.status -ne 'UP') {
    Stop-WithSetupFailure "$HostUrl is not UP. Start it with: docker compose -f docker-compose.sonarqube.yml up -d --wait"
}
Write-Host "version       : $($status.version)"

# =============================================================================
# 1. The project.
# =============================================================================
Write-Section 'Project'
$existing = Invoke-Sonar -Method GET -Path "/api/projects/search?projects=$([uri]::EscapeDataString($ProjectKey))"
if ($existing.components.Count -gt 0) {
    Write-Host "unchanged : project '$ProjectKey' already exists"
} else {
    Invoke-Sonar -Method POST -Path '/api/projects/create' -Body @{ project = $ProjectKey; name = $ProjectName } | Out-Null
    Write-CiSuccess "created : project '$ProjectKey'"
}

# =============================================================================
# 2. The quality gate.
#
# 🔴 THE CONDITIONS BELOW ARE "SONAR WAY", COPIED DELIBERATELY UNCHANGED.
#
# It is tempting to arrive with opinions. Resist it on the first pass: the
# built-in gate is the "Clean as You Code" set, every threshold applies to NEW
# code only, and it is the one configuration whose behaviour on a real codebase
# is documented by somebody other than us. Changing a number before the first
# report has been read is how a gate ends up calibrated to whatever the codebase
# happened to score that week.
#
# What IS repository-specific is spelled out where it belongs instead:
#   - which files are measured at all -> SonarQube.Analysis.xml
#   - which assemblies produce coverage -> coverage.runsettings
#   - what "new code" means here       -> -NewCodeDays above
#
# The one number worth revisiting after a few runs is new_coverage. 80% on new
# code is measured against Core and Application ONLY (everything else is
# coverage-excluded), and a domain layer with an existing test suite should clear
# it comfortably. If it does, raise it — deliberately, in this file, with the
# report that justified it named in the commit message.
# =============================================================================
Write-Section 'Quality gate'

$conditions = @(
    # metric                          op   error  what it means
    @{ metric = 'new_violations';                 op = 'GT'; error = '0' }    # no new issues at all
    @{ metric = 'new_coverage';                   op = 'LT'; error = '80' }   # new lines are tested
    @{ metric = 'new_duplicated_lines_density';   op = 'GT'; error = '3' }    # new code is not copy-paste
    @{ metric = 'new_security_hotspots_reviewed'; op = 'LT'; error = '100' }  # every hotspot looked at
)

$gate = Invoke-Sonar -Method GET -Path "/api/qualitygates/show?name=$([uri]::EscapeDataString($GateName))" -AllowStatus @(404)
if (-not $gate) {
    Invoke-Sonar -Method POST -Path '/api/qualitygates/create' -Body @{ name = $GateName } | Out-Null
    Write-CiSuccess "created : quality gate '$GateName'"
    $gate = Invoke-Sonar -Method GET -Path "/api/qualitygates/show?name=$([uri]::EscapeDataString($GateName))"
} else {
    Write-Host "unchanged : quality gate '$GateName' already exists"
}

# Reconcile rather than append. A create-only script drifts the moment somebody
# edits a threshold in the UI: the gate then holds both the old condition and the
# new one, and the stricter of the two silently wins.
#
# ⚠️ On 26.8.0 a freshly created gate arrives ALREADY carrying the four
# Clean-as-you-Code conditions, so on a clean server every line below reports
# "unchanged" and creates nothing. That is not the loop failing to run — it is
# the loop confirming that the server's idea of the default and this file's agree.
# The moment they stop agreeing, this is what says so.
$current = @()
if ($gate.PSObject.Properties.Name -contains 'conditions' -and $gate.conditions) { $current = @($gate.conditions) }

foreach ($condition in $conditions) {
    $match = @($current | Where-Object { $_.metric -eq $condition.metric })
    if ($match.Count -eq 0) {
        Invoke-Sonar -Method POST -Path '/api/qualitygates/create_condition' -Body @{
            gateName = $GateName; metric = $condition.metric; op = $condition.op; error = $condition.error
        } | Out-Null
        Write-CiSuccess "created : $($condition.metric) $($condition.op) $($condition.error)"
    } elseif ($match[0].op -ne $condition.op -or "$($match[0].error)" -ne $condition.error) {
        Invoke-Sonar -Method POST -Path '/api/qualitygates/update_condition' -Body @{
            id = $match[0].id; metric = $condition.metric; op = $condition.op; error = $condition.error
        } | Out-Null
        Write-CiSuccess "updated : $($condition.metric) $($match[0].op) $($match[0].error) -> $($condition.op) $($condition.error)"
    } else {
        Write-Host "unchanged : $($condition.metric) $($condition.op) $($condition.error)"
    }
}

foreach ($stale in $current) {
    if ($conditions.metric -notcontains $stale.metric) {
        # Reported, never deleted. An extra condition is somebody's decision, and
        # a script that silently reverts a colleague's edit is worse than one
        # that is out of date.
        Write-CiWarning "'$GateName' also has a condition this file does not know about: $($stale.metric) $($stale.op) $($stale.error). Either add it here or remove it in the UI."
    }
}

Invoke-Sonar -Method POST -Path '/api/qualitygates/select' -Body @{ gateName = $GateName; projectKey = $ProjectKey } | Out-Null
Write-CiSuccess "attached : '$GateName' -> '$ProjectKey'"

# =============================================================================
# 3. The new-code period. See the 🔴 on -NewCodeDays.
# =============================================================================
Write-Section 'New code period'
Invoke-Sonar -Method POST -Path '/api/new_code_periods/set' -Body @{
    project = $ProjectKey; type = 'NUMBER_OF_DAYS'; value = "$NewCodeDays"
} | Out-Null

# Read it back, and read it back with `list` specifically.
#
# 🔴 MEASURED on 26.8.0: `set` returns HTTP 200 and
# `new_code_periods/show?project=` then still answers
# {"type":"PREVIOUS_VERSION","inherited":true} — because `set` without a branch
# lands on the main BRANCH's setting and `show` was reporting the PROJECT-level
# one, which is genuinely still unset. `list` is what shows where the value
# actually went. Anyone verifying this by hand with `show` will conclude the call
# silently did nothing; it did not.
$periods = Invoke-Sonar -Method GET -Path "/api/new_code_periods/list?project=$([uri]::EscapeDataString($ProjectKey))"
$applied = @($periods.newCodePeriods | Where-Object { $_.type -eq 'NUMBER_OF_DAYS' -and "$($_.value)" -eq "$NewCodeDays" })
if ($applied.Count -eq 0) {
    Stop-WithSetupFailure "Asked for a $NewCodeDays-day new-code period; the server reports $($periods.newCodePeriods | ConvertTo-Json -Compress). Nothing here fails loudly on its own, so the gate would silently be measuring a different window than this file says."
}
foreach ($period in $applied) {
    Write-CiSuccess "set : new code on branch '$($period.branchKey)' is the last $($period.value) days"
}

Write-Section 'Result'
Write-CiSuccess "Server configured. $HostUrl/dashboard?id=$ProjectKey"
exit 0
