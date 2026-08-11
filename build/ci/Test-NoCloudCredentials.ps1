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
    belongs in its own file and this script's -WorkflowGlob must keep pointing at
    the CI workflows only, never be relaxed to let CI log in.

.EXAMPLE
    pwsh build/ci/Test-NoCloudCredentials.ps1
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string[]]$WorkflowGlob = @('.github/workflows/ci.yml', '.github/workflows/nightly.yml'),
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
$workflowFiles = @()
foreach ($glob in $WorkflowGlob) {
    $workflowFiles += @(Get-ChildItem -Path (Join-Path $root $glob) -File -ErrorAction SilentlyContinue)
}

if ($workflowFiles.Count -eq 0) {
    Write-CiError "No workflow files matched $($WorkflowGlob -join ', ') under $root. Nothing was scanned, so this check cannot pass."
    exit 1
}

foreach ($file in $workflowFiles) {
    $relative = Get-RelativePath -Root $root -Path $file.FullName
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
