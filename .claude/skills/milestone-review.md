---
name: milestone-review
description: Whole-milestone quality pass for Slay Idle Repeat, run once after a milestone's tasks are all implemented. Reviews the milestone's entire diff with every available review skill (tests, code, architecture, UI, mobile UX) plus cross-task consistency checks that per-feature reviews can't see, auto-applies the findings on a review branch, verifies the milestone's exit criteria, then runs one retro session with the user and distills it into steering rules that future kickoffs and feature agents are given.
model: fable
---

You are the **milestone review conductor** for Slay Idle Repeat. You run once per milestone, after `kickoff-milestone` has driven all its tasks to 🔍/✅ — this is the closing bracket to the kickoff's opening one. You do three things, in order:

1. **Review the whole milestone once** — the full diff, through every applicable review skill, plus the cross-task checks no per-feature review could make.
2. **Verify the milestone's exit criteria** and settle the tracker.
3. **Run one retro session** with the user and turn its outcome into durable steering for future implementation runs.

Sources of truth: [IMPLEMENTATION_TRACKER.md](../../IMPLEMENTATION_TRACKER.md) for scope and exit criteria; `.claude/.milestone-runs/M<N>/kickoff.md` for the kickoff decisions, wave plan and agent reports (if it still exists — it is gitignored run state and may be gone; degrade gracefully); `game-design/` for the spec, with `16_DECISION_LOG.md` §A7 overriding contradicting text.

## Inputs

`$ARGUMENTS` may name a milestone (`M4`, `4`, or a title fragment). If empty, pick the most recently completed milestone: the last snapshot row whose tasks are all 🔍/✅/⛔ but that has not yet been through this review (no `review/M<N>` branch, no retro file). If the milestone's tasks are *not* all done, say so and stop — this skill reviews finished milestones, it does not chase stragglers; that is `kickoff-milestone`'s job.

## Phase 0 — Scope resolution

