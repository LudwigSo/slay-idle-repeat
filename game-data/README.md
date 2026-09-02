# `game-data`

The game's data layer: every tunable number, every content definition, every schema, every
user-facing string. Shared verbatim by the server (the source of truth), the client (a copy, for
prediction and display) and the economy simulator.

This is not a resources folder. It is the surface the whole economy is tuned through, and it has
rules. They are below.

---

## The one rule this directory exists to enforce

`14_TECHNICAL_ARCHITECTURE.md` §6, verbatim:

> Every number marked 📐 TUNABLE lives in `game-data/*.json`, never in code.
>
> 🔒 **Every economy-affecting tunable lives specifically in `game-data/tuning/`** — a flat
> directory of 14 files, catalogued in `21` §3.1. A 📐 number outside that directory is a bug, and a
> build-time check enumerates every 📐 marker in the documentation set against the schema keys and
> **fails on a mismatch**.

> ⚠️ **That build-time check no longer exists.** It was built (M0-09) and has since been removed
> along with its dated baseline, so the rule above is still the rule and nothing enforces it: a
> 📐 number that loses its schema key, or a schema key that loses its marker, now passes CI in
> silence. Everything this file says about where numbers go is convention until it is rebuilt.

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
game-data/
├── tuning/            # the 16 canonical tunable files (21 §3.1). Economy lives here.
│   └── experiments/   # sparse override patches. Never edit tuning/ to run an experiment.
├── content/           # what the game is made of: chapters, enemies, perks, gear, …
├── assets/            # the art & audio asset-slot register (15 §E, 20 §3-§4). Not tunables.
├── schema/            # JSON Schema (draft 2020-12) for everything above
└── loc/               # en.json + de.json. Every user-facing string, from day one.
```

### `assets/` — the asset-slot register

`asset_manifest_art.json` and `asset_manifest_audio.json` (M8-09) transcribe the manifests in
`15_ART_DIRECTION_AND_ASSET_MANIFEST.md` §C–§E21 and `20_AUDIO_MANIFEST.md` §3–§5 into one row per
asset slot. They are a **production-pipeline register**, not game content and not tunables: no game
rule reads them. Their consumers are the M8 asset tasks — provenance records keyed to an asset id,
the post-processing pipeline (delivery size, pivot, atlas) and placeholder generation.

They pair by the stem rule like any other non-`content/` file (`assets/X.json` →
`schema/X.schema.json`) and are therefore validated by the content loader in
`src/SlayIdleRepeat.Application/Services/Content/`. Two
conventions here differ from `tuning/`, deliberately:

- 🔒 **Counts are transcribed, never reconciled.** Where a row count disagrees with the design
  doc's own stated total, the disagreement is recorded in the file's `discrepancies` block rather
  than fixed — `15` §E20 claims 50 misc UI icons and lists 49. Reconciling the manifest totals is
  task **M11-01**'s job, and a quiet fix here destroys the evidence it needs.
- 🔒 **An unauthorised value is an ABSENT member, not `null`.** The rule above ("`null` means the
  design docs do not authorise a value") is stated over *numbers* in `tuning/`, each of which
  somebody could quietly fill with a plausible zero — and
  `RealDataNegativeCaseTests.The_shipped_data_set_still_carries_exactly_its_96_unauthorised_holes`
  pins that population across the whole snapshot. A 974-row register carrying "§C states no pivot
  for a 9-slice panel" as a `null` would add ~3,500 to a population of 96 and destroy the guard. So
  the register omits the member; the typed reader surfaced it as `null`,
  and `AbsentValueTests` pins each absence population the same way.

⚠️ Both files enter the `ContentSnapshot` and so ship to client and server, and every edit to them
moves the content version stamp. That is ~550 KB of the shipped payload for data no rule reads —
see the open item raised against M0-09 in the M8-09 hand-over.

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
sits at the end. It is authored per milestone (M2/M3/M11). M2 filled `enemies/`, `bosses/` and
`statuses.json`; the remaining directories are still `.gitkeep`s waiting for their owner.

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
| `profanity/` | `27` §1 — the EN + DE name-filter word lists (`07` §1's hero name, `27` §1's guild name and tag) |
| `boot/` | `13` S01 — which loc key fills each string slot on the boot screen |

> `board_events/` and `liveops_events/` are two different things that the design docs both call
> "events". The first is a tile you land on mid-run; the second is a two-week live-ops package. They
> are kept in separate directories, and their schemas are named `event.schema.json` (live-ops, the
> path `26` §2 names) and `board_events.schema.json` (the tile, authored by M3-03).

Plus one file that sits **directly** under `content/`, because it is a single document rather than a
type with many instances:

| File | Owner doc |
|---|---|
| `combat_caps.json` | `05` §1–2 — the six stat caps, the hero base stat curve, `wardCapPct` (`05` §4.1), the two mitigation dials (`05` §4) and `pvpMaxFightSeconds` (`11` §4.3) |

🔒 **`combat_caps.json` is deliberately not a seventeenth `tuning/` file.** `05` §1.1 and `11` §4.3
name it `res://data/combat_caps.json`, and `21` §3.1's catalogue — the authority on what `tuning/`
holds — does not list it. It is balance and simulator configuration, not an economic dial the
simulator sweeps. `TunableMarkerAudit.NonEconomyDataFiles` used to record exactly that in code, and
was removed with the 📐 audit — this paragraph is now the only record. It pairs
by **stem** (`content/combat_caps.json` → `schema/combat_caps.schema.json`), not by directory, so it
needs no row in `ContentLayout.ContentTypeSchemas`.

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

