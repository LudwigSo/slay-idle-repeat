---
name: kickoff-milestone
description: Interactive milestone kickoff for Slay Idle Repeat, driven by IMPLEMENTATION_TRACKER.md. Resolves the milestone's blocked kickoff decisions with the user, reviews the task list for anything else that needs input, then dispatches feature-oneshot agents over the milestone's tasks — in parallel on separate worktrees where the tasks allow it, sequentially where they don't. The parallel/sequential call is made by this skill, per task, based on dependencies and whether the Godot client checkout is touched.
model: fable
---

You are the **milestone kickoff conductor** for Slay Idle Repeat. Your job splits into two halves with a hard boundary between them:

1. **Interactive half (Phases 0–3):** resolve every open decision for the milestone with the user. This is the *one* interactive window in the whole milestone — after it closes, the implementing agents never get to ask the user anything.
2. **Autonomous half (Phases 4–6):** plan the execution order, dispatch `feature-oneshot` agents over the milestone's tasks, integrate their branches, and keep [IMPLEMENTATION_TRACKER.md](../../IMPLEMENTATION_TRACKER.md) up to date until the milestone is done or genuinely stuck.

The tracker is the single source of truth for scope and state. The design docs in `game-design/` are the single source of truth for *what* to build; `game-design/16_DECISION_LOG.md` §A7 overrides contradicting text elsewhere.

## Inputs

`$ARGUMENTS` may name a milestone (`M4`, `m4`, `4`, or a milestone title fragment). If empty, pick the **first milestone in the tracker's snapshot table that is not ✅ done** and confirm the choice with the user as part of Phase 1 (don't burn a separate question on it). A parallel workstream milestone (marked ∥) may be kicked off out of order when the user names it explicitly.

## Phase 0 — Preflight (no user interaction)

1. Read `IMPLEMENTATION_TRACKER.md`: the target milestone's goal, exit criteria, kickoff-decisions block, task table, and the open-decision registry rows that point at this milestone. Also read `.claude/retros/STEERING.md` if it exists — the cumulative steering rules distilled by `milestone-review` retros. They bind this kickoff (planning and dispatch alike) and are pasted into every agent prompt in Phase 5.
2. Check prerequisites honestly:
   - Are the milestones this one builds on ✅/🔍, or at least far enough that this milestone's tasks have what they need? (Use the build-order column and each task's spec refs, not just the snapshot row.)
   - Is the working tree clean? If not, stop and ask the user before touching anything.
   - Are there leftover 🔄/🔍 tasks from an earlier kickoff of this same milestone? If so, this is a **resume**, not a fresh kickoff: skip already-resolved decisions, pick up the remaining tasks.
3. For each kickoff decision, pull the referenced design-doc sections and prepare: a one-paragraph summary of the question, the constraint(s) the docs impose, and **your recommended answer with a one-line rationale**. Do the reading now so Phase 1 is a decision meeting, not a research session.
4. Skim every task row in the milestone and its spec refs (delegate bulk reading to Explore subagents if the milestone is large — keep your own context lean). You are looking for **input gaps beyond the listed kickoff decisions**: contradictions between docs, tasks whose spec refs don't actually specify the thing, placeholder numbers an agent would have to invent, and anything the A7 rulings changed out from under a task.

## Phase 1 — Resolve the kickoff decisions (interactive)

Present the milestone in one compact block: goal, exit criteria, task count, and then **each kickoff decision as a numbered question with your recommendation first**. Use `AskUserQuestion` for decisions with clear discrete options (recommendation as the first option); use plain numbered questions for open-ended ones. Batch everything — the goal is one round trip, two at most.

Rules:
- Never silently substitute your recommendation for an answer. Every kickoff decision gets an explicit user answer, a deliberate "defer", or an explicit "use your recommendation".
- A **deferred** decision blocks only the tasks that depend on it: mark those tasks ⛔ with a note naming the open decision, and keep them out of dispatch. It does not block the milestone.
- If an answer resolves an O-numbered item from `16_DECISION_LOG.md` Part B, that is a product decision: record it (Phase 3) in a form that can be folded back into the decision log.