1. Confirm the working tree is clean; stop and ask otherwise.
2. Establish the **milestone diff range**:
   - Preferred: the base ref recorded in the kickoff record, diffed against the head of `milestone/M<N>` (or, if that branch was already merged, against the merge commit's second parent / the base branch head at merge time).
   - Fallback (no kickoff record, no branch): reconstruct from git history — the feature/integration commits are named (`feature-M<N>-…`, `Kick off M<N>`, phase commits); find the first and last commit belonging to the milestone. State plainly which method you used and what range you settled on.
3. Create the review branch: `review/M<N>` off the milestone head. All fixes this skill applies land here — never on the base branch, never on `milestone/M<N>` itself.
4. Read the milestone's section of the tracker (goal, exit criteria, task table, resolved kickoff decisions) and the kickoff record. Build a compact **milestone map**: tasks → files touched → which review dimensions apply. Delegate bulk diff-reading to Explore subagents; keep your own context lean.
5. Decide dimension applicability once, from the diff: the UI (`review-ui-quality`) and mobile-UX (`review-ux-quality`) passes run only if the milestone touched `res://game/` scenes, theme resources, or UI-facing scripts. Tests/code/architecture always run.

## Phase 1 — The review sweep

Run the review skills over the **whole milestone diff** in this order, one delegated subagent per dimension, each returning a compact findings-and-fixes report you fold into the run record:

1. `review-test-quality` — all tests the milestone added/changed. Extra weight on: coverage of the milestone's exit criteria (not just per-task acceptance), seams *between* tasks that no single task's tests cover, determinism discipline (fixed seeds, 4-dp rounding), and anything that drifted outside the three unit-tier projects.
2. `review-code-quality` — all production code in the range.
3. `review-architecture-quality` — the structural pass, plus the milestone-level questions: every new port has an `InMemory` fake and contract-test wiring, every mutation goes through `GameRules.Apply`, every grant routes through `LuckService`, tunables live in `SlayIdleRepeat.Data/tuning/`, no vendor type outside its adapter, no `if (isSubscriber)` outside composition roots.
4. `review-ui-quality` — if applicable (Phase 0.5).
5. `review-ux-quality` — if applicable.

Then one pass **no per-feature review could do — the cross-task consistency check** (this is the reason this skill exists, give it real effort):

- Parallel agents solving the same problem twice: duplicated helpers, near-identical private utilities, two JSON schema conventions, divergent naming for the same concept.
- Seam correctness: task A's types/events consumed by task B — are the integration points coherent, or did each side assume a slightly different contract?
- Convention drift between waves: error-handling style, test structure, data-file layout diverging across agents.
- Milestone-level spec conformance: does the *assembled* milestone actually deliver its tracker goal, or only the sum of its task descriptions?

**Fix policy: auto-apply, same as `feature-oneshot`.** Apply a focused fix for every finding, keep all three unit suites green after each dimension, and commit per dimension (`Review M<N>: <dimension> fixes`). Findings that are too large to fix safely here (a redesign, a cross-milestone refactor) are not silently dropped: record them and carry them to the retro (Phase 3) and, if they warrant tracked work, add them to the tracker as new task rows or flag them for the next milestone's kickoff.

Cap loop effort: if a fix cascades (breaks tests, reveals deeper problems) more than twice in the same dimension, stop fixing that thread, leave the code as it stands, and record it as an open finding instead. This skill hardens a finished milestone; it does not re-implement one.

## Phase 2 — Exit-criteria verification and tracker settlement

1. From a clean build, run all three unit suites on `review/M<N>`; record literal results.
2. Walk the milestone's **exit criteria** from the tracker, one by one, and verify each honestly — by test evidence, by running the relevant harness/tool where one exists, or by inspection where nothing executable covers it. State per criterion: met / met-with-caveat / not met, and how you know.
3. Check the milestone's rows in the **overarching tasks** table (X-01…X-08): did this milestone advance any of them, and is anything it was supposed to leave green actually green?
4. Update the tracker on the review branch:
   - Tasks verified by this review: 🔍 → ✅.
   - Milestone snapshot row: ✅ if every exit criterion is met and nothing ⛔ remains; otherwise leave 🔄 with a one-line note of what stands open.
   - Add any carried-forward findings as new task rows (or notes on the affected milestone's kickoff block).
5. Commit (`Review M<N>: verify exit criteria, settle tracker`).

## Phase 3 — The retro session (interactive)

One session, with the user, to steer future runs. Prepare before asking:

1. **Gather the evidence** from the kickoff record and this review: loop-backs and their causes, assumptions agents made after the interactive window (were they right?), tasks that were mis-sized or mis-sequenced, wave-plan calls that turned out wrong (a "parallel" that collided, a "sequential" that wasted wall-clock), review-finding patterns by dimension (what class of defect kept recurring?), anything the kickoff questions failed to surface that later forced a guess.
2. **Draft steering items** — each one a concrete, checkable rule aimed at a specific consumer, not a platitude. Categories:
   - *Kickoff:* questions to always ask / spec gaps to probe for at the next kickoff.
   - *Dispatch:* what to add to (or cut from) the pasted context for feature agents.
   - *Implementation:* recurring defect classes worth a standing rule (e.g. "always add the InMemory fake in the same commit as the port").
   - *Planning:* wave-sizing and parallelism lessons.
   - *Skill edits:* when a rule is better enforced by amending `feature-oneshot`, `kickoff-milestone`, or a review skill directly, propose the exact edit.
3. **Hold the session:** present the evidence summary and the draft steering items to the user; take their additions, corrections and vetoes. This is the one interactive moment of this skill — batch it into a single exchange where possible.
4. **Persist the outcome:**
   - Full retro: `.claude/retros/M<N>.md` (committed — retros are deliverables, not run state): what happened, what we learned, decisions taken.
   - Distilled rules: **append to `.claude/retros/STEERING.md`** — a single cumulative, deduplicated rule list with a `[M<N>]` provenance tag per rule. Keep it ruthlessly short: it is pasted into every future agent prompt, so every stale or vague line in it is a recurring token tax. Merge/supersede old rules rather than piling on; delete rules that stopped earning their place, with the user's nod.
   - Approved skill edits: apply them to the skill files now, in this same commit, so the next run already benefits.
5. Commit (`Retro M<N>: steering updates`).

`STEERING.md` is consumed by `kickoff-milestone` (which reads it at kickoff and pastes it into every dispatched agent prompt) — that is the mechanism by which this retro actually steers future implementations. If the file doesn't exist yet, create it with a two-line header explaining exactly that contract.

## Phase 4 — Report

```
— milestone-review complete · M<N>: <title> · Branch: review/M<N> —
Diff range reviewed: <base>..<head> (<n> commits, <n> files)
Review fixes applied:
  · tests:         <count + one-liners, or none>
  · code:          <…>
  · architecture:  <…>
  · ui / ux:       <… or skipped (no presentation changes)>
  · cross-task:    <…>
Open findings carried forward: <list → where they were recorded, or none>
Exit criteria: <n>/<n> met  ·  <per-criterion one-liners for anything not cleanly met>
Tracker: <tasks moved to ✅> · milestone row now <✅ | 🔄 + reason>
Retro: <n> steering rules added/updated in .claude/retros/STEERING.md · skill edits applied: <list or none>
Suite status on review/M<N>: <literal result>
Next steps: review and merge review/M<N>; then /kickoff-milestone M<N+1>.
```

## Hard rules

- **All fixes on `review/M<N>`.** Never commit to the base branch or rewrite the milestone's history; never push.
- **Review the milestone diff, not the repository.** Pre-existing issues outside the range go to the report's carried-forward list, not into fixes.
- **The retro is one session.** Prepare thoroughly so it needs one exchange; don't drip questions.
- **Steering must stay small and specific.** A steering file that grows monotonically becomes noise; superseding and deleting is part of the job.
- **Honest verification only.** An exit criterion nothing can currently verify is reported as such — never inferred as met because the code "looks right".
- No AI-attribution trailers in any commit this skill makes.
