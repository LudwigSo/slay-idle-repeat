# 31 — Asset Licence Risks

> ## ⚠️ THIS DOCUMENT IS A REGISTER, NOT A GATE
>
> **Nothing in this document blocks anything.** It does not gate production, it does not gate a build, it does not gate a milestone, and it may not be cited to stop work. That is the explicit instruction behind ruling **D60** (2026-08-23): 3D asset production proceeds, output is treated as **work in progress that is not published**, and licence questions are *written down* rather than *enforced*.
>
> It exists so that the risks are known, attributed and reviewable at the moment they start to matter — which is the moment publication is on the table. It is a list of things that are **true now and would be problems later**, kept current so that later is not a discovery.
>
> **It becomes a gate only by a new ruling.** Not by escalation, not by a reviewer's judgement, and not by anything written in this file.

⚠️ **Not legal advice.** This is an engineering register written by reading vendor terms and noting what they do and do not say. Every item marked 🔴 needs a competent human — and in several cases a lawyer — before it is relied on. Nobody on this project has confirmed any of it in writing, which is itself the point of §3.

---

# PART A — WHY THIS DOCUMENT EXISTS

The project previously handled this differently, and the difference is worth stating plainly so the change is not mistaken for an oversight.

`15` §G and `20` §6 both required a **commercial licence confirmed in writing per tool, before the first batch**, and called it *"a legal prerequisite, not a formality"*. That requirement was enforced mechanically: `assets/provenance/tool-licences.json` holds one row per generation tool with a `confirmedInWriting` member that is **absent** on every row, `ToolLicence.RequireConfirmedInWriting()` throws on an absent value, and `ProvenanceGate` raises `UnconfirmedLicence` for any delivered asset naming an unconfirmed tool. The gate is *both-directional* and fails loudly by design.

That design is correct for a project that is publishing. **D60 changes the premise, not the design.** Nothing produced under the current `15` is published, so there is no distribution, no commercial exploitation, and nothing for a licence to be a prerequisite *to*. Enforcing a publication-time precondition against unpublished experiments would block the work that tells us whether the medium is even viable — which is the wrong trade at this stage.

**The mechanism is untouched.** `ProvenanceGate`, `tool-licences.json` and the three record kinds all still exist and still fail loudly. They simply have nothing to act on, because generated 3D output lives under `artifacts/` and never under `assets/` (see §C1). No gate was weakened, no exclusion list was widened, and no `confirmedInWriting` value was set — that remains a legal act that only the product owner may perform, per M8-01b.

---

# PART B — THE RISK REGISTER

## B1. Risk by tool

Tools are listed because they are in use or plausibly in use, **not** because any of them is endorsed or locked. `15` §B0 locks no tool. A tool leaving this table is not an event.

