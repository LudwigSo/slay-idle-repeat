# SonarQube — static analysis on a laptop

Continuous inspection of the C# in this repository, running against a SonarQube
server you start with `docker compose` and throw away whenever you like. No
cloud account, no SonarCloud organisation, nothing to sign up for.

```bash
docker compose -f docker-compose.sonarqube.yml up -d --wait
```

Then <http://127.0.0.1:9000>.

---

## The five files

| File | What it decides |
|---|---|
| [`docker-compose.sonarqube.yml`](../../docker-compose.sonarqube.yml) | The server: image, database, memory, ports. |
| [`SonarQube.Analysis.xml`](../../SonarQube.Analysis.xml) | **The analysis configuration.** What is analysed, what is excluded, where coverage comes from. |
| [`build/ci/Invoke-SonarAnalysis.ps1`](../../build/ci/Invoke-SonarAnalysis.ps1) | The run: begin → build → test → end, plus the preconditions worth checking before a full build. |
| [`Set-SonarQubeDefaults.ps1`](Set-SonarQubeDefaults.ps1) | The server-side half: the project, the quality gate, the new-code period. |
| [`.config/dotnet-tools.json`](../../.config/dotnet-tools.json) | Pins the scanner version, so a laptop and CI analyse identically. |

> 🔴 **There is no `sonar-project.properties`, and adding one would do nothing.**
> SonarScanner for .NET ignores that file entirely — it reads MSBuild's view of
> the solution instead. Project-wide settings go in `SonarQube.Analysis.xml`.

---

## First run, from nothing

**1 — start the server.** First boot migrates a database and builds an
Elasticsearch index, so `--wait` sits on the healthcheck for a minute or two.
That is the healthcheck working.

```bash
docker compose -f docker-compose.sonarqube.yml up -d --wait
```

**2 — set the admin password.** SonarQube starts as `admin` / `admin` and forces
a change at first login. Do it in the UI at <http://127.0.0.1:9000>.

**3 — make two tokens** at *My Account → Security*. Two, not one, and the
distinction is the point:

| Token | Type | Used by | Can |
|---|---|---|---|
| `local-analysis` | **Global Analysis Token** | `Invoke-SonarAnalysis.ps1` | Submit an analysis. Nothing else. |
| `local-admin` | **User Token** | `Set-SonarQubeDefaults.ps1` | Administer the server. |

**4 — apply the configuration.**

```bash
pwsh ./infra/sonarqube/Set-SonarQubeDefaults.ps1
```

Reads `SONAR_ADMIN_TOKEN`. Creates the project, creates the `Slay Idle Repeat
way` quality gate with the *Sonar way* conditions, attaches it, and sets new code
to the last 30 days. Idempotent — run it again any time.

**5 — analyse.**

```bash
pwsh ./build/ci/Invoke-SonarAnalysis.ps1
```

Reads `SONAR_TOKEN`. Takes a full Release build plus the five instrumented test
suites, so budget a few minutes. Then read the report at
<http://127.0.0.1:9000/dashboard?id=slay-idle-repeat>.

---

## What it analyses, and what it does not

Scope lives in [`SonarQube.Analysis.xml`](../../SonarQube.Analysis.xml), which
explains every entry. In summary:

* **Analysed:** every C# project in `SlayIdleRepeat.sln` — domain, application,
  contracts, server, client, all sixteen adapters, and the test suites. Plus
  everything the Java engine picks up beside the solution: the compose files, the
  Dockerfile, `game-data/`'s JSON, the Blender scripts under `assets/`.
* **Not analysed at all:** generated C# (`*.generated.cs` — Godot's
  `InvokeGodotClassMethod` dispatch and friends), `bin/`, `obj/`, `.godot/`,
  `spikes/`, and our own outputs.
* **Analysed but not measured for coverage:** the adapters, the client, the
  server, the contracts, the tests. Not a judgement about their quality — see
  below.

### The coverage coupling

[`coverage.runsettings`](../../coverage.runsettings) instruments
**`SlayIdleRepeat.Core` and `SlayIdleRepeat.Application` only**, by allow-list,
at collection time. Every other assembly produces no coverage data — not 0%, but
nothing at all.

Handed to SonarQube unqualified, that reads as "0% covered" across sixteen
assemblies and produces a project coverage figure that measures the allow-list
rather than the tests. So `sonar.coverage.exclusions` mirrors the allow-list
exactly, and `Invoke-SonarAnalysis.ps1` **refuses to run** if the two ever stop
agreeing. Widening one is a deliberate act; the tripwire makes it a deliberate
act in both files.

