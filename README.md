# Slay, Idle, Repeat

## Documentation

| | |
|:---|:---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | the code layout — which folder a change belongs in, and why |
| [`docs/game-design.md`](docs/game-design.md) | what the game *is* — pillars, loops, systems |
| [`docs/3d-resources.md`](docs/3d-resources.md) | CC0 sources for models, textures, HDRIs and audio, and the licence care each one needs |

Folder-local `README.md` files stay with the folder they document — [`game-data/`](game-data/README.md),
[`infra/`](infra/README.md), [`assets/provenance/`](assets/provenance/README.md),
[`game-data/tuning/experiments/`](game-data/tuning/experiments/README.md) and
[`.github/workflows/`](.github/workflows/README.md). This file is the only root-level doc, because
it is the one the repository's front page shows.

## Verification — one command

Static analysis runs in the build (see below). The two measured stages — the CRAP score
and Stryker mutation testing — run as one command that aggregates their results into a
single summary:

```powershell
pwsh ./scripts/Invoke-Verification.ps1
```

The script measures nothing itself. It runs the same scripts the sections below document
and reads every number back out of the artifact the owning tool produced:
`coverage/Summary.json` plus the Risk Hotspots table rendered into `coverage/index.html`,
and each Stryker run's `mutation-report.json`. A second implementation of either metric
would be a second thing to be wrong.

What it adds over running the two by hand:

- **Ordering.** The stages fight over the same build output on disk — CRAP builds Debug,
  Stryker builds Debug and then rewrites assemblies per mutant. They run strictly one
  after another for that reason, not for tidiness.
- **One preflight for both, before the first build.** A full run is tens of minutes, so an
  unresolvable `MSBUILD_EXE_PATH` is worth learning in the first two seconds. A stage whose
  preconditions fail is reported `BLOCKED`, the other still runs, and the exit code says
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
score it reports is about the changed files, and the summary says so. On a feature branch,
point it at the branch point instead. Useful variants:

```powershell
pwsh ./scripts/Invoke-Verification.ps1 -Stages Mutation -MutationSince <base ref>
pwsh ./scripts/Invoke-Verification.ps1 -FullMutation           # every mutant, hours
pwsh ./scripts/Invoke-Verification.ps1 -Open                   # open the HTML reports after
```

`Get-Help ./scripts/Invoke-Verification.ps1 -Full` documents every switch and why its
default is what it is.

## Static analysis — in the build, not beside it

There is nothing to run. `SonarAnalyzer.CSharp` is a `GlobalPackageReference` in
[`Directory.Packages.props`](Directory.Packages.props), so Sonar's C# rules run inside the
compiler on every build, on every branch, in every checkout — no server, no token, no scan
step:

```powershell
dotnet build SlayIdleRepeat.sln      # this is the analysis
```

🔴 **A finding is a build error.** `Directory.Build.props` sets `TreatWarningsAsErrors` for
the whole repository and the `S*` rules are not exempted. An analysis whose output has to
be read somewhere else is an analysis nobody reads.

The corollary is that the rule set is load-bearing on the build, so the rules that do not
apply to this codebase are switched off **one at a time, each with its reason**, in
[`.editorconfig`](.editorconfig) — read it before adding a suppression.

The first run over this repository produced **492 findings**. 188 were fixed, 15 carry a
one-line `#pragma` with the reason, and the remaining 289 are covered by 18 rule entries in
`.editorconfig` — because the analyser's default profile disagrees with conventions this
codebase had already settled: prose comments it reads as commented-out code, documentation
constants it reads as dead fields, exact float comparison in a bit-exact-deterministic core,
`res://` paths it reads as URIs, and `ThrowIfNull(item, nameof(collection))` — blaming the
argument the caller actually passed — it reads as a mistake.

⚠️ Three of those findings would have made the code **worse** if followed, which is the
argument for reading a rule before obeying it: dropping a `_ =` discard reintroduced CS4014
(an error here), removing a "dead" store would have made a failed profile-open report itself
as a failed session-open, and collapsing an explicit array argument would have bound two
overloads to the same method and turned a real test into `x == x`.

This replaced a local **SonarQube Community Build** server — a compose stack, an admin
token, a scanner session and 1,600 lines of setup, deleted in `chore/sonar-analyzers` and
recoverable from git history. What went with it: the dashboard, the quality gate, issue
history, duplication density and SonarQube's own coverage view. Coverage and complexity are
measured by the CRAP stage instead. The trade was worth making because the server could
never reach the workflow that needed it — the Community Build has **no branch analysis**, so
every run landed on the project's single `main` branch whatever the working tree was on,
which made a per-branch analysis meaningless and two concurrent ones destructive.

It also settles a standing gap rather than moving it. The server-based analysis was
**deliberately not in CI**, because it needed a `secrets.SONAR_TOKEN` and
`build/ci/Test-NoCloudCredentials.ps1` fails the build on any `secrets.` expression in any
workflow — so the one place every change passes through was the one place the analysis
never ran. The analyser needs no credential, so `.github/workflows/ci.yml` now analyses
every push for free, without a line being added to it.

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
