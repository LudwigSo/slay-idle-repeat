# Slay, Idle, Repeat

## Static analysis (SonarQube)

A SonarQube Community Build server runs on the laptop, in its own compose stack, and the
whole solution is analysed by one script. Full walk-through:
[`infra/sonarqube/README.md`](infra/sonarqube/README.md).

```powershell
docker compose -f docker-compose.sonarqube.yml up -d --wait  # http://127.0.0.1:9000
pwsh ./infra/sonarqube/Set-SonarQubeDefaults.ps1             # $env:SONAR_ADMIN_TOKEN
pwsh ./build/ci/Invoke-SonarAnalysis.ps1                     # $env:SONAR_TOKEN
```

The analysis configuration — what is analysed, what is excluded, where coverage comes
from — lives in [`SonarQube.Analysis.xml`](SonarQube.Analysis.xml). There is no
`sonar-project.properties`, and adding one would do nothing: the .NET scanner ignores it.

Deliberately **not** in CI: an analysis needs a `secrets.SONAR_TOKEN`, and
`build/ci/Test-NoCloudCredentials.ps1` fails the build on any `secrets.` expression in any
workflow. That is a decision to take, not a step somebody forgot — the three honest options
are laid out at the end of the SonarQube README.

## Mutation testing (Stryker)

Only `SlayIdleRepeat.Core` and `SlayIdleRepeat.Application` are mutated. Stryker mutates
one project per run, so that is two runs and one config each — dropping `project` from a
config puts Stryker in solution mode and mutates all 36 projects instead. Reports land in
`StrykerOutput/`.

`MSBUILD_EXE_PATH` is required, not a convenience: without it the analysis ends in
"No project found" before a single mutant is created. Set it once per shell:

```powershell
$env:MSBUILD_EXE_PATH = "C:\Program Files\dotnet\sdk\8.0.319\MSBuild.dll"
```

**Full run** — every mutant in the project (hours):

```powershell
dotnet-stryker                                     # SlayIdleRepeat.Core
dotnet-stryker -f stryker-config.application.json  # SlayIdleRepeat.Application
```

**Diff run** — mutates only the files that differ from `main`, uncommitted changes
included; everything else is reported as ignored:

```powershell
dotnet-stryker --since:main
dotnet-stryker -f stryker-config.application.json --since:main
```

`--since` shortens the mutant-testing phase, not the setup: solution analysis, the build,
the full initial test run and coverage capture all still happen, which is ~15 minutes for
Application before the first diff mutant is tested.

Run one at a time — the two runs and any `dotnet build` fight over the same Debug output.

## History

`dfbdebc` removed `game-design/`, `docs/`, `IMPLEMENTATION_TRACKER.md`, `tools/` and all
architecture tests but the dependency rule. Recover from that commit's parent if ever needed.
