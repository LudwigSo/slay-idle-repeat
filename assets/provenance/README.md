# `assets/provenance/` — provenance records for generated assets

`15` §G and `20` §2.1 both require a provenance record for **every** generated asset, and both call
it *"a legal prerequisite, not a formality"*. This directory is the record store. The tooling that
read, wrote and enforced it was `tools/AssetProvenance/` (since removed); the gate ran in CI as
`build/ci/Invoke-ProvenanceGate.ps1`.

## Layout

```
assets/provenance/
  README.md            this file
  tool-licences.json   the 15 §G / 20 §6 licence register — one row per generation tool
  records/             one JSON record per asset id, named {assetId}.json
```

The store is **flat**: one record per asset id, directly in `records/`, never in a subdirectory.
`ProvenanceStore.Load` refuses a subdirectory outright — a record one level down would be invisible
to the gate in both directions, which is a quiet hiding place in a store whose whole premise is that
a missing record is loud.

`records/` is committed **empty** (it holds only a `.gitkeep`). That is the honest state today:
M8-09's register has 974 art + 106 audio slots and **zero assets are delivered**, because every
generating task in M8 is capability-blocked. The directory exists so that its *absence* is a defect
rather than a state — `ProvenanceStore.Load` fails loudly on a missing store rather than reporting
an empty one.

## 🔒 Why it is here and not under `game-data/`

**Ruling A8, M8 2026-08-12.** `LocalFileContentSource` recursively enumerates every `*.json` under
`game-data/` into the `ContentSnapshot`, and therefore into `ContentHashing.Compute`. Provenance
records grow by one per generated asset, so a store under `game-data/` would move the content
version stamp — which `14` §6 makes load-bearing for replay and `CONTENT_VERSION_MISMATCH` — 1,048
times over the life of the pipeline. `assets/` is the art/audio *production* area; `game-data/` is
*runtime game content*. `ProvenanceLayoutTests` holds the separation mechanically, and also checks
that no schema pairing has quietly pulled the store back in.

## The record format

Three kinds, discriminated by `kind`, because `15` §B0's field set fits exactly one of them.

| `kind` | Required members |
|---|---|
| `midjourney` | `jobId`, `prompt`, `seed`, `sref`, `aspectRatio`, `style`, `stylize`, `modelVersion`, `date` (`15` §B0) |
| `procedural` | `generator`, `repoCommit` (full 40-hex), `parameters` (object of scalars) |
| `cc0` | `source`, `url`, `licence`, `dateRetrieved` (`20` §2) |

Every record also carries `assetId`, which must equal its file stem and must be an id in
`game-data/assets/asset_manifest_{art,audio}.json`.

A record for an **audio** asset additionally carries `tool` and `toolVersion` (`20` §2.1). A record
for an **art** asset must *not*: its tool is named by its `kind`, and a second tool field would be a
second source of truth for one fact.

The tool that printed the shape (`tools/AssetProvenance`) has been removed.

## What the gate enforces

Both directions, because M0-10 established that orphan checking in this repository runs both ways
and a one-directional check is how an orphan hides:

- **no delivered asset without a provenance record** — every asset file under `assets/` (excluding
  this directory) must have one;
- **no provenance record for an unknown asset id** — every record must name a slot the register
  holds;
- and a record for an asset a ruling has **cut** is its own failure, not lumped in with "unknown".
  The O8 ruling cut all 32 of `15` §E19's sprite sheets; VFX became procedural in-engine work.

Plus: the record's own fields, the register floor that keeps the whole check non-vacuous, and
`20` §6's *"commercial licence for each tool confirmed in writing"*.

### 🔒 Generated and placeholder output goes to `artifacts/`, never to `assets/`

The delivery scan reads the **filesystem**, not the git index, so anything a tool writes under
`assets/` is a delivery as far as the gate is concerned — including an uncommitted local run.
M8-10's placeholder set is a build artifact and must land under `artifacts/` (which `.gitignore`
already excludes), for the same reason the tracker says it is never committed: it must not enter the
Godot checkout, and it must not make every developer who runs it stare at a red provenance gate.

Do **not** widen the scan's exclusion list to make room for a generator's output directory. The scan
excludes exactly one directory — this one — and every exclusion added beside it is a place a real
delivered asset can sit with no provenance record and a green build.

## ⛔ `tool-licences.json` is M8-01b's, and the product owner owns it

`confirmedInWriting` is **absent** — unset — on every row, and nothing in the tooling defaults it.
No agent may set it. The first delivered Midjourney asset turns the unconfirmed state into a red
build, which is exactly what `15` §G asks for.
