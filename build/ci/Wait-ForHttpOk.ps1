#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Polls a URL until it answers with the expected status, or fails after a
    deadline.

.DESCRIPTION
    Used by the server-image and compose-boot jobs to assert that GET /health
    returns 200 (14 §14: "boot the Docker Compose stack"). A fixed sleep would
    either be too short on a cold runner or waste time on a warm one; this waits
    for the actual signal and reports how long it took.

.EXAMPLE
    pwsh build/ci/Wait-ForHttpOk.ps1 -Url http://127.0.0.1:8080/health -ExpectedBodyPattern '"status"\s*:\s*"ok"'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Url,
    [int]$ExpectedStatus = 200,
    [string]$ExpectedBodyPattern,
    [int]$TimeoutSeconds = 120,
    [int]$IntervalSeconds = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

Write-Section "Waiting for $Url"
Write-Host "Expecting status $ExpectedStatus within ${TimeoutSeconds}s"

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$attempt = 0
$lastProblem = 'no attempt completed'

while ((Get-Date) -lt $deadline) {
    $attempt++
    try {
        # -SkipHttpErrorCheck so a 503 during startup is a retryable observation
        # rather than a terminating error.
        $response = Invoke-WebRequest -Uri $Url -Method Get -TimeoutSec 10 -SkipHttpErrorCheck -MaximumRedirection 0
        $status = [int]$response.StatusCode

        if ($status -ne $ExpectedStatus) {
            $lastProblem = "attempt ${attempt}: status $status (want $ExpectedStatus)"
        } elseif ($ExpectedBodyPattern -and ($response.Content -notmatch $ExpectedBodyPattern)) {
            $lastProblem = "attempt ${attempt}: status $status but body did not match /$ExpectedBodyPattern/ - body was: $($response.Content)"
        } else {
            Write-Host "attempt ${attempt}: HTTP $status"
            Write-Host "Body: $($response.Content)"
            Write-CiSuccess "$Url answered $status after $attempt attempt(s)."
            exit 0
        }
    } catch {
        $lastProblem = "attempt ${attempt}: $($_.Exception.Message)"
    }

    Write-Host $lastProblem
    Start-Sleep -Seconds $IntervalSeconds
}

Write-CiError "$Url did not answer $ExpectedStatus within ${TimeoutSeconds}s. Last: $lastProblem"
exit 1