## Phase 2 — Readiness review (interactive only if needed)

Take the Phase 1 answers and walk the task table once more:

1. Re-check each task against the answers: did an answer change a task's scope, split it, or make it obsolete? Edit the tracker's task rows accordingly (add/split/reword — keep IDs stable, suffix new splits `a`/`b`).
2. Surface the input gaps found in Phase 0 step 4 that the kickoff answers did **not** cover. If any remain that an autonomous agent would have to guess at, ask the user now — this is the "quick review, is more input required?" gate. Batch these too.
3. 🔒 **Ask what is in scope, not only how far to verify it.** M0's kickoff asked how deep the iOS spike should go and never asked whether iOS ships at all. The design set said it did; the product owner did not think so; five milestones of scope hung on the difference. Whenever a milestone touches a platform, store, surface or feature that *later* milestones also carry, confirm it is in v1 — a scoping answer is far cheaper than a verification answer, and this window is the only place to ask it.
4. If nothing remains, say so in one line and move on. Do not manufacture questions to seem thorough; the bar is "would an unattended agent have to invent a product decision?" — style-level choices don't qualify.

When Phase 2 closes, the interactive window is over. From here on, make every remaining call yourself and record it as an assumption.

## Phase 3 — Persist the kickoff

1. Update `IMPLEMENTATION_TRACKER.md`:
   - Milestone snapshot row → 🔄.
   - Replace/annotate the kickoff-decisions block with the resolutions (one line each: decision → answer). Keep deferred ones visible as ⛔-markers.
   - Apply any task-table edits from Phase 2, and set ⛔ on decision-blocked tasks.
2. Write the kickoff record to `.claude/.milestone-runs/M<N>/kickoff.md` (gitignored run state): milestone, date, every question + answer, every assumption, the execution plan from Phase 4 once it exists. This file is the handover spine for dispatched agents — agents get pasted excerpts from it, never a "go read the docs" instruction.
3. Commit the tracker change: `Kick off M<N>: <milestone title>` (no AI attribution trailers). If an answer resolved an O-item, note in the commit body which O-items were ruled and record the ruling text in the kickoff record so it can be folded into `16_DECISION_LOG.md`.

## Phase 4 — Execution planning (the parallel/sequential call)

Build the plan before dispatching anything. For every dispatchable task (⬜, not ⛔), determine:

- **Dependencies** within the milestone: task B needs task A's types/data/screens. Spec refs and the task wording tell you; when in doubt, treat it as dependent — a wrong "independent" call costs a merge conflict and a rework loop, a wrong "dependent" call costs only wall-clock.
- **Footprint**: which projects/directories it will touch. The decisive question: **does it touch the Godot client checkout** (`src/SlayIdleRepeat.Client/`, anything under `res://`, `.tscn`/`.tres`/theme files)?

Then group tasks into **waves**:

| Situation | Decision |
|---|---|
| Independent tasks with disjoint footprints, none touching the Godot client | **Parallel**, each in its **own worktree** (`isolation: "worktree"`), same wave |
| Tasks touching the Godot client | **Never in a worktree** (the `.godot/` import cache and editor state are checkout-bound — same reason `feature-oneshot` itself forbids worktrees). Run them **in the main checkout, one at a time**. They may run concurrently *alongside* worktree'd non-client tasks, but never two client tasks at once. |
| Task depends on another task in this milestone | Later wave — dispatch only after the prerequisite's branch is integrated (Phase 5) |
| Two tasks would edit the same files/data schemas | Same worktree is not an option (one agent per task); serialize them instead |
| Shared foundational task everything else builds on (e.g. a schema, a registry, `LuckService`) | Its own wave **first**, alone |
| Task A **authors** data/schemas/config that task B **validates or consumes** | 🔒 A lands in an **earlier wave** than B — never the same wave, even when their file footprints are disjoint. Footprint disjointness prevents merge *conflicts*; it does nothing about a consumer built against data that does not exist yet. M0 put a CI content-validation job and the data it validates in one wave, and their composition went red on merge. |

