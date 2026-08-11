# `tuning/experiments/` — sparse override patches

**If you are here because you want to change a number to see what happens: do not edit the file in
`tuning/`. Put the change here instead.** That is what this directory is for, and this README exists
at the point of temptation so that nobody has to have read doc 21 to know it.

---

## The rule

`21_ECONOMY_SIMULATOR_SPEC.md` §3.3, verbatim:

> ```
> --overrides tuning/experiments/cheaper_merges.json
> ```
>
> An override file is a **sparse JSON patch** applied on top of the canonical data at load time.
> Sweeps, experiments and what-ifs all run as overrides, so the canonical data is only ever edited
> when a change is *adopted*. This keeps `git diff` on `game-data` a record of decisions
> rather than a record of attempts.

And `14_TECHNICAL_ARCHITECTURE.md` §6, verbatim:

> Experiments and what-ifs run as **sparse override patches** layered on top of the canonical files
> (`21` §3.3), never as edits to them. This keeps `git diff` on `game-data` a record of
> decisions rather than a record of attempts.

---

## The shape

An override file is keyed by canonical filename, then by the path within that file. It is *sparse* —
it names only what it changes. The example in `21` §3.3:

```json
{
  "forge.json": { "mergeCrownCost": { "S": 4200, "SS": 22000 } },
  "progression.json": { "legendXpExponent": 1.09 }
}
```

Overrides layer, in the order given on the command line:

```
dotnet run --project tools/EconomySim -- --overrides tuning/experiments/cheaper_merges.json
```

---

## The loop (`21` §9.1)

1. Run the simulator.
2. Read `out/economy/{ts}/expectation.md` — the one table.
3. If a band is missed, read `power_decomposition.csv` to find *which* source is off.
4. Change **one** number, as an override in this directory.
5. Re-run.
6. **When satisfied, promote the override into the canonical file and commit it alone.**
7. The CI assertion now protects it.

Step 6 is the only way a number ever reaches `tuning/`. A canonical file edited *during* the search
loses the property that its history reads as a list of decisions — and that history is the only
record of why the economy is shaped the way it is.

---

## What does not belong here

- **Anything a player will ever load.** Overrides are a tooling input. They are never shipped, never
  served from the content endpoint, and never part of a `ContentSnapshot`.
- **A permanent "local settings" file.** If an override has survived more than a few days, it is
  either a decision that should be promoted or an experiment that should be deleted.