The same file's `<Format>` must keep emitting `opencover`. SonarQube's C# plugin
has no Cobertura importer — `sonar.cs.opencover.reportsPaths` is the only
supported path for coverlet output. This is the exact inverse of the choice
`coverage.runsettings` documents for the CRAP score, which needs Cobertura and
ignores the OpenCover twin. Both formats, two consumers, one run. The tripwire
checks this too.

---

## The baseline, and what to calibrate first

First full analysis, `2c0e312`, scanner 11.2.1 against SonarQube 26.8.0:

| | |
|---|---|
| Files / lines of code | 670 / 56,298 |
| Coverage | **89.4 %** (Core + Application — the measured scope) |
| Duplication | 0.1 % |
| Issues | 831 — 814 maintainability, 16 reliability, 1 security |
| Security hotspots | 0 |
| Technical debt | 2,258 min, ratio 0.1 % |
| Tests imported | 9,243, 1 failing |

Two things about those numbers before anyone quotes them:

* **The quality gate went green on nothing.** A first analysis establishes the
  baseline, so there is no new code yet and every condition is unevaluated. The
  gate becomes meaningful on the *second* run.
* **The one failing test is an artefact of the coverage run.**
  `MetaLoopTests.The_whole_meta_loop_stays_inside_the_unit_tier_budget` is a
  wall-clock budget (750 ms) and came in at 1004 ms under coverlet
  instrumentation. Same reason `scripts/Measure-Crap.ps1` exists to be read
  rather than gated on.

**Nothing in the quality profile has been changed**, deliberately — see the 🔴 in
`Set-SonarQubeDefaults.ps1`. But three rules dominate the noise and are the
obvious first calibration, once somebody has read the report:

| Rule | Count | Why it may not apply here |
|---|---|---|
| `csharpsquid:S1244` — no floating-point equality | 11 of the 16 reliability issues | Every one is in `SlayIdleRepeat.Core`, whose whole contract is bit-exact determinism (`14` §8.2). Exact float comparison is the point, not an oversight. |
| `external_roslyn:CA1861` / `CA1859` | 406 of the 831 issues | Hidden-severity .NET analyzer suggestions. They never appear as build warnings, but the SDK writes them to the SARIF log the scanner reads, so they arrive as INFO issues and swamp the count. |
| `text:S6389` — bidirectional character | 1 | In `HeroNameRuleTests`. The test is *about* hostile name input; the character is the fixture. |

---

## Two things the Community Build cannot do

**No branch or pull-request analysis.** Every run lands on the project's single
`main` branch whatever branch your working tree is on, and passing
`sonar.branch.name` fails the run outright. On a feature branch the honest
reading of a result is *"this is what the project would look like if this branch
were main"*. Branch analysis starts at Developer Edition.

**No incremental analysis.** The scanner sees a project through the compiler
actually running on it, so `Invoke-SonarAnalysis.ps1` builds with
`--no-incremental`. An up-to-date project MSBuild skips emits no diagnostics and
lands in SonarQube with zero issues and zero lines — indistinguishable from clean
code, and exactly what a second run in the same tree would otherwise produce.

---

## `TreatWarningsAsErrors` needs no special handling — the scanner does it

`begin` injects several hundred SonarAnalyzer rules into the compilation **as
warnings**, and `Directory.Build.props` sets `TreatWarningsAsErrors=true` for the
whole repository. That reads like a build that dies on the first S-rule finding
with `end` never running.

It does not. The scanner's own injected targets carry, immediately before
`CoreCompile`:

```xml
<!-- Make sure no warnings are treated as errors -->
<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
```

Measured on scanner 11.2.1: a build of `SlayIdleRepeat.Core` inside a scanner
session produced 151 warnings and 0 errors while the property still evaluated to
`true`. So `Invoke-SonarAnalysis.ps1` passes nothing about it — an earlier draft
did, and a global `-p:` would have weakened the rule across every project to fix
a problem that does not exist.

---

## Not in CI, and the reason is a rule this repository already has

There is no SonarQube job in [`ci.yml`](../../.github/workflows/ci.yml), and
adding one is a decision somebody has to take rather than a step somebody
forgot.

Analysis needs `${{ secrets.SONAR_TOKEN }}` and a reachable server.
[`build/ci/Test-NoCloudCredentials.ps1`](../../build/ci/Test-NoCloudCredentials.ps1)
**fails the build on any `secrets.` expression in any workflow** — that is `14`
§1.1's portability rule made mechanical, and its own header is explicit that the
exclusion list is "not a way to quiet a finding in a file that is [CI]".