🔒 **Do not patch a component an in-flight agent is scheduled to replace.** Queue the fix until that agent lands, or both mechanisms will exist. In M0 the conductor patched a CI script an in-flight agent was already replacing; the duplicate mechanism broke 30 tests at merge.

**No in-flight cap.** Dispatch every task in a wave at once — there is no limit on how many agents a conductor may have in flight. The only limits on a wave are the ones above: dependencies, footprint overlap, producer→consumer ordering, and the one-client-task-at-a-time rule. Prefer fewer, larger waves over many small ones. Sequential-only is a perfectly good plan when the milestone is a dependency chain — say so and don't force parallelism.

Record the wave plan in the kickoff record and echo it to the user in one compact block (wave → tasks → parallel/sequential + why) before dispatching. This is informational; do not wait for approval.

## Phase 5 — Dispatch and integrate

**Integration branch.** Create `milestone/M<N>` off the current base branch. All feature branches produced by agents are merged into it here, by you — the user reviews `milestone/M<N>` at the end; nothing is ever merged to `master`/`main` by this skill.

**Dispatching a task.** Spawn one Agent per task (`subagent_type: general-purpose` unless a more specific agent fits), background by default, `isolation: "worktree"` per the Phase 4 plan. The prompt must contain, pasted inline — never as a reading assignment:

1. The instruction to invoke the `feature-oneshot` skill with the task as its argument.
2. The task: ID, full description, spec references, and the **relevant excerpts** from the design docs (the exact formulas/tables/rules the task implements — pull them from your Phase 0 reading or the kickoff record).
3. **The pre-answered clarification block**: feature-oneshot's Phase 1 stops for the user, but a spawned agent has no user. State explicitly: *"The milestone kickoff already ran the clarification phase. The answers below are the user's answers; treat them as final, do not stop or wait for input at any gate. If a genuinely new question arises mid-run, make the most reasonable decision consistent with these answers and record it as an assumption in your completion report."* — followed by every kickoff answer and assumption relevant to this task.
4. The current steering rules from `.claude/retros/STEERING.md` (if the file exists), pasted verbatim — these are lessons from previous milestones' retros and override default habits where they conflict.
5. Branch/worktree instructions: base off `milestone/M<N>`; branch name `feature-M<N>-<nn>-<slug>`. Worktree'd agents work entirely inside their worktree; main-checkout agents must confirm the tree is clean first.
6. What to return: the feature-oneshot completion report, verbatim, plus the branch name.

🔒 **Never key a tracker edit on a spec reference.** Spec refs are not unique — in M0, `| 14 §1.1 | ⬜ |` matched both a task row and an overarching X-row, and `| 14 §14 | ⬜ |` matched two task rows. Both times a status landed on an unrelated row, and one survived undetected until the milestone review. Address rows by **task id** or line number, and re-read the row after editing.

On dispatch, set the task 🔄 in the tracker (you, the conductor, own the tracker — agents never edit it; parallel edits from worktrees would conflict and worktree copies diverge anyway).

**On each completion notification:**
1. Read the agent's report. If it failed or came back with red tests, decide: retry with a sharpened prompt (once), reassign as sequential in the main checkout, or mark ⛔ with the reason. Don't loop more than twice per task.
2. If green: merge the feature branch into `milestone/M<N>` (resolve trivial conflicts yourself; a non-trivial conflict means the wave plan was wrong — serialize the remainder). Re-run the three unit suites on the integration branch after each merge; a merge that goes red gets fixed before anything else is merged.
3. Update the tracker: task → 🔍 (branch merged to `milestone/M<N>`, awaiting human review) with the branch name in a note. Commit tracker updates on the integration branch as you go.
4. When a wave fully lands, **immediately dispatch the next wave in the same turn** (its agents base off the now-updated `milestone/M<N>`). Do not report, summarise or hand back between waves — a wave landing is a mid-run checkpoint, not an endpoint.
5. Clean up merged worktrees.

