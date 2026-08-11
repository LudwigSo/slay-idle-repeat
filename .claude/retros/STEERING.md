# Steering rules

Cumulative rules distilled from milestone retros. `kickoff-milestone` reads this file at kickoff and **pastes it verbatim into every dispatched agent prompt** — so every stale or vague line here is a token tax on every future agent. Keep it short, concrete and checkable; supersede and delete rather than pile on.

Each rule carries a `[M<N>]` provenance tag. Rules that stop earning their place get deleted, not archived.

---

## Implementation

**S1 · A test that cannot fail is a defect, not a weak test. [M0]**
Before claiming a rule, guard or assertion works, **make it fail on purpose**, capture the literal output, revert, and put that output in your report. Untested guards were M0's single largest defect class — they appeared in every suite and survived per-task review.

**S2 · Assert the identity, not the symptom. [M0]**
When several rules can produce the same error code, pin **which rule fired** — the pointer, location or message fragment — not just the code. In M0, deleting the exact schema bound a test's name cited still passed, because a different rule fired elsewhere. Uncapped ad rewards would have shipped green.

**S3 · Every reflection- or metadata-driven rule needs a floor on its subject set. [M0]**
A rule whose subject set can silently become empty passes forever. Assert a minimum count, or that a known member is present. Renaming one namespace prefix in M0 would have turned five architecture rules permanently green.

**S4 · A declared exception must expire by itself. [M0]**
Empty-suite exemptions, baseline entries, awaiting-content schemas, deliberately-vacuous rules: each must **fail when it stops being true** — including when it has been *satisfied*. One mechanism per repo, not one per task.
⚠️ Known limit: an exemption whose *reason* went stale while still formally valid is not mechanically detectable. Re-read them at each kickoff.

**S5 · Determinism primitives are validated against externally published vectors. [M0]**
Generate the reference table from an **independent implementation**, and confirm that generator reproduces the published vectors *before* writing a row. A table generated from the code under test proves only self-consistency. State your sources in the report.

**S6 · Never fill a hole with a plausible value. [M0]**
If the design docs do not authorise a number, leave it absent and greppable (`null`), and say so. A fabricated number that looks precise is worse than a missing one — `16` R6 exists for this. Never coerce such a hole to a default at read time; fail loudly.

**S7 · Add the `InMemory` fake *and* the shared contract suite in the same change as the port. [M0]**
M0's first port shipped without its A8 suite and its two implementations already disagreed on the exception type they threw. Application tests against the fake then prove nothing about the real adapter.

## Dispatch

**S8 · Paste this into every `feature-oneshot` dispatch: your review subagents report to the conductor, not to you. [M0]**
A phase result describing work as "running in the background, will report when notified" is an **incomplete turn**, not a result. Block on real commands in the foreground. In M0 this cost a manual recovery and three orphaned reviews that had found three cannot-fail blockers.

**S9 · Never quote a number from one agent's report into another agent's prompt without verifying it. [M0]**
A "98" repeated from a report was really 96, and it propagated into two dispatch prompts and six committed files, including a production doc comment.

**S10 · Agents sharing a checkout stage explicit paths. [M0]**
Never `git add -A` or `git commit -a` when another agent may be mid-edit in the same worktree. Prefer separate worktrees; when that is not possible, say so in the prompt and name the file territory explicitly.

## Planning

**S11 · Order waves by producer → consumer, never by theme. [M0]**
A task that authors data or schemas lands in an **earlier wave** than any task that validates or consumes them. M0 put the CI content-validation job and the data it validates in one wave; their composition went red on merge.

**S12 · Do not patch a component an in-flight agent is scheduled to replace. [M0]**
Queue the fix until that agent lands, or the two mechanisms will both exist. In M0 the duplicate broke 30 tests.

**S13 · Cap 3 agents in flight; raise it only for provably disjoint footprints, and record why at dispatch. [M0]**
The cap bounds merge-integration cost, which is near zero when file territories do not overlap. M0 sustained 4 with no collision — but both of M0's real collisions were about *ordering*, which the cap does not address.

**S14 · Never key a tracker edit on a spec reference. [M0]**
Spec refs are not unique — `14 §1.1` and `14 §14` each appear in two rows, and both times a status landed on an unrelated row. Address rows by task id or line, and re-read the result.

## Kickoff

**S15 · Ask which *platforms and surfaces are in scope*, not just how far to verify them. [M0]**
M0's kickoff asked how deep the iOS spike should go and never asked whether iOS ships. The design set said it did; the product owner did not think so. Five milestones of scope hung on the difference.

**S16 · Carry forward every doc contradiction a milestone surfaces, with a named owner. [M0]**
Agents reading specs closely find real conflicts (M0 found four). Each needs a ruling at the kickoff of the milestone that implements it — not a note nobody owns.