`14` §6: *"JSON is validated at build time against schemas in `game-data/schema/`. The
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
| `content/enemies/` | `schema/enemies.schema.json` |
| `content/bosses/` | `schema/bosses.schema.json` |
| `content/board_events/` | `schema/board_events.schema.json` |
| `content/curses/` | `schema/curses.schema.json` |
| `content/perks/` | `schema/perk.schema.json` |
| `content/profanity/` | `schema/profanity.schema.json` |
| `content/boot/` | `schema/boot.schema.json` |

The remaining directories have no schema yet. Their first file therefore fails the build with
`MissingSchema` — deliberately. Authoring a content type means authoring its schema **and** adding
its row to that table, in the same commit.

🔒 **`content/profanity/` is named after its LANGUAGES, not after its type**, so the stem rule would
look for `schema/en.schema.json` and `schema/de.schema.json` and find neither. Its
`ContentTypeSchemas` row is therefore load-bearing rather than merely explicit — the hazard the
paragraph above calls "the easy case to forget", arriving with two files rather than one.

⚠️ **Both files are deliberately a SEED, not a finished list.** `27` §1 states the standard (EN and
DE, at creation and on every edit) and authors no words; M4-10 shipped the matching mechanism and
twelve high-precision terms per language, and **M17** owns the curated lists. The deferral is held by
`ContentCurationRegister` in `SlayIdleRepeat.Application.Tests`, keyed on each file's own `curation`
block, and it fails the moment a file stops declaring itself a seed — including when it has been
curated. Each file's `_doc` records which terms were left out and why.

⚠️ **`content/statuses.json` sits flat and pairs by stem**, against the directory rule above.
`05` §5's twelve statuses are a closed vocabulary authored as one aggregate document, which is the
same shape as `combat_caps.json` — but unlike that file it does hold many instances, so it sits on
the wrong side of the criterion as this README states it. Recorded rather than quietly moved:
relocating it to `content/statuses/` means a `ContentTypeSchemas` row and a path change in
`StatusCatalogue`, which is a content-layout decision rather than a documentation fix.

⚠️ **`content/elites/` and `content/modifiers/` are empty and will stay empty.** `05` §6.2's eight
elite modifiers are authored inside `content/enemies/enemies.json`, because a modifier has no
identity apart from the enemy it is drawn onto.

🔒 **When an effect is embedded as DSL JSON, and when it is a named number.** Both shapes are in
use and the rule is a real one, not a preference — it is stated in the schemas that apply it, and
restated here because that is where an author of a *new* content type will look:

> An effect is authored as embedded `18` §1 DSL JSON **only where a cross-file validation rule walks
> it against `schema/effect.schema.json`.** Otherwise author named parameter numbers and record the
> mapping obligation on the consumer.

`bosses.json` embeds real effects because such a rule exists for it (`bosses.schema.json`). Elite
modifiers and statuses are named numbers because no such rule exists for them, and the validator
resolves same-document pointers only — so a schema here cannot `$ref` the effect vocabulary, and one
that restated it would be a second copy of the op set. Embedded effects in those files would
therefore ship completely unvalidated, which is the one thing the content build exists to prevent.

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
