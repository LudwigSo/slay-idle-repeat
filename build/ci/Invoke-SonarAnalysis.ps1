#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs a full SonarQube analysis of the solution — begin, build, test with
    coverage, end — against a SonarQube server.

.DESCRIPTION
    SonarScanner for .NET is not a linter you point at a directory. It wraps the
    build: `begin` installs MSBuild targets that make the compiler emit Sonar's
    Roslyn diagnostics, the build produces them, and `end` collects everything
    and uploads it. Miss the build in between and the analysis uploads an empty
    project while reporting success.

    Everything CI would do lives here rather than in a YAML step, so the same
    command runs on a laptop against the local server from
    docker-compose.sonarqube.yml. See infra/sonarqube/README.md.

    ⚠️ SonarQube COMMUNITY BUILD HAS NO BRANCH OR PULL-REQUEST ANALYSIS. Every
    run lands on the project's single "main" branch, whatever branch the working
    tree is on. That is an edition limit, not a setting, and it is why this
    script takes no -Branch parameter: passing sonar.branch.name to Community
    Build fails the run outright. On a feature branch the honest reading of a
    result is "this is what the project would look like if this branch were
    main".

.PARAMETER ProjectKey
    The SonarQube project key. Must already exist on the server, or the token
    must carry "Create Projects" — see infra/sonarqube/README.md.

.PARAMETER HostUrl
    Defaults to $env:SONAR_HOST_URL, then to the local compose server.

.PARAMETER Token
    Defaults to $env:SONAR_TOKEN. Never defaulted to a literal, never logged.

.PARAMETER SkipTests
    Analyse without running the suites. Faster, and every coverage figure the
    run uploads is then 0% — which SonarQube records as fact. Use it for a quick
    rule check, never for a run whose numbers anyone will quote.

.PARAMETER WaitForQualityGate
    Block after upload until the server has finished the background analysis and
    then fail (exit 1) if the quality gate is red. Off by default: the gate is
    calibrated in the UI, and the first few runs on a codebase that has never
    been analysed exist to find out what the numbers ARE.

.OUTPUTS
    Exit code 0 - analysis uploaded (and the gate passed, if -WaitForQualityGate).
    Exit code 1 - the quality gate failed. Only reachable with -WaitForQualityGate.
    Exit code 2 - setup or precondition failure. Nothing was uploaded.

.EXAMPLE
    # Local server from docker-compose.sonarqube.yml.
    $env:SONAR_TOKEN = 'squ_...'
    pwsh ./build/ci/Invoke-SonarAnalysis.ps1

.EXAMPLE
    # The way a gate would run it.
    pwsh ./build/ci/Invoke-SonarAnalysis.ps1 -WaitForQualityGate
