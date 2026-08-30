# Shared helpers for the build/ci scripts. Dot-source it; do not run it.
#
# Everything CI does lives in this directory as a script that a developer can run
# on a laptop with the same arguments the workflow uses. A check that only exists
# inside a YAML step is a check nobody can reproduce when it goes red.

Set-StrictMode -Version Latest

function Get-RepositoryRoot {
    <#
        build/ci/_common.ps1 -> build/ci -> build -> <repo root>
    #>
    [CmdletBinding()]
    param([string]$Override)

    if ($Override) { return (Resolve-Path -LiteralPath $Override).Path }
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..' '..')).Path
}

function Test-RunningInGitHubActions {
    return ($env:GITHUB_ACTIONS -eq 'true')
}

function Write-Section {
    param([Parameter(Mandatory)][string]$Title)
    Write-Host ''
    Write-Host "=== $Title " -NoNewline
    Write-Host ('=' * [Math]::Max(1, 70 - $Title.Length))
}

function Write-CiError {
    <# Fails visibly in the GitHub UI as well as in the log. #>
    param([Parameter(Mandatory)][string]$Message, [string]$File)

    if (Test-RunningInGitHubActions) {
        $location = if ($File) { "file=$File" } else { '' }
        Write-Host "::error $location::$($Message -replace "`r?`n", ' ')"
    }
    Write-Host "ERROR: $Message" -ForegroundColor Red
}

function Write-CiWarning {
    param([Parameter(Mandatory)][string]$Message, [string]$File)

    if (Test-RunningInGitHubActions) {
        $location = if ($File) { "file=$File" } else { '' }
        Write-Host "::warning $location::$($Message -replace "`r?`n", ' ')"
    }
    Write-Host "WARNING: $Message" -ForegroundColor Yellow
}

function Write-CiSuccess {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "OK: $Message" -ForegroundColor Green
}

function Exit-WithFailures {
    <#
        One exit path for every check script: print every failure found (never
        just the first — a check that stops at failure #1 costs a CI round trip
        per problem), then set the exit code.
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Failures,
        [Parameter(Mandatory)][string]$CheckName
    )

    Write-Section "$CheckName result"
    if ($Failures.Count -eq 0) {
        Write-CiSuccess "$CheckName passed."
        exit 0
    }

    foreach ($failure in $Failures) { Write-CiError -Message $failure }
    Write-Host ''
    Write-Host "$CheckName FAILED with $($Failures.Count) problem(s)." -ForegroundColor Red
    exit 1
}

function Read-TrxCounters {
    <#
        The real test counts out of a TRX, and whether they were measured at all.

        `dotnet test` returns exit code 0 for an assembly containing zero tests, so
        every script here reads the counters rather than the exit code.

        THREE states, not two, because the callers word them differently:
          Present=$false            - no TRX at all. The run did not complete.
          Present=$true, Measured=$false - a TRX carrying no <Counters>. Also not
                                    a measurement, but a different one: the file
                                    exists and the reader should be told so.
          Measured=$true            - real counts, including a genuine zero.

        A Total of 0 with Measured=$false means "not measured", which must never
        be reported as an empty suite: that sends the reader off to declare an
        exemption for a suite whose real problem is that it did not run.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Present = $false; Measured = $false; Total = 0; Passed = 0; Failed = 0 }
    }

    $xml = [xml](Get-Content -Raw -LiteralPath $Path)
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $counters = $xml.SelectSingleNode('//t:Counters', $ns)
    if (-not $counters) {
        return [pscustomobject]@{ Present = $true; Measured = $false; Total = 0; Passed = 0; Failed = 0 }
    }

    return [pscustomobject]@{
        Present  = $true
        Measured = $true
        Total    = [int]$counters.total
        Passed   = [int]$counters.passed

        # Every way a test can end without passing, folded into one number: a run
        # that timed out or aborted is as red as one that asserted wrong.
        Failed   = [int]$counters.failed + [int]$counters.error +
                   [int]$counters.aborted + [int]$counters.timeout
    }
}

function Get-RelativePath {
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Path)
    $full = (Resolve-Path -LiteralPath $Path).Path
    $rooted = $Root.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($full.StartsWith($rooted, [StringComparison]::OrdinalIgnoreCase)) {
        $full = $full.Substring($rooted.Length)
    }
    return ($full -replace '\\', '/')
}
