# 3D & audio resources

Where the art comes from when it is not generated. Every source below is listed because it
clears the same bar: **downloadable without an account, usable commercially, and licensed
permissively enough that a record in `assets/provenance/` can honestly say so.**

That bar is not editorial taste. [`assets/provenance/README.md`](../assets/provenance/README.md)
requires a provenance record for every delivered asset, and one of its three record kinds is
literally `cc0` — `source`, `url`, `licence`, `dateRetrieved`. A source whose licence you cannot
name in one field is a source you cannot deliver from. `15` §G and `20` §2.1 both call this *"a
legal prerequisite, not a formality"*.

🔴 **CC0 is not the same as "free".** Two of the sources below (Scan the World, and parts of
Smithsonian) carry **per-object** licences that are sometimes CC BY or CC BY-NC-SA. Non-commercial
is disqualifying here. Read the licence on the object page, not on the site's front page, and put
what you read into the record's `licence` field verbatim.

## At a glance

| Source | What it is for | Licence | Account |
|:---|:---|:---|:---|
| [Poly Haven](#poly-haven) | HDRIs, PBR textures, scanned props | CC0, site-wide | no |
| [ambientCG](#ambientcg) | PBR materials at volume, decals, terrain | CC0, site-wide | no |
| [Kenney](#kenney) | Low-poly 3D kits, sprites, UI | CC0, site-wide | no |
| [Kenney Audio](#kenney-audio) | SFX, UI blips, jingles, voice | CC0, site-wide | no |
| [Quaternius](#quaternius) | Low-poly packs, many pre-rigged | CC0, site-wide | no |
| [The Base Mesh](#the-base-mesh) | Clean-topology base meshes to build on | CC0, site-wide | no |
| [Smithsonian 3D / Scan the World](#smithsonian-3d--scan-the-world) | Museum and sculpture scans | ⚠️ **per object** | no |

---

## Poly Haven

<https://polyhaven.com> — the best single bookmark in 3D, and the only one on this list that
covers all three of lighting, surfacing and geometry at production quality.

- **HDRIs** (~1,000) in `.hdr` and `.exr`, 1K up to 16K. This is what an environment map for
  `AppRoot`'s `WorldEnvironment` should come from rather than being authored by hand.
- **Textures** — PBR sets with the full channel list (diffuse/albedo, rough, normal — GL and DX —
  displacement, AO, metal where it applies), up to 8K.
- **Models** — photogrammetry-scanned props, shipped as `.blend`, `.fbx`, `.gltf` and USD, with
  texture resolutions typically up to 4K.

Everything is **CC0**: commercial use, no attribution, redistribution allowed. Their terms do
prohibit scraping, so pull assets through the site or the API rather than crawling it.

**Fit here.** Scanned props are far denser than this game's low-poly look and are not drop-in
board content — decimate them, or use them as the high-poly source you bake from. The HDRIs and
the textures, by contrast, are directly usable: an HDRI costs no triangles and buys most of the
lighting quality on a mobile budget.

**Reachable from the Blender MCP.** `mcp__blender__search_polyhaven_assets` and
`mcp__blender__download_polyhaven_asset` hit Poly Haven's public API directly, so a texture or
HDRI can land in a Blender scene without a browser round-trip. There is also a plain HTTP API at
<https://api.polyhaven.com> (no key) if a script needs it.

## ambientCG

<https://ambientcg.com> — thousands of PBR materials, no account required, over a million
downloads a month. Where Poly Haven is curated and deep, ambientCG is *broad*: if you need "some
plausible cobblestone" and do not care which, this is the faster stop.

Eight asset types, all **CC0**: Material (PBR), Atlas, Decal, Photo Texture (plain, no PBR maps),
Substance, HDRI, 3D Model, and Terrain.

There is a documented public API (<https://docs.ambientcg.com>, v1–v3) including a
`/downloads_csv` endpoint — the one source here with a first-class bulk-metadata path, which
matters if a material set ever needs to be re-fetched reproducibly rather than by hand.

**Fit here.** Decals and atlases are the underused half. A tile board gets more visual variety per
byte from a decal sheet laid over a shared material than from a unique texture per tile kind, and
a mobile draw-call budget notices the difference.

## Kenney

<https://kenney.nl/assets> — thousands of assets across 3D kits, 2D sprites, UI and audio, all
**CC0 (public domain)**, no registration, no payment. Attribution is not required; if you credit
anyway, "Kenney" or "kenney.nl" is the form asked for.

The 3D side is organised as **kits** — Mini Dungeon, City, Castle, Nature, Furniture, Platformer,
Space and so on — modular pieces on a consistent grid and a shared palette, meant to snap together.
Formats vary by pack and are listed on each pack's page; the common shipping set is OBJ plus FBX
plus glTF/GLB, often with the source `.blend`.

**Fit here.** This is the closest match on the list to the game's own idiom: chunky, low-poly,
flat-shaded, grid-aligned. That makes it excellent for **greyboxing a board chapter** before the
real art exists — and it makes it a trap for shipping, because a Kenney kit reads instantly as a
Kenney kit. Use it to prove a layout, then replace it.

⚠️ The `Kenney Game Assets All-in-1` bundle on itch.io is a paid convenience bundle over the same
CC0 content. Paying for it does not change the licence, and not paying does not lose you anything
except the single download.

## Kenney Audio

<https://kenney.nl/assets/category:Audio> — the same CC0 terms, same no-account access, split into
packs by role: Interface Sounds and UI Audio, Impact Sounds, RPG Audio, Sci-fi Sounds, Digital
Audio, Casino Audio, Music Jingles, and two Voiceover packs (one general, one Fighter).

**Fit here.** Interface/UI Audio and Impact Sounds cover most of what an auto-battler actually
needs — a die landing, a hit connecting, a perk being drafted, a menu confirming. RPG Audio covers
the shrine/shop/chest tile events. The Music Jingles are stings, not tracks; they do not solve
background music.

🔒 An audio provenance record needs `tool` and `toolVersion` in addition to the `cc0` fields, and
an art record must **not** carry them. See the record-format table in
[`assets/provenance/README.md`](../assets/provenance/README.md).

## Quaternius

<https://quaternius.com> — low-poly packs, and the one place on this list where a meaningful share
of the characters arrive **already rigged**. Packs are tagged `Rigged`, `Retargetable` and
`Animated`; humanoid rigs are the common case, and creature and vehicle packs ship with animations
baked in.

**CC0**, no attribution, commercial use fine, no account. Downloads are `.blend` and `.fbx`. A
Patreon exists for an all-in-one bundle and early access; it buys convenience, not rights.

**Fit here.** A retargetable humanoid rig is the expensive part of a character pipeline, and this
is the cheapest legitimate source of one. Worth checking against the hero recipe before rigging
anything from scratch — the swappable weapon sockets are ours, but the skeleton underneath need
not be.

## The Base Mesh

<https://thebasemesh.com> — 1,250+ base meshes, **CC0**, free. Not finished assets: starting
points. What they guarantee is the part that is tedious to get right by hand — **clean quad
topology, UV unwrapped, built to real-world scale.**

**Fit here.** Two uses. First, sculpting and modelling start points, so a new enemy begins from
correct anatomy and a sane edge flow instead of a cube. Second, **scale calibration** — a library
built to real-world scale is a free answer to "how big is a door", which is exactly the question
that goes wrong when props from five sources land in one scene.

Formats and any download gate are stated per mesh on its own page; check there before assuming a
format is available.

## Smithsonian 3D / Scan the World

Two separate projects, both museum-scale scan archives, both worth knowing and both requiring more
licence care than everything above.

**Smithsonian Open Access 3D** — <https://3d.si.edu> — 2,000+ models scanned from the Smithsonian's
collections, from sculpture and specimens to the Apollo 11 command module. Downloadable as GLB,
glTF and OBJ. The Open Access programme releases material under **CC0**, and there is a public API
and a GitHub data repository for the metadata.

**Scan the World** — <https://www.myminifactory.com/scantheworld/> — 16,000+ objects since 2014,
photogrammetry-scanned in partnership with 50+ cultural institutions, hosted on MyMiniFactory.

⚠️ **Scan the World's licences are per object and they are not uniform.** Many objects are CC0;
others are CC BY or **CC BY-NC-SA**, depending on the institution that holds the original. A
non-commercial clause is fatal for this project, and "I found it in a CC0 collection" is not a
defence — the object page is the authority. Treat Smithsonian the same way even though CC0 is the
programme default, because not every item on `si.edu` is inside the Open Access programme.

**Fit here.** These are 3D-print-oriented scans: dense, watertight, usually untextured or
vertex-coloured, and wildly over budget for a mobile real-time renderer. Do not import one and
expect a game asset. They are useful as **sculptural reference and as bake sources** — a statue
for a shrine tile, a relief for a boss arena wall — where a decimated mesh or a baked normal map
carries the silhouette and the original never ships.

---

## Before you deliver anything from this list

1. **Read the licence on the object's own page**, not the site's. Copy what it says into the
   record's `licence` field.
2. **Write the provenance record** — `kind: "cc0"`, with `source`, `url`, `licence` and
   `dateRetrieved`. `assetId` must equal the file stem *and* name a slot in
   `game-data/assets/asset_manifest_{art,audio}.json`. Records go flat in
   `assets/provenance/records/`; a subdirectory is refused outright.
3. **Generated and placeholder output goes to `artifacts/`, never to `assets/`.** The delivery
   scan reads the filesystem, not the git index, so a local experiment written under `assets/` is
   a delivery with a missing record and a red gate.
4. **Keep the download.** CC0 is irrevocable but a URL is not — sites reorganise and assets get
   pulled. The record's `url` is provenance, not a retrieval plan.
