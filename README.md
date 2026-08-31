# Slay, Idle, Repeat

## Mutation testing (Stryker)

Only `SlayIdleRepeat.Core` and `SlayIdleRepeat.Application` are mutated. Stryker mutates
one project per run, so that is two runs and one config each — dropping `project` from a
config puts Stryker in solution mode and mutates all 37 projects instead. Reports land in
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
