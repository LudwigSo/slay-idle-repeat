# CRAP score

CRAP — Change Risk Anti-Patterns — ranks methods by how dangerous they are to
change. It is the one number that combines *how tangled* a method is with *how
much of it a test would catch you breaking*.

```
CRAP(m) = complexity(m)^2 * (1 - coverage(m))^3 + complexity(m)
```

- **100% coverage** → CRAP equals complexity. That is the floor; you cannot get
  below it with tests.
- **0% coverage** → CRAP is `complexity^2 + complexity`.
- The coverage term is *cubed*, so the first tests you add to an untested tangle
  buy far more than the last ones.

## What is measured

**Only `SlayIdleRepeat.Core` and `SlayIdleRepeat.Application`.** Everything else
— adapters, `tools/`, the Godot client — is out of scope on purpose. Those two
are the domain and the use-case layer: the code whose risk is worth ranking, and
the code `23` keeps free of vendor SDKs, so a hotspot in them is always a hotspot
in our own logic.

The allow-list lives in `coverage.runsettings` and is applied **at collection
time**:

```xml
<Include>[SlayIdleRepeat.Core]*,[SlayIdleRepeat.Application]*</Include>
```

Two properties of that line are deliberate:

- It is an **allow-list, not a deny-list**, so a new adapter or tool is out of
  scope the day it is created rather than the day somebody remembers to exclude
  it. The scope cannot drift open by omission.
- It runs **at collection, not in ReportGenerator**, so coverlet never
  instruments the other fifteen assemblies at all. Filtering in the report would
  pay the full instrumentation cost on every run and then discard the results.

Narrowing the scope changed nothing about the two assemblies that remain — Core
was 91.9% and Application 91.4% both before and after, measured — it just stopped
paying for the other fifteen.

## Running it

```bash
pwsh ./scripts/Measure-Crap.ps1
```

That is the whole setup. ReportGenerator is pinned in
`.config/dotnet-tools.json` and the script restores it itself, so a fresh clone
needs no preparatory step and renders with the same version CI does.

Run it **from anywhere** — the script locates the repository from its own path.
It does have to `cd` there internally, because a .NET local tool is found by
walking up from the current directory to a `.config/dotnet-tools.json`; that is
a property of `dotnet`, not a choice.

The report lands in `coverage/` — open `coverage/index.html` and look for
**Risk Hotspots**. `coverage/Summary.txt` is printed to stdout as the run ends.

| Flag | Default | What it does |
|---|---|---|
| `-Solution` | the single root `.sln` | Which solution to test |
| `-SkipTests` | off | Re-render the report from the coverage already in `TestResults/` |
| `-CrapThreshold` | 30 | Minimum CRAP score to appear as a hotspot |
| `-ComplexityThreshold` | 15 | Minimum cyclomatic complexity to appear as a hotspot |
| `-FailOnCrap` | 0 (off) | Exit non-zero if any method scores above this |
| `-ExcludeSuites` | the architecture suite | Test suites that must not run under coverage |
| `-Open` | off | Open the HTML report when done |

Exit codes: `0` success, `1` a `-FailOnCrap` maximum was exceeded, `2` a setup
or precondition failure.

## Reading the number

| CRAP | Severity | Action |
|---|---|---|
| 1–5 | Low | None. |
| 6–15 | Moderate | Consider tests. |
| 16–30 | Elevated | Prioritise coverage. |
| 31–60 | High | Refactor and/or test. |
| 60+ | Critical | Urgent refactor. |

**Coverage cannot save a complex method.** Once complexity reaches the threshold
you are measuring against, the floor is already at or above it — at 100%
coverage CRAP *is* complexity. A method with complexity 31 cannot score below 31
no matter how many tests you write. The only move left is to split it. This is
the property that makes CRAP more useful than a coverage percentage: it tells
you when testing has stopped being the answer.

Conversely a long, dull method with complexity 3 and no tests scores 12 —
"moderate". CRAP is deliberately unimpressed by length.

## An empty Risk Hotspots section is not a broken setup

ReportGenerator **omits the Risk Hotspots section entirely** when nothing crosses
both thresholds. A green run with no hotspot table means the codebase is under
CRAP 30 / complexity 15, which is the outcome you want.

To tell that apart from a plumbing failure, drop the thresholds so that
everything qualifies:

```bash
pwsh ./scripts/Measure-Crap.ps1 -SkipTests -CrapThreshold 1 -ComplexityThreshold 1
```

If the section is still absent *then*, something is genuinely wrong.

## Why the run settings collect two formats

`coverage.runsettings` asks coverlet for `cobertura,opencover`, and **the CRAP
score is computed from the Cobertura files.** That is the opposite of the advice
you will find in most write-ups, so it is worth stating why. Measured on this
repository with ReportGenerator 5.5.11 and coverlet 6.0.4:

| | carries per-method complexity | yields a Crap Score column |
|---|---|---|
| coverlet → **Cobertura** | yes, `complexity="..."` on `<method>` | **yes** — ReportGenerator computes it |
| coverlet → OpenCover | yes, `cyclomaticComplexity="..."` | no |

The usual claim — "Cobertura has no complexity, so you must use OpenCover" — was
true of Cobertura files generally and of older ReportGenerator versions.
ReportGenerator has computed Crap Score from Cobertura since **5.2.1**, and
coverlet has always written per-method `complexity` into it.