| Tool | Role | Licence risk | Notes |
|---|---|---|---|
| **Blender** | Authoring, retopology, rigging, baking, rendering, export | 🟢 **Low** | GPL-2.0-or-later. **Output is unambiguously yours** — the GPL covers the program, not what you make with it, and the Blender Foundation states this explicitly. The GPL does *not* propagate to a `.glb`. The lowest-risk element in the pipeline, which is a real argument for keeping authoring under our own control (`15` §B0) |
| **Meshy** | Text-to-3D and image-to-3D base meshes | 🔴 **Unconfirmed** | Paid tiers advertise commercial use; free tiers generally do **not**, and the difference is a plan attribute rather than an output attribute — so *which plan generated a given asset* is a fact that must be recorded at generation time or lost. No account was held as of 2026-08-23. **Terms unread, unconfirmed, and subject to change** |
| **Hunyuan3D** | Base meshes, via the Blender MCP | 🔴 **Unconfirmed** | Tencent model. Historically carries **use restrictions in the model licence itself** (territorial and acceptable-use clauses) that are unusual for a generation tool and do not resemble a normal SaaS ToS. Read the model licence, not just the service page |
| **Rodin / Hyper3D** | Base meshes, via the Blender MCP | 🔴 **Unconfirmed** | Third-party service. Terms unread. The Blender MCP addon also exposes a **free-trial key path**, and free-trial output is exactly the case most likely to exclude commercial use |
| **Midjourney** | Concept art and texture reference only — **no longer a delivery tool** | 🟡 **Reduced but live** | The only tool with a row in `tool-licences.json`. `15` §B0's previous text stated commercial rights are included on paid plans, and the M8 kickoff (2026-08-12) recorded that the paid plan **is** held. `confirmedInWriting` is still absent. Its role shrinking to *reference* reduces exposure but does not remove it — see B2.3 |
| **Sketchfab download** | Third-party model download, via the Blender MCP | 🔴 **Highest risk in the table** | ⚠️ **Categorically different from a generator.** These are *other people's models*, each under its own per-asset licence — CC-BY (attribution required), CC-BY-NC (**non-commercial**, unusable), or a paid store licence. A downloaded model carries obligations a generated one does not, and the MCP tool makes acquiring one a single call. **Do not let a downloaded model become a base mesh for a shipping asset without reading that model's specific licence** |
| **PolyHaven download** | HDRIs, textures, models, via the Blender MCP | 🟢 **Low** | CC0 across the library. Genuinely public domain, no attribution required. Fits the existing `cc0` provenance kind exactly — the one third-party source that already has a home in the schema |

## B2. Risks that are structural, not per-tool

These do not go away by choosing a different vendor, and they are the reason this register is organised around them rather than around a shortlist.

### B2.1 🔴 Training-data provenance is unknowable from the outside

No generative 3D vendor discloses a fully auditable training corpus. A vendor's grant of rights in *its output* is a contractual promise from that vendor; it is not, and cannot be, a warranty that the output infringes nobody. Some vendors offer an indemnity, most do not, and the ones that do usually cap it and condition it on the paid tier. **This risk is irreducible by tool choice.** It is materially reduced by *how much of the final asset is ours* — which is the practical argument for treating generated meshes as block-outs that our own retopology, UVs, rig and bake replace (`15` §B4 steps 2–10).

### B2.2 🔴 "You own your outputs" is narrower than it sounds

The claim generally means *the vendor asserts no ownership claim of its own*. It routinely coexists with: non-commercial restriction on free tiers; a licence back to the vendor to use your prompts and outputs for training; public-by-default galleries where your generations are visible to others; a right to change terms unilaterally; and no indemnity. **Each of those is compatible with "you own your outputs" and each matters separately.** Read for all five, not for the headline.

### B2.3 🔴 Terms are a moving target, and the binding version is the one at generation time

The commercial terms that matter for an asset are the ones in force **when it was generated**, not when it ships. An asset generated today under permissive terms does not become non-commercial if the vendor tightens tomorrow — but proving *which* terms applied requires knowing the date, the tool, the version and the plan. That is a record-keeping problem, and it is cheap to solve now and impossible to solve retroactively. See §C2.

### B2.4 🟡 The plan tier is an attribute of the asset, not of the account

A free-tier generation and a paid-tier generation of the same prompt may carry different rights. Accounts change tier; assets do not. **The tier must be recorded per asset at generation time** or the distinction is permanently lost. This is the single most common way a project discovers, late, that part of its back catalogue is unusable.

### B2.5 🟡 Rendered output inherits its source's risk

`15` introduces asset kind **R** — 2D PNGs rendered from 3D models. A render is a derivative of its source mesh and **carries whatever the source mesh carries**. Kind **R** therefore does not launder a risky base mesh into a safe icon. Conversely, kind **F** (flat authored 2D) touches no generator at all and is the cleanest category in the manifest — 349 of the 949 assets.

---

# PART C — WHAT TO DO NOW, AND WHAT CHANGES LATER

## C1. What is already true and needs no action

