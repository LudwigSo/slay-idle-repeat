# Steering rules

Cumulative rules distilled from milestone retros. `kickoff-milestone` reads this file at kickoff and **pastes it verbatim into every dispatched agent prompt** — so every stale or vague line here is a token tax on every future agent. Keep it short, concrete and checkable; supersede and delete rather than pile on.

Each rule carries a `[M<N>]` provenance tag. Rules that stop earning their place get deleted, not archived.

---

## Implementation

**S1 · A test that cannot fail is a defect, not a weak test. [M0, amended M1, M2]**
Before claiming a rule, guard or assertion works, **make it fail on purpose**, capture the literal output, revert, and put that output in your report. Untested guards were M0's single largest defect class — they appeared in every suite and survived per-task review.
🔒 **Revert a mutation with a targeted edit — never `git checkout -- <file>`, never `sed -i`. [M1]** Three M1 agents hit this: one discarded uncommitted review fixes and shipped a broken commit only the Debug build caught, and `sed -i` silently rewrote a file's line endings, invisible to `git diff --stat`. "Commit before you break things" is necessary but not sufficient, because review fixes land between the commit and the mutation.
🔒 **One passing mutation proves a rule is not *vacuous*. It does not prove it is correctly *scoped*. [M2]** Probe every rule with **two different shapes plus a negative control** — something it must ignore. **Never use a `const` as a probe:** the compiler folds it to a literal, so no IL reference exists and the proof passes for the wrong reason.
M2 shipped **nine** cannot-fail tests and **every one was caught by a second probe, never the first**: an `IsStatic && !IsInitOnly` filter that skipped a `static readonly Dictionary`; a `callvirt set_Item` an IL scan could not see; Cecil resolving a `TypeSpecification` past a wrapped array; a rule stated over a field's *type*, so `int` was immutable and passed; an assertion at the fight's last tick, proving a stun had *ended*; an override that was a no-op on a conventionally indexed roster; `Should.NotThrow` over `Enum.GetValues`, satisfied by one `default` arm; a duplicate-id fixture under introsort's 16-element stability threshold; and a defect invisible at 1.0 ASPD that only appeared at 2.0. Choose probe values that can **discriminate**.

**S2 · Assert the identity, not the symptom. [M0]**
When several rules can produce the same error code, pin **which rule fired** — the pointer, location or message fragment — not just the code. In M0, deleting the exact schema bound a test's name cited still passed, because a different rule fired elsewhere. Uncapped ad rewards would have shipped green.

**S3 · Every reflection- or metadata-driven rule needs a floor on its subject set. [M0]**
A rule whose subject set can silently become empty passes forever. Assert a minimum count, or that a known member is present. Renaming one namespace prefix in M0 would have turned five architecture rules permanently green.

**S4 · A declared exception must expire by itself. [M0]**
Empty-suite exemptions, baseline entries, awaiting-content schemas, deliberately-vacuous rules: each must **fail when it stops being true** — including when it has been *satisfied*. One mechanism per repo, not one per task.
🔒 **An expiry check must test that the owner is still OPEN, not that the owner EXISTS. [M4]** The tunable-marker baseline's test asserts each entry's `closedBy` task id appears in the tracker — so an entry whose owner already shipped stays green forever. **Eight** entries name already-✅ tasks; two named M4-04, which merged *inside the milestone that was supposed to close them*. This is S4's own failure mode occurring inside the mechanism built to prevent S4 failures. Any register keyed on a task id needs the status, not just the id.
⚠️ Known limit: an exemption whose *reason* went stale while still formally valid is not mechanically detectable. Re-read them at each kickoff.

**S5 · Determinism primitives are validated against externally published vectors. [M0]**
Generate the reference table from an **independent implementation**, and confirm that generator reproduces the published vectors *before* writing a row. A table generated from the code under test proves only self-consistency. State your sources in the report.

