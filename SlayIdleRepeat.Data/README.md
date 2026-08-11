# `SlayIdleRepeat.Data`

The game's data layer: every tunable number, every content definition, every schema, every
user-facing string. Shared verbatim by the server (the source of truth), the client (a copy, for
prediction and display) and the economy simulator.

This is not a resources folder. It is the surface the whole economy is tuned through, and it has
rules. They are below.

---

## The one rule this directory exists to enforce

`14_TECHNICAL_ARCHITECTURE.md` §6, verbatim:

> Every number marked 📐 TUNABLE lives in `SlayIdleRepeat.Data/*.json`, never in code.
>
> 🔒 **Every economy-affecting tunable lives specifically in `SlayIdleRepeat.Data/tuning/`** — a flat
> directory of 14 files, catalogued in `21` §3.1. A 📐 number outside that directory is a bug, and a
> build-time check enumerates every 📐 marker in the documentation set against the schema keys and
> **fails on a mismatch**.

**A 📐 number outside `tuning/` is a bug.** Not a style preference — a bug, and one the build is
meant to catch. The reason is in `21` §3.2: a number in code cannot be swept by the economy
simulator, and a number that cannot be swept will never be tuned. Eighteen months of small,
reasonable exceptions is how a tuning surface stops existing.

> **Count discrepancy, flagged for a doc fix:** `14` §6 says "a flat directory of 14 files" but
> defers to the catalogue in `21` §3.1, which lists **16**. Sixteen is implemented, per the M0
> kickoff ruling. `14` §6's count is stale prose.

---

## Layout

```
SlayIdleRepeat.Data/
├── tuning/            # the 16 canonical tunable files (21 §3.1). Economy lives here.
│   └── experiments/   # sparse override patches. Never edit tuning/ to run an experiment.
├── content/           # what the game is made of: chapters, enemies, perks, gear, …
├── schema/            # JSON Schema (draft 2020-12) for everything above
└── loc/               # en.json + de.json. Every user-facing string, from day one.
```

### `tuning/` — numbers that change the economy

The exact catalogue, from `21` §3.1. **Exactly these sixteen — a seventeenth file, or a missing one,
is a bug.**

| File | Owner doc |
|---|---|
| `power_model.json` | `29` §2-3 — formula weights, reference opponent |
| `calibration_builds.json` | `29` §2.5 — reference par build, standard dummy, archetype loadouts |
| `par_power.json` | `29` §4-5 — content par, level-expectation factor curves |
| `expected_progression.json` | `29` §6 — the product owner's intent, per profile per day |
| `sim_profiles.json` | `21` §5.4 — the 14 behavioural profiles |
| `currencies.json` | income and sink rates per source |
| `progression.json` | Legend XP curve, energy, catch-up/frontier curve |
| `drops.json` | rarity tables per chapter band, affix pools, quality range |
| `luck.json` | `24` — every pity N, soft-pity slope, source-class map |
| `forge.json` | merge costs, enhance rates and costs, reforge/retune costs |
| `beasts.json` | pet/mount level costs, egg and crate odds |
| `dungeons.json` | `25` — tier yields, entry caps, energy cost |
| `events.json` | `26` — earn rates, track thresholds, shop prices |
| `guilds.json` | `27` — quest targets, boss HP, perk values |
| `ads.json` | `12` — placement caps, bundle scaling |
| `sim_thresholds.json` | `21` §11 — the assertion thresholds themselves |

### `content/` — what the game is made of

Content is *identity and structure*: which enemies a chapter contains, what a perk does, which boss
sits at the end. It is authored per milestone (M2/M3/M11), and every directory here is currently a
`.gitkeep` waiting for its owner.

| Directory | Owner doc |
|---|---|
| `chapters/` | `14` §6 (schema example), `19` |
| `enemies/` | `05`, `19` |
| `elites/` | `03`, `19` |
| `bosses/` | `17` |
| `perks/` | `06` |
| `gear/` | `08` §7 |
| `pets/` | `07` §2.3 |
| `mounts/` | `07` §3.2 |
| `talents/` | `09` |
| `board_events/` | `19` Part A — the 30 in-run event cards (`EVT_WELL`, …) |
| `curses/` | `19` Part E |
| `quests/` | `19` Part B — the 20 daily quests |
| `modifiers/` | `19` Part C — the 14 weekly modifiers |
| `liveops_events/` | `26` — live-ops event packages (`EVT_EMBERFALL`, …) |
| `feats/` | `28` Part D |

> `board_events/` and `liveops_events/` are two different things that the design docs both call
> "events". The first is a tile you land on mid-run; the second is a two-week live-ops package. They
> are kept in separate directories, and their schemas are named `event.schema.json` (live-ops, the
> path `26` §2 names) and — when authored — a distinct board-event schema.

### `content/` vs `tuning/` — where does a number go?

The test is: **would the economy simulator want to sweep it?**

- A chapter's `powerTarget`, a boss's HP, a perk's magnitude → those shape the *economy*, and the
  ones the simulator grades against live in `tuning/`. A content file may hold a number, but only
  where that number is the identity of the thing (a chapter's stage lengths, an enemy's archetype
  weight), never where it is an economic dial.
- Anything carrying a 📐 marker in the design docs → `tuning/`. No exceptions, by the rule above.

If you are unsure, it goes in `tuning/`. The failure mode this directory guards against is numbers
leaking *out* of `tuning/`, never numbers being over-collected into it.

### `schema/` — what makes the build fail

