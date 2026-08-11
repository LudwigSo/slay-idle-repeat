#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails if CI has acquired a cloud credential, a registry login or a repository
    secret.

.DESCRIPTION
    14 §1.1 🔒, the portability rule:

        "Portability test | CI builds and boots the full stack in Docker Compose
         on every commit. If it cannot run on a laptop, it is locked in."
        "Local development | `docker compose up` brings the entire stack ... up on
         a laptop with no cloud account."

    That is the real assertion behind the compose-boot job, and it is not
    something a green boot proves - a stack that boots BECAUSE the workflow first
    authenticated to a cloud registry has failed the test while reporting
    success. The only way to check it is to look at what CI is allowed to reach
    for. So: no `secrets.*` expressions, no cloud login actions, no OIDC token
    permission, no registry push, no image pulled from a private cloud registry.

    Server release (14 §14) does push a container image to a registry - that is a
    RELEASE workflow, deliberately out of scope here. If one is ever added, it
    belongs in its own file, and it must be named in -WorkflowExclusion below
    with its reason - never removed from the glob, and never allowed to relax the
    bans for the CI workflows.

    ⚠️ WHY THE GLOB IS A GLOB, not a list of filenames.

    This check's whole value is "it looked at everything CI can reach for". An
    earlier version named ci.yml and nightly.yml literally. Under that version,
    renaming nightly.yml - or adding codeql.yml in M1 - meant the new file was
    silently never scanned while the check still reported success, so a
    ${{ secrets.* }} inside it would collect a green tick. A gate that decides
    what to inspect from a hard-coded list degrades every time the repository
    grows, and does so invisibly.

    So: scan every workflow, and make skipping one an explicit, reviewable,
    self-expiring diff (a -WorkflowExclusion entry whose file has disappeared is
    itself a failure).

.EXAMPLE
    pwsh build/ci/Test-NoCloudCredentials.ps1
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,

    # Every workflow, not a list of names. See the ⚠️ note above.
    [string[]]$WorkflowGlob = @('.github/workflows/*.yml'),

    # Workflows deliberately NOT held to the bans, each with the reason it is
    # out of scope. Empty today: ci.yml and nightly.yml are both CI, and both
    # must run with no credentials whatsoever.
    #
    # The only entry that ever belongs here is a RELEASE workflow (14 §14), which
    # pushes a server image to a registry and therefore does legitimately hold a
    # credential. Adding a name here is how you say "this file is not CI"; it is
    # not a way to quiet a finding in a file that is.
    #
    # Format: @{ Name = 'release.yml'; Why = '14 §14 server release; ...' }
    [hashtable[]]$WorkflowExclusion = @(),

    [string[]]$ComposeGlob = @('docker-compose.yml', 'docker-compose.*.yml')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Get-RepositoryRoot -Override $RepositoryRoot

Write-Section 'No cloud credentials (14 §1.1)'
Write-Host "Repository root : $root"

$failures = [System.Collections.Generic.List[string]]::new()

# Pattern, and what it would mean if it appeared.
$workflowBans = @(
    @{ Pattern = '\$\{\{\s*secrets\.'; Why = 'a repository secret is being injected. CI must boot the stack with no credentials of any kind.' }
    @{ Pattern = 'docker/login-action';                     Why = 'a container registry login.' }
    @{ Pattern = 'aws-actions/configure-aws-credentials';   Why = 'an AWS credential.' }
    @{ Pattern = 'google-github-actions/auth';              Why = 'a Google Cloud credential.' }
    @{ Pattern = 'azure/login';                             Why = 'an Azure credential.' }
    @{ Pattern = 'docker\s+login';                          Why = 'a registry login.' }
    @{ Pattern = 'docker\s+push';                           Why = 'a registry push. CI builds the image; publishing is a release workflow.' }
    @{ Pattern = 'id-token:\s*write';                       Why = 'OIDC federation to a cloud provider.' }
)

function Remove-YamlComments {
    <#
        Scan what the workflow DOES, not what it says about itself. These files
        are heavily commented - several comments name the very things banned
        below, precisely to explain why they are banned - and a checker that
        cannot tell a prohibition from a violation is one people learn to work
        around by deleting the explanation.
    #>
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    $kept = foreach ($line in ($Text -split "`r?`n")) {
        if ($line -match '^\s*#') { '' } else { $line }
    }
    return ($kept -join "`n")
}

$cloudRegistryHosts = @('\.dkr\.ecr\.[a-z0-9-]+\.amazonaws\.com', 'gcr\.io/', '[a-z0-9-]+\.azurecr\.io', '[a-z0-9-]+\.pkg\.dev/')