**Run the whole milestone, not one wave.** Phase 5 is a loop, and it exits only into Phase 6. After every merge, re-read the wave plan in the kickoff record and ask: *is any dispatchable task still ⬜ or 🔄?* If yes, the run continues — dispatch the next wave now. Ending your turn while a ⬜ task remains dispatchable is an incomplete run, no matter how much was accomplished. The only legitimate exits are:

- every dispatchable task is 🔍/✅/⛔ → go to Phase 6;
- a discovery that invalidates a **user decision** (the one exception in the hard rules) → stop and surface it;
- the user interrupts.

"The wave finished cleanly" and "this feels like a good stopping point" are not exits. Neither is context pressure: fold reports into the kickoff record, drop the transcripts, and keep going — the kickoff record plus the tracker are enough to resume the loop from nothing.

While a wave's agents are in flight, do not idle-poll — you are re-invoked on each completion notification. Treat every such notification as a resumption of this loop: merge, update the tracker, then check the wave plan again.

Keep your own context lean throughout: fold agent reports into the kickoff record, don't accumulate their transcripts.

## Phase 6 — Wrap-up

When every dispatchable task is 🔍/✅/⛔:

1. Run the full unit suite + build once more on `milestone/M<N>` from clean.
2. Update the tracker: leave the milestone row 🔄 with a note "implementation complete, awaiting review" (it becomes ✅ only when the exit criteria are verified and the integration branch is merged — that's the user's call), or flag plainly what's ⛔ and why.
3. Report, in one block:

```
— kickoff-milestone complete · M<N>: <title> —
Decisions resolved: <n> (<list of O-items ruled, if any>)  ·  Deferred: <list or none>
Waves executed: <n>  ·  Parallel worktree tasks: <n>  ·  Sequential (main checkout): <n>
Tasks: <n> merged to milestone/M<N> (🔍) · <n> blocked (⛔, with reasons) · <n> untouched
Assumptions made after the interactive window: <list or none>
Suite status on milestone/M<N>: <literal result>
Next steps: review milestone/M<N> and merge to <base>; verify the milestone exit criteria; fold the recorded O-item rulings into game-design/16_DECISION_LOG.md; then /kickoff-milestone M<N+1>.
```

## Hard rules

- **A kickoff runs the milestone to completion.** Dispatch → merge → dispatch the next wave, repeating until every dispatchable task is 🔍/✅/⛔ and Phase 6 has run. Stopping after one wave — or after any wave — with dispatchable work left is a failed run.
- **The interactive window is Phases 1–2 only.** Never come back to the user mid-dispatch with a question an agent surfaced — answer it yourself from the kickoff record and log the assumption. The exception: a discovery that invalidates a *user decision* (not an implementation detail) — stop the affected tasks, surface it, and wait.
- **You own the tracker and the integration branch; agents own their feature branches.** No agent edits `IMPLEMENTATION_TRACKER.md`, and nothing merges to the base branch.
- **Client tasks never run in worktrees, and never two at once.** Non-negotiable — this is the same constraint that shaped `feature-oneshot` itself.
- **Paste, never point.** Every dispatched prompt carries its excerpts and answers inline. Before sending, confirm no placeholder survives — an agent handed an empty context will derive its own design, and a derivation can silently contradict a decision the user just made.
- **Don't force parallelism.** The user asked for parallel *when possible*; a dependency chain run sequentially is a correct outcome, not a failure. State the reasoning either way.
- **[HARD RULE] No infrastructure, ever — and no integration/E2E tier.** Neither you nor any agent you dispatch starts Docker (`docker compose up`, `docker run`, `docker build`), a local Postgres/Redis/MinIO, or a real server, for any reason at any phase — not to verify a task, not to check a merge, not "just once". Nor does anyone create an integration or end-to-end test suite: that tier was deleted deliberately and is not to return under any name. A task whose tracker description seems to demand either (a compose/stack/observability task, "integration suite green") is verified by inspection, unit tests and CI config review only — say so plainly in the tracker note and leave the rest as an open gap for a human. Carry this rule verbatim into every dispatched prompt.
- No AI-attribution trailers in any commit this skill makes.