A SonarQube job is CI. So the choice is a real one, with three honest answers:

1. **Leave it local.** Analysis is a thing you run before opening a change, like
   `scripts/Measure-Crap.ps1`. Costs nothing, gates nothing.
2. **Self-hosted SonarQube reachable from the runner**, and widen
   `Test-NoCloudCredentials.ps1` to permit exactly one workflow to hold exactly
   one token — as a deliberate, documented amendment to the rule.
3. **SonarQube Cloud** (free for public repositories), which trades the rule
   away entirely: a cloud account, an organisation, and a token in CI.

Until then, `Invoke-SonarAnalysis.ps1` is written the way every other check in
`build/ci/` is — one command, same arguments on a laptop and on a runner — so
whichever answer wins, the job is this much YAML around a script that already
exists:

```yaml
  # ⚠️ NOT COMMITTED. Adding this file reds `no cloud credentials (14 §1.1)`
  # until that rule is deliberately amended. See above.
  sonarqube:
    name: static analysis
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0        # sonar.scm.provider=git needs the full history
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - uses: actions/setup-java@v4
        with:
          distribution: temurin
          java-version: '17'    # the scanner's `end` step runs a Java engine
      - name: Analyse
        shell: pwsh
        env:
          SONAR_HOST_URL: ${{ vars.SONAR_HOST_URL }}
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
        run: ./build/ci/Invoke-SonarAnalysis.ps1
```

Add `-WaitForQualityGate` when — and only when — somebody has read a few reports
and agreed to the thresholds.

---

## Running it

```bash
# Start / stop
docker compose -f docker-compose.sonarqube.yml up -d --wait
docker compose -f docker-compose.sonarqube.yml down        # keep the history
docker compose -f docker-compose.sonarqube.yml down -v     # forget everything

# Logs, when a boot fails
docker compose -f docker-compose.sonarqube.yml logs sonarqube

# Analyse
pwsh ./build/ci/Invoke-SonarAnalysis.ps1

# Analyse and fail on a red gate — what a CI job would run
pwsh ./build/ci/Invoke-SonarAnalysis.ps1 -WaitForQualityGate
```

Host ports are overridable exactly like the main stack's, via
[`.env.example`](../../.env.example): `SIR_SONARQUBE_PORT` (9000) and
`SIR_SONARQUBE_DB_PORT` (5433, deliberately not the main stack's 5432).

### Footprint

`docker stats --no-stream`, idle, after a cold `up -d --wait` on the development
machine:

| Service | idle | `mem_limit` |
|---|---|---|
| sonarqube | 2.60 GiB | 4 GiB |
| sonarqube-db | 146 MiB | 512 MiB |
| sonarqube-sysctl | — | 32 MiB (exits immediately) |

**~2.75 GiB idle**, against ~450 MiB for the entire product stack. That ratio is
the whole argument for keeping this in a separate file: it is five times the
game's backend to run a tool that reads the repository. The image is ~1.1 GB on
disk.

Note how little headroom the 4 GiB ceiling is: Elasticsearch reserves its
`-Xms768m` up front and the three JVMs together claim ~2.5 GB before any work
happens. A 3 GiB limit looks fine until the compute engine processes a report.

### It is a separate stack

`docker-compose.sonarqube.yml` has its own compose project name, so a
`docker compose down -v` in either stack cannot reach the other's volumes, and
SonarQube's ~3 GB does not load every `docker compose up` a developer runs to
work on the game. Compose does not auto-load a file named like this — the `-f` is
mandatory.

### If it will not start

`docker compose logs sonarqube` and look for Elasticsearch. The container needs
`vm.max_map_count >= 524288` in the Docker VM, which on Docker Desktop ships at
262144 — half. The one-shot `sonarqube-sysctl` service raises it on every `up`,
because the setting belongs to the Docker VM's kernel and resets when that VM
restarts. Check it with:

```bash
docker run --rm busybox sysctl vm.max_map_count
```

---

## Upgrading

Community Build is a **rolling release** — monthly, with no LTA. Upgrading is a
deliberate act:

1. Bump the tag **and** the digest in `docker-compose.sonarqube.yml`, together.
2. `up -d --wait` once and let it migrate its own database.
3. Do not skip more than one major at a time, and never point an older image at a
   migrated volume — the analysis history is the only thing here worth keeping.

The scanner version in `.config/dotnet-tools.json` moves independently. Bump it
when the server complains about it, not on a schedule.