**S6 · Never fill a hole with a plausible value. [M0]**
If the design docs do not authorise a number, leave it absent and greppable (`null`), and say so. A fabricated number that looks precise is worse than a missing one — `16` R6 exists for this. Never coerce such a hole to a default at read time; fail loudly.

**S7 · Add the `InMemory` fake *and* the shared contract suite in the same change as the port. [M0]**
M0's first port shipped without its A8 suite and its two implementations already disagreed on the exception type they threw. Application tests against the fake then prove nothing about the real adapter.

**S17 · Compare aggregate state by canonical bytes, never by record equality. [M1]**
A synthesized record `Equals` compares `IReadOnlyDictionary`/`IReadOnlyList` components **by reference**, and a `ToSnapshot()` that copies a non-empty map allocates a fresh one each call. Both of M1's Criticals and one latent bug are this shape — including a guard that accused handlers of writing a run nobody had written, green everywhere because the empty-map singleton was the only case any fixture built. `14` §16.6's writer is the one encoding whose contract is "two states differing in anything encode differently".

**S18 · A `const` reference leaves no trace, so no IL rule can see it. [M1]**
The compiler inlines a `const` as a literal and folds `SomeConst + "."` into a single literal. Four sightings in three mechanisms: a layering mutation that *passed*; a `Model` type able to read tuning across a forbidden edge; **62 of 63 rules passing while `Core` referenced `Contracts`**; and a meta-rule reporting a constant as read by nothing when its only reader used it in a concatenation. Any rule that matches IL operands needs a source arm or a prefix match, and must say which spellings it does **not** close.
🔒 **Same defect, independently rediscovered in M2:** never use a `const` as an S1 mutation probe either — see S1's M2 amendment above. Two milestones hitting the identical blind spot from opposite directions (a layering rule, then a probe) makes this a defect class, not an incident.

**S19 · The task that first populates a rule's subject set owns proving that rule bites — arm by arm. [M1]**
Five M1 tasks found live M0-authored defects the moment they gave a rule real subjects; every one would have gone **green**, not red. A rule that is 90 % live reads as "live" in every summary. State which *arm* each mutation exercised: M1-11's first mutation demonstrated the arm that had been asserting since M0, and it said so rather than claiming the win.