`14` §6: *"JSON is validated at build time against schemas in `SlayIdleRepeat.Data/schema/`. The
build fails on unknown IDs, missing icons, out-of-range values, orphaned references or duplicate
IDs."*

The schemas are deliberately strict — `"additionalProperties": false` everywhere, required keys,
enumerated ID patterns, numeric bounds wherever a design doc states one, `uniqueItems` on every
collection with IDs. **A permissive schema is worse than no schema, because it manufactures
confidence.** They encode locked design rules as `const` where the docs lock them: no ad reward may
be uncapped (`12` §1), no ad placement may advance a pity counter (`12` §4.2), no dungeon may
advance a `24` counter (`25` §4.5), no guild perk may affect combat (`27` R1), no currency may be
purchasable (`10` §1).

Naming:

- `schema/<name>.schema.json` for each of the 16 `tuning/<name>.json` files — **1:1, no orphans on
  either side.**
- `schema/chapter.schema.json`, `schema/event.schema.json` — content types. `event.schema.json` is
  the path `26` §2 names, and validates one live-ops event *package*; it is not
  `events.schema.json`, which validates the framework-wide tuning file. The one-letter difference is
  load-bearing.
- `schema/loc.schema.json` — the locale files.

🔒 **`tuning/` pairs by file; `content/` pairs by *directory*.** A tuning file is one schema's only
subject, so the stem rule (`tuning/forge.json` → `schema/forge.schema.json`) holds. A content
*directory* holds many files of one **type** — `content/chapters/CH_01_EMBERFALL.json`,
`CH_02_….json`, … — and all of them are governed by that type's single schema. The pairing is a
**declared table**, `ContentLayout.ContentTypeSchemas`, not a stemming rule: `liveops_events/` →
`event.schema.json` is not derivable from either name, and guessing it is exactly the one-letter
mistake the paragraph above warns about.

| Content directory | Schema |
|---|---|
| `content/chapters/` | `schema/chapter.schema.json` |
| `content/liveops_events/` | `schema/event.schema.json` |

The other thirteen directories have no schema yet. Their first file therefore fails the build with
`MissingSchema` — deliberately. Authoring a content type means authoring its schema **and** adding
its row to that table, in the same commit.

### `loc/` — every user-facing string, from day one

Key convention, following `14` §6 (`loc.chapter.5.name`) and `26` §2 (`loc.event.emberfall.name`):

```
loc.<domain>.<id>.<field>
```

lowercase, dot-separated, snake_case segments. `en.json` and `de.json` must carry an **identical key
set** — a key in one and not the other is a string that will render as its own key in front of a
player.

🔒 **Nothing ships machine-translated** (`16` D20, X-04). German values that no human has translated
carry the sentinel `##TODO_DE##` followed by the EN source, so that a validation rule can find every
hole and a translator has the context. **A build that ships to players must fail while any sentinel
remains.** Every DE value is currently a sentinel.

DE runs roughly 30% longer than EN, and that must be tested in tight widgets — which cannot happen
until real German strings exist.

---

## Conventions

- **UTF-8, no BOM. LF line endings**, pinned by `.gitattributes` in this directory so no editor or
  `core.autocrlf` setting can turn a one-line change into a whole-file diff.
- **2-space indent.** Keys in a deliberate, stable order — meta keys (`$schema`, `_doc`, `_status`)
  first, then payload in the order the owning design-doc section presents it. Not alphabetical:
  matching the doc's order is what makes a file reviewable against its source.
- **`$schema`** on every data file, as a relative path to its schema.
- **`_doc`** on every file and on every non-obvious block: the design-doc section that owns those
  numbers. A number nobody can trace to a section is a number nobody will defend.
- **`_status`**: `transcribed` (every value comes from a design doc), `partial` (some blocks are
  authored, some are unauthorable), `skeleton` (typed, no values yet).
- 🔒 **`null` means "the design docs do not authorise a value here."** It is never a legitimate
  runtime value. This is deliberate: a hole that is `null` is greppable, and a hole filled with a
  plausible-looking number is invisible. Where a doc says NEEDS DETAIL, or gives only a formula's
  shape without its constant, the value is `null` and the `_doc` says why.

---

## Every number here is a placeholder

`16_DECISION_LOG.md` R6, verbatim:

> **Every economy number is unvalidated.** The pacing targets, drop rates, costs and curves
> throughout these documents are genre-informed estimates that have never been tested against each
> other. […] Until it has run, treat all 📐 TUNABLE numbers as placeholders that look precise.

Everything in `tuning/` is transcribed from the design docs. Nothing in it has been validated
against anything else. `10` §9 puts it plainly: *"all economy numbers in this document are informed
guesses — including the ones that look precise."*

The economy simulator (`21`) is what turns them into real numbers, and `21` §9.5 gives the ordering
rule for doing it: **run with dungeons, events and guilds all enabled together before tuning any of
them individually.** Each was sized in isolation, all three pay the same materials, and guild perks
multiply the other two. Tuning them one at a time converges on the wrong answer three times.

---

## Changing something

| You want to… | Do this |
|---|---|
| Try a number out | Write a sparse override in `tuning/experiments/`. See its README. |
| Adopt a number | Promote the override into the canonical file, **in a commit of its own**. |
| Change what the game *should* achieve | Edit `expected_progression.json`. That is a product decision (`29` §6) and belongs in its own commit too. |
| Add a new tunable | Add it to the right one of the 16 files and to its schema. Never a 17th file. |
| Add a user-facing string | Add the key to `en.json` **and** `de.json` in the same commit. |
| Add a new grant source | Assign it a source class in `luck.json`. `24` §3: the validator fails the build if a grant source has no class. |