* **Generated output goes to `artifacts/`, never `assets/`.** `assets/provenance/README.md` 🔒 requires this, the delivery scan reads the filesystem rather than the git index, and `artifacts/` is gitignored. This is what makes "work in progress, not published" a mechanical fact rather than an intention — and it is why `ProvenanceGate` stays green with no exclusion added.
* **No `confirmedInWriting` value has been set**, and none may be. Unchanged by D60.
* **No Meshy row was added to `tool-licences.json`.** Per that file's own note, *"adding a row is not the same as licensing a tool"* — a row with no confirmation still fails the gate, so adding one would change nothing except to imply somebody had shortlisted a vendor. Nobody has.

## C2. Cheap insurance worth doing now

None of this is a gate. All of it is far cheaper now than reconstructed later.

1. **Record the production metadata per asset** — `15` §B5 already requires it for reproducibility. Extend it with the four facts that decide licensing: **tool, tool version, plan tier, and generation date.** Same record, four more fields, no new mechanism.
2. **Keep every prompt and seed.** They are the evidence of independent creation, and they cost nothing to retain.
3. **Never delete a source mesh or an intermediate.** The chain from generated block-out to shipped asset is the argument that the shipped asset is substantially ours (B2.1).
4. **Read a Sketchfab model's licence before it becomes a base mesh.** This is the one place where a single MCP call can create an obligation that survives every downstream transformation. A CC-BY-NC model does not become usable by being retopologised.
5. **Prefer flat 2D (kind F) and Blender-authored geometry for anything already known to be shipping** — UI panels, 9-slice frames, perk emblems. `15` §H already schedules kind **F** UI work in parallel from day one, so this costs nothing and de-risks 349 assets by construction.

## C3. 🔴 What must be resolved *before* anything is published

This is the list that turns back into a gate, and the reason it is written now.

| # | Item | Owner |
|---|---|---|
| 1 | Commercial terms confirmed **in writing** for every tool that touched a shipping asset, with a `confirmationRef` naming where the confirmation is filed | 🔴 **Product owner only.** A legal act; no agent may perform it (M8-01b) |
| 2 | A **fourth provenance record kind** for 3D assets. `ProvenanceRecord.cs` has exactly three — `midjourney`, `procedural`, `cc0` — and a generated 3D asset fits none. Needs the `15` §B5 field set: tool, version, plan tier, prompt, seed, base-mesh hash, date | Engineering, on a ruling |
| 3 | A **row per tool** in `tool-licences.json`, added at the point a tool is actually licensed rather than shortlisted | Product owner |
| 4 | Per-asset audit of anything descended from a **downloaded** third-party model (B1, Sketchfab) | Whoever downloaded it |
| 5 | Decide whether **CC0-only** sourcing (PolyHaven) plus Blender authoring is sufficient for some categories, which would remove them from this register entirely | Product owner |
| 6 | Re-read every 🔴 in Part B. They were unread as of 2026-08-23 and terms change (B2.3) | Product owner |

⚠️ **Item 2 is the only one that is engineering work rather than a decision**, and it is worth noting that it is *currently harmless*: with no delivery under `assets/`, the missing fourth kind blocks nothing. It becomes blocking on the first real delivery, and it is small enough that it should not be discovered on that day.

---

# PART D — RELATIONSHIP TO OTHER DOCUMENTS

| Document | Relationship |
|---|---|
| `15_ART_DIRECTION_AND_ASSET_MANIFEST.md` | The production spec. Its §G licence row points here and marks itself **not a gate**. Its §B5 production record is where §C2's four fields belong |
| `16_DECISION_LOG.md` | **D60** is the ruling that created this document and suspended the licence precondition for unpublished work. A future ruling is the only thing that can turn this register back into a gate |
| `20_AUDIO_MANIFEST.md` | §6 carries the same written-confirmation requirement for audio. **Untouched by D60** — audio is settled differently, by `16` D50: sourced procedurally and from CC-licensed material under `kind: procedural`, with no audio tool licensed at all |
| `assets/provenance/README.md` | The enforcement mechanism, unchanged. Read it before assuming anything here relaxes it |