**S24 · A kind that resolves via its own follow-up command must clear the pending state as the last step of that command's handler. [M3]**
Same shape every single-command tile kind already used — but two different M3 tasks independently omitted it for their own kind (Minigame's handler recorded the resolution but never cleared `Run`'s pending-tile fields; Portal's own RESOLVE_TILE branch was a no-op that never got there at all). Both left the run permanently unable to legally act again once it landed on that kind. Only the milestone-review cross-task pass caught it — no per-task acceptance test was shaped to ask "can the run *leave* this tile." Any task adding a "lands on X, needs its own follow-up command" resolver should treat "clear the pending state once the follow-up command lands" as a standing checklist item, not something to derive fresh.

**S25 · A seam whose only caller is deferred is untested by construction. Build the fixture that reaches it anyway. [M4]**
M4's Critical: `MERGE` and `SALVAGE` destroyed items through `Inventory.Remove` and never cleared the loadout slot, so destroying worn gear threw `InvalidOperationException` out of `Apply` on a legal command. `Player.DiscardItem` — documented as *"the seam every destructive item operation must use"* — had **zero production callers**.
**Every part of the process worked and it still shipped.** M4-10 wrote the obligation into `GapRegister` and named M4-04 as owner; M4-04 shipped all three destructive handlers without it; nothing went red, because `EQUIP` was `Deferred` for the whole milestone so no command could fill a slot and no fixture reached the state; then **M7-00d wired `EQUIP` in the next milestone** and made it live. A register records an obligation — it does not enforce one.
**So:** when you write a rule whose triggering state no shipped command can reach, the deliverable is not the note — it is a **fixture that constructs the state directly** (M4's fix added `ForgeWorlds.Wearing(...)`, three lines) and a case per handler that would break it. If you genuinely cannot construct it, that is a Critical finding to report, not a comment to leave. Corollary: `git grep` your own new seam for production callers before claiming it is in use.

**S27 · A tuning key that feeds an ordinal predicate must be named for the ordinal. [M4]**
`consecutiveMissesBeforeForce: 6` and `guaranteeAfterConsecutiveMisses: 4` were both handed straight to `HardPity.Fires`, whose contract is `misses >= everyNth - 1` — so both named the opposite of what they meant, and the two readings differ by exactly one drop on a **fairness guarantee**. A balance author editing the JSON is off by one; a reviewer reading the JSON "fixes" the code. M4's code review did propose exactly that `+1`, which would have moved a live guarantee by a draw — the design text (`24` §4.3 D1, *"On the **6th**, force"*) is the authority, not the key name. Name it `forceOnNthKill` / `guaranteeOnNthPick`. The same milestone got it right one file over: `DraftGuarantees` passes `+1` for its two *count*-named keys and none for its ordinal-named one.

## Dispatch

**S8 · Block on your review subagents in the foreground. A backgrounded review is not a result. [M0, rewritten M1, amended M2]**
Never end a phase — or a turn — with "reviews are running, will report when notified": that is an **incomplete turn**, not a result. Run each review as a foreground command, wait for its findings, apply them, then re-run the suites. **You may not end your turn while a subagent you spawned is still running.**
🔒 **[M1] This is a check at a call site, not a claim to evaluate about your own turn.** The old wording described the *symptom* ("a report saying work is running in the background"). An M1 agent had it pasted three times in one prompt, wrote *"per S8, I must block"* in its own reasoning immediately before spawning three background reviews, and ended its turn anyway — because the tool result says "You will be notified automatically", which reads like a guarantee of resumption. Its own verdict: the call-site form would have caught it.
🔒 **[M2] It happened again, with this rule pasted verbatim into the agent's prompt** — because the wording ("your review subagents report to the conductor, not to you") was written in the *conductor's* voice, and read by the agent it was addressed to, it licensed exactly the backgrounding it meant to forbid. The recovered reviews found both of that task's anti-cache architecture rules unable to catch a cache. **A rule whose headline can be read as permission for the thing it forbids is a defect in the rule.**
*(Conductors: paste this into every `feature-oneshot` dispatch.)*

**S9 · Never quote a number from one agent's report into another agent's prompt without verifying it. [M0]**
A "98" repeated from a report was really 96, and it propagated into two dispatch prompts and six committed files, including a production doc comment.
🔒 **It covers prose too, and it binds the conductor. [M1]** M1's conductor shipped four: two wrong test counts, a "~2.3×" that was 1.25×–1.58×, and — worst — telling two agents a doc question was *settled* when the authoritative site withheld a verdict. Never state a ruling landed without re-reading the site that owns it.
🔒 **A `§` reference is a claim about a document. Resolve it there before writing it. [M4]** `12 §66`, `14 §662` and `09 §53` are **line numbers written as section references** — `12` stops at §9.1, `14` at §16.6, `09` at §8. They reached 20 sites: production doc comments, test failure messages, an architecture rule's licence string, and a design document. Nothing checks that a §ref resolves, and a plausible one is never questioned again.

**S10 · Agents sharing a checkout stage explicit paths. [M0]**
Never `git add -A` or `git commit -a` when another agent may be mid-edit in the same worktree. Prefer separate worktrees; when that is not possible, say so in the prompt and name the file territory explicitly.
🔒 **A file-territory split is NOT a substitute for a worktree when an agent runs S1 mutations. [M4]** M4's review conductor put two fix agents in one checkout with a clean territory split, and it failed anyway: an S1 probe is a **temporary edit to production code**, and a probe on `ChestPickGuarantee.GuaranteeFires => true` (a gold chest on *every* pick) and one on `PresetTuning.FirstSlot` both landed in the other agent's exclusive files, each announced by the harness as an intentional change not to revert. **Concurrent S1 mutation ⇒ separate worktrees, no exception.** And the recovery is not a test run: a green suite cannot see a probe reverted and re-applied between runs, so after any shared-checkout episode, **read the production diff line by line** and audit BOM/line endings — `.gitattributes text=auto` normalises endings but not byte-order marks.

**S23 · Make every lane AWARE of a shared version counter. Never pre-assign its values. [M3, superseded M4]**
`SnapshotSchema.SchemaVersion` (or any one-number-for-the-whole-aggregate counter) collided twice in M3 — M3-03 vs. M3-02 (both bumped 3→4) and M3-13 vs. M3-06 (both 6→7) — because each lane's branch was cut before its sibling merged and both claimed "next".
🔒 **M4 tried to fix that by pre-assigning a number per task, and the mechanism was NOT IMPLEMENTABLE.** `SnapshotFieldOrderPinTests` iterates `Enumerable.Range(1, SchemaVersion)` and demands a pin section for **every intervening version**, so a task jumping to a reserved-but-higher number must invent three other tasks' serialisation layouts. `GapRegister` already said so in as many words *before* the kickoff was written — S21's exact shape, committed by the conductor.
**The rule that actually works: a bumping task can only ever take `current + 1`; numbers settle at MERGE ORDER, and the conductor renumbers at merge if two lanes both bump.** What to keep from the original is the *awareness*, which did its job across five M4 lanes: tell each prompt that a shared counter exists, that a sibling may be moving it, and to report its number prominently and unprompted. Every M4 agent did, and no collision went silent.

**S20 · Re-read `STEERING.md` at the start of every kickoff turn, including resumes, and diff it against what you last pasted. [M1]**
This file is edited *between* dispatches — by the product owner, or by a retro. M1's conductor read it once at kickoff and pasted that snapshot into eight later dispatches, carrying a rule the owner had deleted hours earlier and missing one they had added about the conductor's own behaviour. A stale paste is invisible to every downstream agent.
⚠️ **M2 could not honour this in real time**: it ran as a milestone session parallel to an unfinished M1, so its own retro forked this file before M1's retro (S17–S20 above) had landed on `main`, and the merge that reconciled the two happened after both were done. This is the corollary the rule didn't anticipate — re-reading only helps when the two retros are sequential, not concurrent. No mechanical fix proposed; recorded as a known limit for the next time two milestones run in parallel.

## Planning

**S11 · Order by producer → consumer, never by theme. [M0]**
A task that authors data or schemas starts **before** any task that validates or consumes them. M0 put the CI content-validation job and the data it validates in one wave; their composition went red on merge.

**S12 · Do not patch a component an in-flight agent is scheduled to replace. [M0]**
Queue the fix until that agent lands, or the two mechanisms will both exist. In M0 the duplicate broke 30 tests.

**S13 · Wave by *directory*, not only by dependency. Two tasks in one directory is a serialisation decision. [M0, superseded M2]**
*(Replaces M0's "cap 3 agents in flight". That was the right instinct — bound merge-integration cost — aimed at the wrong unit: the cost tracks shared **territory**, not agent count.)*
M2's numbers: the three largest changes of the milestone — **58, 60 and 18 files — merged alone with zero conflicts and zero fixes**. The four that shared `Rules/Combat/` cost **eight hand-resolutions**, six in one merge.
🔒 **And they were not merge conflicts.** Git merged five of the six cleanly and the result then failed to compile, or compiled and failed an architecture rule: two agents independently consolidated one rule into two different primitives; one made a field derived and broke four sibling call sites; one widened a signature and broke a sibling's fixture; two rules fired correctly on types written in the same wave. **Textual merging succeeds where semantic merging does not — only the compiler and the architecture rules catch the difference.** Before widening a wave, ask what directory each task lands in, not just what it depends on.

**S13b · A milestone run does not end at a lane boundary. [M0]**
After each merge, re-check every lane for a task whose own predecessor has now landed and dispatch it in the same turn. Ending the run with dispatchable work left — because something landed cleanly, or context got long — is an incomplete run, not a checkpoint.

🔒 **S13c · Plan in LANES, not waves. Parallelise as much of a milestone as the dependencies allow. [M1]**
A **wave is a barrier**: every task in it must land before the next starts. A **lane is a dependency chain that runs independently**; lanes run concurrently, and a task starts the moment *its own* predecessor lands — not when a batch does. The difference is pure wasted wall-clock, and M1 paid it eleven times: `M1-10` (energy math) depended on nothing M1 built and sat in the third wave purely for batch alignment, while `M1-02` waited on a barrier rather than on `M1-06`, the one task it actually needed.
**Two axes, and only one is a real constraint:**
- **Dependency** — hard. Task B names a type task A declares. Serialise.
- **File contention** — soft. Two tasks editing one file is a *merge* problem, not an ordering one. Resolve it by giving each a **distinct edit anchor** in the prompt and let git merge them: M1 did exactly this for `SubjectSetFloorTests` and the merge was clean. Serialising for contention is how M1 turned a 5-deep dependency graph into an 11-step queue.
Record the lane map at kickoff — lane → tasks in order → what each waits on — and re-read it after every merge (S13b).
⚠️ S13's directory-territory signal (above) is the finer-grained cousin of this rule's file-contention axis: two lanes can be dependency-independent and still share enough directory territory to cost a hand-resolution at merge. Treat directory overlap as a third planning input, not a reason to abandon lanes.

**S14 · Never key a tracker edit on a spec reference. [M0]**
Spec refs are not unique — `14 §1.1` and `14 §14` each appear in two rows, and both times a status landed on an unrelated row. Address rows by task id or line, and re-read the result.

## Kickoff

**S15 · Ask which *platforms and surfaces are in scope*, not just how far to verify them. [M0]**
M0's kickoff asked how deep the iOS spike should go and never asked whether iOS ships. The design set said it did; the product owner did not think so. Five milestones of scope hung on the difference.

**S16 · Carry forward every doc contradiction a milestone surfaces, with a named owner. [M0]**
Agents reading specs closely find real conflicts (M0 found four). Each needs a ruling at the kickoff of the milestone that implements it — not a note nobody owns.

**S22 · A "nothing is deferred" or "fully in scope" kickoff ruling must be checked against every existing ⬜ tracker row touching the same area, not just against the tasks selected for this milestone. [M3]**
M3's kickoff decision #2 ruled the tile vocabulary "fully live from Chapter 1, nothing deferred to M11 for the tile vocabulary itself" — true of the *resolver types*, but two tile kinds (Shop, Dice Forge) still needed already-scheduled, not-yet-started tracker rows (`M3-08b`, `M3-11`) to become actually reachable mid-run, and the ruling never checked against those rows before being stated as settled. It reached the milestone-review's cross-task-consistency pass before anyone caught the gap between "the resolver type exists" and "a run can legally act after landing on it." At the moment a kickoff ruling claims completeness for some area, grep the tracker for every ⬜ row whose description touches that same area, not only the rows already inside this milestone's task list.

**S21 · Before ruling against an apparent gap, check what the repo has already decided about it. [M2]**
A conductor ruling is pasted into dispatches as *authority*, so a wrong one propagates faster than any agent's mistake and is harder to challenge. S4 makes you re-read declared **exemptions** at each kickoff; nothing makes you re-read committed **classifications**, and that is the gap.
In M2 I ruled that effects should be referenced by id — taking a real finding and jumping to a solution **without checking that M2-01's committed `ContentLoader.VocabularySchemas` said the opposite in as many words** (*"an effect is never a file"*). It reached two dispatches before an agent challenged it with quotations. It cost nothing **only because the wave order happened to put the engine before the data**.
Corollary, same root: **your claims about repo state are as unreliable as a number quoted from a report.** Eight of M2's ten S9 catches were against the conductor's own prompts, and **every one was a claim about the repo** — a file's namespace, which task owns a baseline entry, how many sources exist — never a design number, of which ~100 were transcribed correctly. Say *"verify this against the repo; if it disagrees, the repo wins"* and mean it.