# ------------------------------------------------------------------ workflows
# Per-glob, not in aggregate. A glob that matches nothing is a glob that has gone
# stale - a directory renamed, an extension changed - and rolling it into a total
# lets one live glob cover for a dead one. There is no useful sense in which a
# pattern this script was told to scan matching zero files is a pass.
$workflowFiles = @()
foreach ($glob in $WorkflowGlob) {
    $matched = @(Get-ChildItem -Path (Join-Path $root $glob) -File -ErrorAction SilentlyContinue)
    if ($matched.Count -eq 0) {
        Write-CiError ("Workflow glob '$glob' matched no file under $root. Nothing was scanned for it, so " +
            "this check cannot report success: the pattern is stale, or the workflows have moved.")
        exit 1
    }
    Write-Host "glob '$glob' -> $($matched.Count) file(s)"
    $workflowFiles += $matched
}

$workflowFiles = @($workflowFiles | Sort-Object FullName -Unique)

if ($workflowFiles.Count -eq 0) {
    Write-CiError "No workflow files matched $($WorkflowGlob -join ', ') under $root. Nothing was scanned, so this check cannot pass."
    exit 1
}

# Excluded names are checked against what is actually there, so an exclusion
# cannot outlive the file it was written for. Same reasoning as the stale
# exemption rule in build/ci/test-suites.json: an exemption nobody is forced to
# revisit is an exemption that quietly becomes a hole.
$excludedNames = @($WorkflowExclusion | ForEach-Object { $_.Name })
foreach ($exclusion in $WorkflowExclusion) {
    if ($workflowFiles.Name -notcontains $exclusion.Name) {
        $failures.Add("STALE EXCLUSION: -WorkflowExclusion names '$($exclusion.Name)' ($($exclusion.Why)) " +
            "but no such workflow exists. Delete the entry - it is what keeps a future file of that name " +
            "unscanned without anyone deciding to skip it.")
    }
}

foreach ($file in $workflowFiles) {
    $relative = Get-RelativePath -Root $root -Path $file.FullName

    if ($excludedNames -contains $file.Name) {
        $why = @($WorkflowExclusion | Where-Object { $_.Name -eq $file.Name })[0].Why
        Write-CiWarning "skipped: $relative - $why"
        continue
    }

    $text = Remove-YamlComments -Text (Get-Content -Raw -LiteralPath $file.FullName)
    Write-Host "scanned: $relative"

    foreach ($ban in $workflowBans) {
        if ($text -match $ban.Pattern) {
            $failures.Add("$relative matches /$($ban.Pattern)/ - $($ban.Why)")
        }
    }
    foreach ($registryHost in $cloudRegistryHosts) {
        if ($text -match $registryHost) {
            $failures.Add("$relative references a private cloud registry (/$registryHost/). Base images come from public registries only.")
        }
    }
}

# -------------------------------------------------------------------- compose
# docker-compose.yml arrives with M0-03. Its absence is not a failure here - the
# compose-boot job is what reports that - but when it exists it is held to the
# same rule.
$composeFiles = @()
foreach ($glob in $ComposeGlob) {
    $composeFiles += @(Get-ChildItem -Path (Join-Path $root $glob) -File -ErrorAction SilentlyContinue)
}

if ($composeFiles.Count -eq 0) {
    Write-CiWarning "No compose file found yet (M0-03). Only the workflows were scanned."
} else {
    foreach ($file in $composeFiles) {
        $relative = Get-RelativePath -Root $root -Path $file.FullName
        $text = Remove-YamlComments -Text (Get-Content -Raw -LiteralPath $file.FullName)
        Write-Host "scanned: $relative"

        if ($text -match '\$\{\{') {
            $failures.Add("$relative contains a GitHub Actions expression. Compose must be runnable verbatim on a laptop, with no CI templating.")
        }
        foreach ($registryHost in $cloudRegistryHosts) {
            if ($text -match $registryHost) {
                $failures.Add("$relative pulls an image from a private cloud registry (/$registryHost/), so 'docker compose up' would need an account. 14 §1.1 forbids it.")
            }
        }
    }
    # Note on deliberate non-bans: MinIO is configured with AWS_ACCESS_KEY_ID and
    # friends because it speaks the S3 API. Those are local dev values for a local
    # container, not a cloud account, so credential-shaped variable NAMES are not
    # banned here - only real cloud endpoints and real secret injection are.
}

Exit-WithFailures -Failures $failures.ToArray() -CheckName 'No cloud credentials'
