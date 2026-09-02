# Slay, Idle, Repeat

## Verification — one command

The three quality measurements documented below — SonarQube static analysis, the CRAP
score and Stryker mutation testing — run as one command that aggregates their results into
a single summary:

```powershell
pwsh ./scripts/Invoke-Verification.ps1
```

It needs `$env:SONAR_TOKEN` and a running SonarQube server for its first stage; see
[Static analysis](#static-analysis-sonarqube) below, or drop that stage with
`-Stages Crap,Mutation`.

The script measures nothing itself. It runs the same scripts the sections below document
and reads every number back out of the artifact the owning tool produced: the SonarQube web
API, `coverage/Summary.json` plus the Risk Hotspots table rendered into
`coverage/index.html`, and each Stryker run's `mutation-report.json`. A second
implementation of any of those metrics would be a second thing to be wrong.

What it adds over running the three by hand:

- **Ordering.** The stages fight over the same build output on disk — Sonar builds Release
  inside a scanner session, CRAP builds Debug, Stryker builds Debug and then rewrites
  assemblies per mutant. They run strictly one after another for that reason, not for
  tidiness.
- **One preflight for all three, before the first build.** A full run is roughly half an
  hour before Stryker even starts, so an unset `SONAR_TOKEN` or an unresolvable
  `MSBUILD_EXE_PATH` is worth learning in the first two seconds. A stage whose
  preconditions fail is reported `BLOCKED`, the others still run, and the exit code says
  the verification was incomplete.
- **`MSBUILD_EXE_PATH` is derived** from the SDK `global.json` actually selects, so the
  literal path in the Stryker section below need not be kept current.
- **The solution-mode tripwire.** A `stryker-config*.json` with no `project` key is
  refused up front instead of quietly mutating all 36 projects.

Results land in `artifacts/verification/` (gitignored):

| | |
|:---|:---|
| `summary.md` | the whole run, readable — per-stage status, headline numbers, the riskiest methods by CRAP score, the files with the most unkilled mutants |
| `summary.json` | the same, for comparing two runs |
| `logs/*.log` | each stage's full output |

Exit codes: **0** everything ran and no gate tripped · **1** everything ran and at least
one gate is red · **2** a requested stage could not run, so the verification is
*incomplete* — which outranks a red gate in the exit code, because an unknown is worse than
a known bad. Both are listed in the summary either way.

Mutation testing defaults to **diff mode against `main`**, because a full run is hours: the
score it reports is about the changed files, and the summary says so. Useful variants:

```powershell
pwsh ./scripts/Invoke-Verification.ps1 -Stages Crap,Mutation   # no SonarQube server to hand
pwsh ./scripts/Invoke-Verification.ps1 -ReuseCoverageForCrap   # ~12 min faster, caveat in -?
pwsh ./scripts/Invoke-Verification.ps1 -FullMutation           # every mutant, hours
pwsh ./scripts/Invoke-Verification.ps1 -Open                   # open the HTML reports after
```

`Get-Help ./scripts/Invoke-Verification.ps1 -Full` documents every switch and why its
default is what it is.

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

## CRAP score

`CRAP(m) = complexity(m)² × (1 − coverage(m))³ + complexity(m)` — cyclomatic complexity
weighted by how untested the method is. ReportGenerator computes it in its Risk Hotspots
section; nothing here recalculates it.

```powershell
pwsh ./scripts/Measure-Crap.ps1                                        # report in coverage/
pwsh ./scripts/Measure-Crap.ps1 -SkipTests -CrapThreshold 1 -ComplexityThreshold 1
```

Only `SlayIdleRepeat.Core` and `SlayIdleRepeat.Application` are measured, and that
allow-list is applied at *collection* time in
[`coverage.runsettings`](coverage.runsettings) — the other assemblies are never
instrumented, rather than instrumented and then filtered out of the report.

The score is computed from the **Cobertura** files and not the OpenCover ones. That is the
opposite of the usual advice and it was measured here: coverlet's Cobertura carries the
per-method `complexity` ReportGenerator computes from, while its OpenCover output omits the
`crapScore` attribute ReportGenerator's OpenCover parser reads — so an OpenCover-driven run
renders a Risk Hotspots table with no Crap Score column and exits 0. The script asserts
both the input and the rendered output for that reason.

An absent Risk Hotspots section on a green run means nothing crossed the thresholds, not
that the setup is broken. The second command above proves the plumbing by making everything
a hotspot.

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

(`scripts/Invoke-Verification.ps1` derives this from `dotnet --info` instead, so the
verification run does not depend on that literal being current.)

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