OpenCover fails here for a subtler reason: ReportGenerator's OpenCover parser
does not *compute* the Crap Score, it *reads* a `crapScore` attribute that the
real OpenCover tool emits. **Coverlet never writes that attribute.** So an
OpenCover-driven run renders a Risk Hotspots table with Cyclomatic and NPath
columns, no Crap Score column, and exit code 0 — the one number you wanted,
silently missing from an otherwise healthy report.

OpenCover is still collected because it is the only source of the NPath
complexity metric and costs one extra file per suite. Nothing reads it for CRAP.

Because that failure is invisible, `Measure-Crap.ps1` checks the **output**: if
the report contains a populated Risk Hotspots table with no `Crap Score` column,
it exits 2 and says so. Checking the inputs alone would not have caught this.

## The report shows only the top 20

ReportGenerator's HTML Risk Hotspots table is **capped at 20 rows, and the page
says so nowhere.** On this repository at the default thresholds that is 20 rows
out of **553** qualifying methods — so reading the table as "the list of our
risky methods" is off by more than an order of magnitude.

`Measure-Crap.ps1` prints a warning whenever the table comes back full, because
a truncated list that looks complete is worse than no list.

There is no setting to raise the cap. To see past it, raise the thresholds and
work down in tiers:

```bash
pwsh ./scripts/Measure-Crap.ps1 -SkipTests -CrapThreshold 500
```

That shows the worst offenders alone; lower it stepwise as they get fixed. The
cap is also the practical argument against treating this as a backlog to burn
down — use it to find the next thing to fix, not to count how much is left.

## Which suites run, and why one does not

Every suite under `tests/` runs **except the architecture suite**, and that
exclusion is about correctness, not speed.

`SlayIdleRepeat.Architecture.Tests` reads the IL of Core and Application with
Mono.Cecil and NetArchTest. Coverlet instruments those same two assemblies on
disk for the duration of a run. So the rules end up scanning coverlet's injected
tracking code and report violations that are not in the source — ambient time and
randomness, culture-sensitive formatting, types outside a documented namespace.
Measured: **173/173 pass without coverage, exactly 4 fail with it.**

Nothing is lost by skipping it. Those rules *read* assemblies rather than
executing them, so the suite contributes essentially no covered lines — Core and
Application land on the same percentages either way. It still runs, unaffected,
in its own CI job, which is where `23` §6 wants it.

The suites run **concurrently** (up to half your cores). A plain sequential loop
measured 12m04s against 8m41s parallel: `Application.Tests` alone is 8m12s, so it
is the critical path and everything else should run alongside it. Parallelising
is safe because each test project instruments the copies of Core and Application
in its *own* `bin` directory.

Four suites — `AssetManifest`, `AssetPipeline`, `AssetPlaceholders`,
`AssetProvenance` — exercise `tools/` and therefore produce a completely empty
coverage file under this allow-list. The script names them at the end of every
run. They are not skipped by default, because "contributes nothing today" is a
fact about today; pass `-ExcludeSuites` if you want to stop paying for them, and
revisit that when the allow-list widens.

### Timing-budget tests cannot pass under coverage

Two tests in `Core.Tests` assert wall-clock budgets:

- `InMemoryGamePerformanceTests.A_full_inventory_costs_a_command_only_linearly_and_stays_inside_the_budget`
- `CombatSimulatorTests.A_worst_case_1800_tick_fight_simulates_inside_the_budget`

Coverage instrumentation adds overhead to every sequence point, and running
suites concurrently loads the machine further, so these fail intermittently
during a CRAP run. **This is an artefact of measurement, not a regression** — they
pass in a normal `dotnet test`. The script prints a loud warning whenever any
suite fails, so the failure is never silent.

They are not excluded, because they live inside a suite that must run for Core
coverage and carry no `[Trait]` to filter on. The options, none of them free, are
to tag them with a category and filter it, to run them outside the coverage job,
or to accept the warning.

## Things that look wrong and are not

- **`MoveNext`, `<>c__DisplayClass`, `<Foo>g__Local|1_0`.** Async methods,
  iterators, lambdas and local functions are compiled into generated types and
  methods, and that is the name coverage data carries. The hotspot is real; only
  the name is ugly. Do not try to "fix" it.
- **A method you deleted still listed.** `TestResults/` was stale. The script
  deletes it on every run that is not `-SkipTests`, so this only happens when you
  pass that flag.
- **Fewer coverage files than test projects.** The script warns about this
  explicitly. It means a suite emitted nothing and the ranking covers less of the
  codebase than it appears to.
- **No Godot client methods at all.** `SlayIdleRepeat.Client` is outside the
  allow-list, so its scene code never appears. Before the allow-list existed, the
  entire top of the ranking was Godot's generated dispatch —
  `InvokeGodotClassMethod`, `Get`/`SetGodotClassPropertyValue`,
  `RestoreGodotObjectData`, cyclomatic complexity up to 92, written by nobody and
  refactorable by nobody. The `**/*.generated.cs` rule in `coverage.runsettings`
  still guards against that if the allow-list is ever widened over the client;
  note the `-*Generated*` class filter does NOT, because it matches class names
  and those methods sit on ordinary classes like `Game.Scenes.Board`.

## CI

`.github/workflows/ci.yml` runs the script and publishes `coverage/` as an
artifact. `-FailOnCrap` is **deliberately not armed there**. An existing codebase
has hotspots on day one, and a hard gate turned on before anyone has looked at
the list makes the pipeline red for reasons nobody has agreed to yet. Calibrate
against a few reports first, then pick a number that ratchets.
