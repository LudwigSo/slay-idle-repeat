# Steering rules

Cumulative rules distilled from milestone retros. `kickoff-milestone` reads this file at kickoff and **pastes it verbatim into every dispatched agent prompt** — so every stale or vague line here is a token tax on every future agent. Keep it short, concrete and checkable; supersede and delete rather than pile on.

Each rule carries a `[M<N>]` provenance tag. Rules that stop earning their place get deleted, not archived.

---

## Implementation

**S1 · A test that cannot fail is a defect, not a weak test. [M0, amended M2]**
Before claiming a rule, guard or assertion works, **make it fail on purpose**, capture the literal output, revert, and put that output in your report. Untested guards were M0's single largest defect class — they appeared in every suite and survived per-task review.
🔒 **One passing mutation proves a rule is not *vacuous*. It does not prove it is correctly *scoped*.** Probe every rule with **two different shapes plus a negative control** — something it must ignore. **Never use a `const` as a probe:** the compiler folds it to a literal, so no IL reference exists and the proof passes for the wrong reason.
M2 shipped **nine** cannot-fail tests and **every one was caught by a second probe, never the first**: an `IsStatic && !IsInitOnly` filter that skipped a `static readonly Dictionary`; a `callvirt set_Item` an IL scan could not see; Cecil resolving a `TypeSpecification` past a wrapped array; a rule stated over a field's *type*, so `int` was immutable and passed; an assertion at the fight's last tick, proving a stun had *ended*; an override that was a no-op on a conventionally indexed roster; `Should.NotThrow` over `Enum.GetValues`, satisfied by one `default` arm; a duplicate-id fixture under introsort's 16-element stability threshold; and a defect invisible at 1.0 ASPD that only appeared at 2.0. Choose probe values that can **discriminate**.

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

**S8 · Block on your review subagents in the foreground. A backgrounded review is not a result. [M0, amended M2]**
Never end a phase — or a turn — with "reviews are running, will report when notified": that is an **incomplete turn**, not a result. Run each review as a foreground command, wait for its findings, apply them, then re-run the suites. **You may not end your turn while a subagent you spawned is still running.**
In M0 this cost a manual recovery and three orphaned reviews that had found three cannot-fail blockers. In M2 it happened again **with this rule pasted verbatim into the agent's prompt** — because the old wording ("your review subagents report to the conductor, not to you") was written in the *conductor's* voice, and read by the agent it was addressed to, it licensed exactly the backgrounding it meant to forbid. The recovered reviews found both of that task's anti-cache architecture rules unable to catch a cache. **A rule whose headline can be read as permission for the thing it forbids is a defect in the rule.**
*(Conductors: paste this into every `feature-oneshot` dispatch.)*

**S9 · Never quote a number from one agent's report into another agent's prompt without verifying it. [M0]**
A "98" repeated from a report was really 96, and it propagated into two dispatch prompts and six committed files, including a production doc comment.

**S10 · Agents sharing a checkout stage explicit paths. [M0]**
Never `git add -A` or `git commit -a` when another agent may be mid-edit in the same worktree. Prefer separate worktrees; when that is not possible, say so in the prompt and name the file territory explicitly.

## Planning

**S11 · Order waves by producer → consumer, never by theme. [M0]**
A task that authors data or schemas lands in an **earlier wave** than any task that validates or consumes them. M0 put the CI content-validation job and the data it validates in one wave; their composition went red on merge.

**S12 · Do not patch a component an in-flight agent is scheduled to replace. [M0]**
Queue the fix until that agent lands, or the two mechanisms will both exist. In M0 the duplicate broke 30 tests.

**S13 · Wave by *directory*, not only by dependency. Two tasks in one directory is a serialisation decision. [M0, superseded M2]**
*(Replaces M0's "cap 3 agents in flight". That was the right instinct — bound merge-integration cost — aimed at the wrong unit: the cost tracks shared **territory**, not agent count.)*
M2's numbers: the three largest changes of the milestone — **58, 60 and 18 files — merged alone with zero conflicts and zero fixes**. The four that shared `Rules/Combat/` cost **eight hand-resolutions**, six in one merge.
🔒 **And they were not merge conflicts.** Git merged five of the six cleanly and the result then failed to compile, or compiled and failed an architecture rule: two agents independently consolidated one rule into two different primitives; one made a field derived and broke four sibling call sites; one widened a signature and broke a sibling's fixture; two rules fired correctly on types written in the same wave. **Textual merging succeeds where semantic merging does not — only the compiler and the architecture rules catch the difference.** Before widening a wave, ask what directory each task lands in, not just what it depends on.

**S14 · Never key a tracker edit on a spec reference. [M0]**
Spec refs are not unique — `14 §1.1` and `14 §14` each appear in two rows, and both times a status landed on an unrelated row. Address rows by task id or line, and re-read the result.

## Kickoff

**S15 · Ask which *platforms and surfaces are in scope*, not just how far to verify them. [M0]**
M0's kickoff asked how deep the iOS spike should go and never asked whether iOS ships. The design set said it did; the product owner did not think so. Five milestones of scope hung on the difference.

**S17 · Before ruling against an apparent gap, check what the repo has already decided about it. [M2]**
A conductor ruling is pasted into dispatches as *authority*, so a wrong one propagates faster than any agent's mistake and is harder to challenge. S4 makes you re-read declared **exemptions** at each kickoff; nothing makes you re-read committed **classifications**, and that is the gap.
In M2 I ruled that effects should be referenced by id — taking a real finding and jumping to a solution **without checking that M2-01's committed `ContentLoader.VocabularySchemas` said the opposite in as many words** (*"an effect is never a file"*). It reached two dispatches before an agent challenged it with quotations. It cost nothing **only because the wave order happened to put the engine before the data**.
Corollary, same root: **your claims about repo state are as unreliable as a number quoted from a report.** Eight of M2's ten S9 catches were against the conductor's own prompts, and **every one was a claim about the repo** — a file's namespace, which task owns a baseline entry, how many sources exist — never a design number, of which ~100 were transcribed correctly. Say *"verify this against the repo; if it disagrees, the repo wins"* and mean it.

**S16 · Carry forward every doc contradiction a milestone surfaces, with a named owner. [M0]**
Agents reading specs closely find real conflicts (M0 found four). Each needs a ruling at the kickoff of the milestone that implements it — not a note nobody owns.