#>
[CmdletBinding()]
param(
    [string]$ProjectKey = 'slay-idle-repeat',

    [string]$ProjectName = 'Slay Idle Repeat',

    # Defaults to <Version> in Directory.Build.props — "the assembly SemVer", the
    # first of the three numbers that file is emphatic about keeping apart. This
    # is the one that belongs on an analysis: it says which build was measured.
    # PROTOCOL_VERSION and SchemaVersion mean nothing to SonarQube.
    [string]$ProjectVersion,

    [string]$HostUrl,

    [string]$Token,

    [string]$Solution,

    [string]$Configuration = 'Release',

    [string]$RepositoryRoot,

    # 🔴 The architecture suite must not run under coverage instrumentation, and
    # this is not about speed. Its rules read the IL of Core and Application with
    # Mono.Cecil and NetArchTest; coverlet rewrites those same assemblies on disk
    # for the duration of a run, so the rules scan coverlet's injected tracking
    # code and report violations that are not in the source. Measured in
    # scripts/Measure-Crap.ps1 (173/173 pass without coverage, exactly 4 fail
    # with it) — the same exclusion, for the same reason, and it stays in step
    # with that file by being spelled the same way.
    #
    # It costs no coverage: those rules read assemblies rather than executing
    # them. It still runs, uninstrumented, in its own CI job.
    [string[]]$ExcludeSuites = @('SlayIdleRepeat.Architecture.Tests'),

    [switch]$SkipTests,

    [switch]$WaitForQualityGate,

    # Seconds the end step waits for the server's background analysis before
    # giving up. Only meaningful with -WaitForQualityGate.
    [ValidateRange(30, 3600)]
    [int]$QualityGateTimeout = 300,

    # Turns on sonar.verbose for the run WITHOUT editing SonarQube.Analysis.xml,
    # so a debugging session cannot be committed by accident.
    [switch]$ScannerVerbose
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned for the same reason build/ci/Invoke-UnitTests.ps1 and
# scripts/Measure-Crap.ps1 pin it: this script inspects $LASTEXITCODE after
# `dotnet` itself, and a runner image that flips this preference would turn those
# checks into thrown exceptions with different exit codes than the contract in
# .OUTPUTS promises.
$PSNativeCommandUseErrorActionPreference = $false

. (Join-Path $PSScriptRoot '_common.ps1')

$ExitQualityGateFailed = 1
$ExitSetupFailure = 2

function Stop-WithSetupFailure {
    param([Parameter(Mandatory)][string]$Message)
    Write-CiError -Message $Message
    exit $ExitSetupFailure
}

$root = Get-RepositoryRoot -Override $RepositoryRoot

# Move there. Not for the paths below — those are absolute — but because
# `dotnet tool restore` and `dotnet sonarscanner` locate the local tool manifest
# by walking UP FROM THE CURRENT DIRECTORY, and because the scanner takes
# sonar.projectBaseDir from the working directory. Run this by absolute path
# from somewhere else and both quietly point at the wrong tree.
Push-Location $root
try {

    if (-not $Solution) {
        $solutionFiles = @(Get-ChildItem -Path $root -Filter '*.sln' -File)
        if ($solutionFiles.Count -ne 1) {
            Stop-WithSetupFailure "Expected exactly one .sln at $root, found $($solutionFiles.Count). Pass -Solution."
        }
        $Solution = $solutionFiles[0].FullName
    }

    $settingsFile = Join-Path $root 'SonarQube.Analysis.xml'
    $runSettings = Join-Path $root 'coverage.runsettings'
    $testResultsDir = Join-Path $root 'TestResults'

    # ------------------------------------------------------------- 0. preflight
    # Everything that can be known before a forty-minute build is checked here.
    # Every one of these has a failure mode that otherwise surfaces as a green
    # run with wrong numbers rather than as an error.
    Write-Section 'Preflight'

    $failures = [System.Collections.Generic.List[string]]::new()

    if (-not $HostUrl) { $HostUrl = $env:SONAR_HOST_URL }
    if (-not $HostUrl) {
        # The local compose server. 127.0.0.1 and not localhost: on Windows the
        # latter can resolve to ::1 first, and the published port is IPv4-only.
        $HostUrl = 'http://127.0.0.1:9000'
    }
    $HostUrl = $HostUrl.TrimEnd('/')

    if (-not $Token) { $Token = $env:SONAR_TOKEN }
    if (-not $Token) {
        $failures.Add("No token. Set `$env:SONAR_TOKEN or pass -Token. Generate one at $HostUrl/account/security (see infra/sonarqube/README.md).")
    }

    if (-not (Test-Path -LiteralPath $settingsFile)) {
        $failures.Add("$settingsFile is missing. It is the only place this repository's exclusions and coverage paths live; without it the scanner silently falls back to the defaults shipped inside the dotnet-sonarscanner package and analyses everything.")
    }
    if (-not (Test-Path -LiteralPath $runSettings)) {
        $failures.Add("$runSettings is missing. It is what turns coverage collection on at all.")
    }

    # --- is the settings file shaped so the scanner can read it? --------------
    #
    # 🔴 THIS CHECK EXISTS BECAUSE THE FAILURE IT CATCHES ALREADY HAPPENED HERE,
    # and it left no trace anywhere a person would look.
    #
    # The scanner deserialises each <Property> as a plain string, so a comment
    # INSIDE the element truncates the value to whatever text follows the last
    # child node — silently. `begin` exits 0, the build runs, the analysis
    # uploads, and the project page looks entirely plausible while an exclusion
    # list has been cut down to its last entry. Measured against scanner 11.2.1;
    # see the 🔴 block at the top of SonarQube.Analysis.xml.
    #
    # Checked HERE rather than by reading back what `begin` stored, because
    # .sonarqube/conf/SonarQubeAnalysisConfig.xml holds only a subset of the
    # properties and would report perfectly good settings as missing.
    if (Test-Path -LiteralPath $settingsFile) {
        [xml]$settingsXml = Get-Content -Raw -LiteralPath $settingsFile
        # local-name(), not the namespace prefix: the document carries a default
        # namespace, and an XPath that names it is a check that starts silently
        # passing the day a scanner upgrade changes it.
        $properties = @($settingsXml.SelectNodes("/*[local-name()='SonarQubeAnalysisProperties']/*[local-name()='Property']"))
        if ($properties.Count -eq 0) {
            $failures.Add("$settingsFile declares no <Property> elements at all. Either it is empty or its xmlns does not match what the scanner expects, and every setting in it would be ignored.")
        }
        foreach ($property in $properties) {
            $name = $property.GetAttribute('Name')
            $children = @($property.ChildNodes)
            $comments = @($children | Where-Object { $_.NodeType -ne 'Text' })
            if ($comments.Count -gt 0) {
                $failures.Add("SonarQube.Analysis.xml: <Property Name=`"$name`"> contains a $($comments[0].NodeType) node. The scanner keeps ONLY the text after the last child node, so this value would be silently truncated to '$($children[-1].Value.Trim())'. Move the explanation above the element.")
            }
            if ($property.InnerText -match '[\r\n]') {
                $failures.Add("SonarQube.Analysis.xml: <Property Name=`"$name`"> spans multiple lines. Line breaks alone are tolerated by the scanner, but this is the shape that invites a comment into the element — keep the value on one line.")
            }
        }
    }

    # --- the coverage-scope tripwire -----------------------------------------
    # coverage.runsettings decides what is INSTRUMENTED; SonarQube.Analysis.xml
    # decides what SonarQube EXPECTS coverage for. They are one decision written
    # in two files, and the failure mode when they drift is silent: an assembly
    # that stops being instrumented but is still expected reads as 0% covered,
    # which SonarQube then reports as a fact about the tests.
    #
    # So: pin the strings. Widening the allow-list is a deliberate act, and this
    # makes updating SonarQube.Analysis.xml part of it.
    if (Test-Path -LiteralPath $runSettings) {
        [xml]$runSettingsXml = Get-Content -Raw -LiteralPath $runSettings
        $collectorConfig = $runSettingsXml.RunSettings.DataCollectionRunSettings.DataCollectors.DataCollector.Configuration

        $expectedInclude = '[SlayIdleRepeat.Core]*,[SlayIdleRepeat.Application]*'
        $actualInclude = ($collectorConfig.Include -replace '\s', '')
        if ($actualInclude -ne $expectedInclude) {
            $failures.Add(
                "coverage.runsettings' <Include> is now '$actualInclude', not '$expectedInclude'. " +
                "The coverage scope moved. sonar.coverage.exclusions in SonarQube.Analysis.xml is the mirror of that " +
                "allow-list and has to move with it, or SonarQube will report 0% for whatever changed hands. " +
                "Update both, then update the expected string in this script.")
        }

        # coverage.runsettings' own header says of OpenCover "Nothing reads it
        # for CRAP". This is the thing that reads it: SonarQube's C# plugin has
        # no Cobertura importer, so dropping `opencover` from <Format> turns
        # every coverage number here into 0% with nothing going red.
        if ($collectorConfig.Format -notmatch 'opencover') {
            $failures.Add(
                "coverage.runsettings' <Format> is '$($collectorConfig.Format)' and does not include 'opencover'. " +
                "SonarQube cannot read Cobertura — sonar.cs.opencover.reportsPaths is the only supported path for " +
                "coverlet output — so this run would upload 0% coverage for everything.")
        }
    }

    # --- the JRE -------------------------------------------------------------
    # The end step runs a Java analysis engine. Scanner 11.x can download a JRE
    # from the server when one is missing, so this is a warning and not a
    # failure — but a missing JRE turns into a surprise download partway through
    # a run, and on an air-gapped machine into a failure at the very last step.
    $java = Get-Command java -ErrorAction SilentlyContinue
    if (-not $java) {
        Write-CiWarning "No `java` on PATH. The scanner will try to provision a JRE from $HostUrl. Install a JDK 17+ to avoid the download."
    } else {
        $javaVersion = (& java -version 2>&1 | Select-Object -First 1)
        Write-Host "java            : $javaVersion"
    }

    # --- the server ----------------------------------------------------------
    # Asked before the build, because the alternative is discovering the server
    # is down after paying for a full Release build of thirty-four projects.
    if ($failures.Count -eq 0) {
        try {
            $status = Invoke-RestMethod -Method Get -Uri "$HostUrl/api/system/status" -TimeoutSec 15
            if ($status.status -ne 'UP') {
                $failures.Add("$HostUrl reports status '$($status.status)', not 'UP'. It is still starting, migrating its database, or degraded. Nothing would be uploaded.")
            } else {
                Write-Host "server          : $HostUrl (SonarQube $($status.version), UP)"
            }
        } catch {
            $failures.Add("$HostUrl is not answering /api/system/status ($($_.Exception.Message)). Start it with: docker compose -f docker-compose.sonarqube.yml up -d --wait")
        }
    }

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) { Write-CiError -Message $failure }
        Stop-WithSetupFailure "$($failures.Count) precondition(s) failed. Nothing was built and nothing was uploaded."
    }

    if (-not $ProjectVersion) {
        # SelectNodes, not dotted property access: Directory.Build.props has
        # SEVERAL <PropertyGroup> elements on purpose (the framework settings and
        # "THE THREE NUMBERS" are deliberately kept apart), and under StrictMode
        # `.PropertyGroup.Version` throws on the resulting element array rather
        # than returning the one group that has a Version.
        [xml]$buildProps = Get-Content -Raw -LiteralPath (Join-Path $root 'Directory.Build.props')
        $versionNode = $buildProps.SelectSingleNode('/Project/PropertyGroup/Version')
        if ($versionNode) {
            $ProjectVersion = $versionNode.InnerText.Trim()
        } else {
            # Not fatal, but say so: an analysis with no version cannot be told
            # apart from the one before it in the project's history.
            Write-CiWarning 'No <Version> in Directory.Build.props; uploading as 0.0.0.'
            $ProjectVersion = '0.0.0'
        }
    }

    Write-Host "repository root : $root"
    Write-Host "solution        : $Solution"
    Write-Host "project key     : $ProjectKey"
    Write-Host "project version : $ProjectVersion"
    Write-Host "settings file   : $settingsFile"
    Write-Host "coverage        : $(if ($SkipTests) { 'SKIPPED (-SkipTests) — every uploaded number will be 0%' } else { 'collected' })"
    Write-Host "quality gate    : $(if ($WaitForQualityGate) { "waited on, ${QualityGateTimeout}s timeout" } else { 'not waited on' })"

    # ---------------------------------------------------- 1. stale coverage
    # Mandatory, not an optimisation, and the same trap Measure-Crap.ps1
    # documents: coverlet writes into a fresh GUID directory per run, so a
    # leftover TestResults/ is globbed in by sonar.cs.opencover.reportsPaths and
    # averaged into the coverage this run uploads.
    if (-not $SkipTests) {
        Write-Section 'Clearing stale coverage'
        if (Test-Path -LiteralPath $testResultsDir) {
            Remove-Item -LiteralPath $testResultsDir -Recurse -Force
            Write-Host 'TestResults/ : deleted'
        } else {
            Write-Host 'TestResults/ : absent'
        }
    }

    # --------------------------------------------------------- 2. tool restore
    Write-Section 'dotnet tool restore'
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        Stop-WithSetupFailure ".config/dotnet-tools.json is what pins dotnet-sonarscanner to the version CI analyses with; without a restore there is no scanner to run (exit $LASTEXITCODE)."
    }

    # ---------------------------------------------------------------- 3. begin
    # Installs the MSBuild targets that make the next step's compilation emit
    # Sonar's diagnostics, and downloads the quality profile from the server.
    Write-Section 'sonarscanner begin'
    $beginArgs = @(
        'sonarscanner', 'begin'
        "/k:$ProjectKey"
        "/n:$ProjectName"
        "/v:$ProjectVersion"
        # Absolute. A relative /s: is resolved against the scanner's own working
        # directory, and when it misses, the scanner falls back to the default
        # SonarQube.Analysis.xml INSIDE the nuget package without saying so.
        "/s:$settingsFile"
        "/d:sonar.host.url=$HostUrl"
        "/d:sonar.token=$Token"
    )
    if ($WaitForQualityGate) {
        $beginArgs += "/d:sonar.qualitygate.wait=true"
        $beginArgs += "/d:sonar.qualitygate.timeout=$QualityGateTimeout"
    }
    if ($ScannerVerbose) { $beginArgs += '/d:sonar.verbose=true' }

    # Logged with the token redacted. The scanner itself masks it in its own
    # output; this line is ours and has to do the same.
    Write-Host "dotnet $(($beginArgs -join ' ') -replace 'sonar\.token=\S+', 'sonar.token=***')"
    & dotnet @beginArgs
    if ($LASTEXITCODE -ne 0) {
        Stop-WithSetupFailure "sonarscanner begin exited $LASTEXITCODE. Nothing was analysed. A 401/403 here means the token is wrong or lacks 'Execute Analysis'; a 404 on the project means it does not exist and the token cannot create it."
    }

    # ---------------------------------------------------------------- 4. build
    #
    # 🔴 --no-incremental IS LOAD-BEARING. The scanner sees a project through the
    # compiler actually running on it. MSBuild skips a project whose outputs are
    # up to date, that project emits no diagnostics, and it lands in SonarQube
    # with zero issues and zero lines — indistinguishable from clean code. A
    # second run in the same working tree is exactly when that happens.
    #
    # ⚠️ NOTHING IS PASSED HERE ABOUT TreatWarningsAsErrors, AND THAT IS THE
    # ANSWER TO THE OBVIOUS QUESTION. `begin` injects SonarAnalyzer.CSharp's
    # several hundred rules into this compilation as warnings, and
    # Directory.Build.props sets TreatWarningsAsErrors=true for the whole
    # repository — which reads like a build that dies on the first S-rule finding
    # with `end` never running and the analysis reporting nothing.
    #
    # It does not, and this was measured rather than reasoned about. The
    # scanner's own injected targets contain, immediately before CoreCompile:
    #
    #     <!-- Make sure no warnings are treated as errors -->
    #     <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    #
    # (.sonarqube/bin/targets/SonarQube.Integration.targets, scanner 11.2.1.)
    # A build of SlayIdleRepeat.Core inside a scanner session produced 151
    # warnings and 0 errors with the property still evaluating to `true`.
    #
    # So an earlier draft of this script passed -p:TreatWarningsAsErrors=false
    # "to make the analysis possible". It was redundant, and a global -p: would
    # have applied it to every project in the solution — quietly weakening a rule
    # this repository is deliberate about, to fix a problem that does not exist.
    Write-Section 'dotnet build'
    & dotnet build $Solution `
        --configuration $Configuration `
        --no-incremental
    if ($LASTEXITCODE -ne 0) {
        Stop-WithSetupFailure "dotnet build exited $LASTEXITCODE inside the Sonar begin/end pair. Fix the build first — an analysis of a solution that does not compile measures nothing."
    }

    # ----------------------------------------------------------------- 5. test
    if ($SkipTests) {
        Write-Section 'Skipping tests (-SkipTests)'
        Write-CiWarning 'No coverage will be collected, so this run uploads 0% for every file SonarQube expects coverage for. Do not quote its numbers.'
    } else {
        $suites = @(
            Get-ChildItem -Path (Join-Path $root 'tests') -Filter '*.csproj' -Recurse -File |
                Where-Object { $ExcludeSuites -notcontains [IO.Path]::GetFileNameWithoutExtension($_.Name) } |
                Sort-Object -Property Name
        )
        if ($suites.Count -eq 0) {
            Stop-WithSetupFailure "No test project under $root/tests/ survived -ExcludeSuites ($($ExcludeSuites -join ', '))."
        }

        Write-Section "dotnet test — $($suites.Count) suite(s) under coverage"
        # Per suite, not one `dotnet test` over the solution, because one suite
        # has to be left out — see the 🔴 on -ExcludeSuites. Sequential rather
        # than throttled in parallel like Measure-Crap.ps1: this runs after a
        # full Release build inside a scanner session, and interleaved output
        # from six suites is unreadable when one of them fails.
        #
        # --no-build reuses step 4's output. Rebuilding here would ALSO be a
        # rebuild inside the begin/end pair and would be incremental — see the
        # 🔴 above for why that quietly empties the analysis.
        $testExitCode = 0
        foreach ($suite in $suites) {
            $name = [IO.Path]::GetFileNameWithoutExtension($suite.Name)
            & dotnet test $suite.FullName `
                --configuration $Configuration `
                --no-build `
                --settings $runSettings `
                --results-directory (Join-Path $testResultsDir $name) `
                --logger "trx;LogFileName=$name.trx"
            if ($LASTEXITCODE -ne 0) { $testExitCode = $LASTEXITCODE }
        }

        # A failing suite is REPORTED but not fatal. coverlet still writes
        # coverage for the tests that did run, and SonarQube wants the failure
        # counts too — its "Unit Tests" measures are the point of importing the
        # TRX files at all. Same trade as Measure-Crap.ps1: the numbers are
        # optimistic and the log says so.
        if ($testExitCode -ne 0) {
            Write-CiWarning "At least one suite failed (last exit $testExitCode). Coverage below is from a run with failing tests and is therefore OPTIMISTIC. The analysis still uploads, and the failures show up on the project page."
        }

        $coverageFiles = @(Get-ChildItem -Path $testResultsDir -Filter 'coverage.opencover.xml' -Recurse -File -ErrorAction SilentlyContinue)
        if ($coverageFiles.Count -eq 0) {
            Stop-WithSetupFailure (
                "No coverage.opencover.xml anywhere under $testResultsDir.`n" +
                "  The 'XPlat code coverage' collector named in coverage.runsettings comes from the coverlet.collector package:`n" +
                "  with no reference, dotnet test accepts --settings, runs green, and writes no coverage whatsoever.`n" +
                "  Uploading now would record 0% as a measured fact, so this stops instead.")
        }
        Write-Host ''
        Write-Host "coverage files : $($coverageFiles.Count)"
        foreach ($file in $coverageFiles) { Write-Host "  $(Get-RelativePath -Root $root -Path $file.FullName)" }
    }

    # ------------------------------------------------------------------ 6. end
    # Collects the diagnostics, reads the coverage and TRX files named in
    # SonarQube.Analysis.xml, runs the Java engine over everything the MSBuild
    # projects did not cover (JSON, YAML, Dockerfiles, shell, Python), and
    # uploads. The token is passed again: `end` is a separate process.
    Write-Section 'sonarscanner end'
    # Teed to a file as well as the console, because the engine states the scope
    # it actually applied in three INFO lines in the middle of a few thousand,
    # and those lines are the only place the truth about the exclusions appears.
    # The check below reads them back; a human debugging a surprising number on
    # the dashboard reads them for the same reason.
    $endLog = Join-Path $root 'artifacts' 'sonar' 'sonarscanner-end.log'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $endLog) | Out-Null
    & dotnet sonarscanner end "/d:sonar.token=$Token" 2>&1 | Tee-Object -FilePath $endLog
    $endExitCode = $LASTEXITCODE

    if ($endExitCode -ne 0) {
        if ($WaitForQualityGate) {
            # `end` exits non-zero for BOTH "the upload failed" and "the gate is
            # red", and they mean opposite things: one is broken plumbing, the
            # other is the tool working. Ask the server which happened rather
            # than guessing from an exit code.
            Write-Section 'Quality gate'
            try {
                $headers = @{ Authorization = "Bearer $Token" }
                $gate = Invoke-RestMethod -Method Get -Headers $headers -TimeoutSec 30 `
                    -Uri "$HostUrl/api/qualitygates/project_status?projectKey=$([uri]::EscapeDataString($ProjectKey))"
                if ($gate.projectStatus.status -eq 'ERROR') {
                    foreach ($condition in $gate.projectStatus.conditions) {
                        if ($condition.status -eq 'ERROR') {
                            Write-CiError -Message "$($condition.metricKey): $($condition.actualValue) (threshold $($condition.comparator) $($condition.errorThreshold))"
                        }
                    }
                    Write-Host ''
                    Write-Host "Quality gate FAILED. Full report: $HostUrl/dashboard?id=$ProjectKey" -ForegroundColor Red
                    exit $ExitQualityGateFailed
                }
            } catch {
                Write-CiWarning "Could not read the quality gate status back ($($_.Exception.Message)); falling through to the scanner's own exit code."
            }
        }
        Stop-WithSetupFailure "sonarscanner end exited $endExitCode. The analysis was not uploaded, or the server rejected it. Its output is above."
    }

    # ------------------------------------------- 6b. what scope actually applied
    #
    # The other half of the settings-file check in preflight. That one says the
    # file is SHAPED right; this one says the engine RECEIVED it. Between them
    # sits everything that can quietly drop a property — a scanner upgrade, a
    # renamed key, a pattern the engine parses differently than we read it.
    #
    # The engine announces the scope it applied, once per module, as:
    #     INFO:   Excluded sources: ...
    #     INFO:   Excluded sources for coverage: ...
    #     INFO:   Excluded sources for duplication: ...
    # sonar.exclusions legitimately gains an entry the scanner adds itself (the
    # coverage report path, so the OpenCover XML is not analysed as source), so
    # this checks that every pattern we asked for is PRESENT, never that the
    # lists are identical.
    Write-Section 'Applied scope'
    $endOutput = Get-Content -Raw -LiteralPath $endLog
    $scopeChecks = @(
        @{ Property = 'sonar.exclusions';          Pattern = 'Excluded sources:\s*(.+)' }
        @{ Property = 'sonar.coverage.exclusions'; Pattern = 'Excluded sources for coverage:\s*(.+)' }
        @{ Property = 'sonar.cpd.exclusions';      Pattern = 'Excluded sources for duplication:\s*(.+)' }
    )

    $scopeProblems = [System.Collections.Generic.List[string]]::new()
    foreach ($check in $scopeChecks) {
        $node = $settingsXml.SelectSingleNode("/*[local-name()='SonarQubeAnalysisProperties']/*[local-name()='Property'][@Name='$($check.Property)']")
        if (-not $node) { continue }

        $match = [regex]::Match($endOutput, $check.Pattern)
        if (-not $match.Success) {
            $scopeProblems.Add("$($check.Property) is set, but the engine never reported the matching scope line. It may not have been applied at all — see $endLog.")
            continue
        }

        $applied = $match.Groups[1].Value
        $missing = @($node.InnerText -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -and ($applied -notlike "*$_*") })
        if ($missing.Count -gt 0) {
            $scopeProblems.Add("$($check.Property): the engine applied '$applied' and did NOT include $($missing -join ', ').")
        } else {
            Write-Host "$($check.Property) -> $applied"
        }
    }

    if ($scopeProblems.Count -gt 0) {
        foreach ($problem in $scopeProblems) { Write-CiError -Message $problem }
        # A warning, not a failure: the analysis IS uploaded by this point, and
        # deleting it is not something this script should do on its own. But the
        # numbers on that dashboard were measured against a scope nobody chose,
        # and somebody has to be told before they quote them.
        Write-CiWarning "The uploaded analysis used a different scope than SonarQube.Analysis.xml asks for. Treat its numbers as unexplained until this is resolved."
    }

    Write-Section 'Result'
    Write-CiSuccess "Analysis uploaded. $HostUrl/dashboard?id=$ProjectKey"
    exit 0

} finally {
    Pop-Location
}
