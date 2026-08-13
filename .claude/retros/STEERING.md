# Steering rules

Cumulative rules distilled from milestone retros. `kickoff-milestone` reads this file at kickoff and **pastes it verbatim into every dispatched agent prompt** — so every stale or vague line here is a token tax on every future agent. Keep it short, concrete and checkable; supersede and delete rather than pile on.

Each rule carries a `[M<N>]` provenance tag. Rules that stop earning their place get deleted, not archived.

---

## Implementation

**S1 · A test that cannot fail is a defect, not a weak test. [M0]**
Before claiming a rule, guard or assertion works, **make it fail on purpose**, capture the literal output, revert, and put that output in your report. Untested guards were M0's single largest defect class — they appeared in every suite and survived per-task review.
🔒 **Revert a mutation with a targeted edit — never `git checkout -- <file>`, never `sed -i`. [M1]** Three M1 agents hit this: one discarded uncommitted review fixes and shipped a broken commit only the Debug build caught, and `sed -i` silently rewrote a file's line endings, invisible to `git diff --stat`. "Commit before you break things" is necessary but not sufficient, because review fixes land between the commit and the mutation.

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

**S17 · Compare aggregate state by canonical bytes, never by record equality. [M1]**
A synthesized record `Equals` compares `IReadOnlyDictionary`/`IReadOnlyList` components **by reference**, and a `ToSnapshot()` that copies a non-empty map allocates a fresh one each call. Both of M1's Criticals and one latent bug are this shape — including a guard that accused handlers of writing a run nobody had written, green everywhere because the empty-map singleton was the only case any fixture built. `14` §16.6's writer is the one encoding whose contract is "two states differing in anything encode differently".

**S18 · A `const` reference leaves no trace, so no IL rule can see it. [M1]**
The compiler inlines a `const` as a literal and folds `SomeConst + "."` into a single literal. Four sightings in three mechanisms: a layering mutation that *passed*; a `Model` type able to read tuning across a forbidden edge; **62 of 63 rules passing while `Core` referenced `Contracts`**; and a meta-rule reporting a constant as read by nothing when its only reader used it in a concatenation. Any rule that matches IL operands needs a source arm or a prefix match, and must say which spellings it does **not** close.

**S19 · The task that first populates a rule's subject set owns proving that rule bites — arm by arm. [M1]**
Five M1 tasks found live M0-authored defects the moment they gave a rule real subjects; every one would have gone **green**, not red. A rule that is 90 % live reads as "live" in every summary. State which *arm* each mutation exercised: M1-11's first mutation demonstrated the arm that had been asserting since M0, and it said so rather than claiming the win.

## Dispatch

**S8 · Never call the Agent tool without `run_in_background: false`. [M0, rewritten M1]**
Your subagents' completion notifications route to whoever dispatched *you*, so a turn that ends with one in flight orphans it. Never end a phase or write a completion report while anything you spawned is running.
🔒 **This is a check at a call site, not a claim to evaluate about your own turn** — and that rewrite is the point. The old wording described the *symptom* ("a report saying work is running in the background"). An M1 agent had it pasted three times in one prompt, wrote *"per S8, I must block"* in its own reasoning immediately before spawning three background reviews, and ended its turn anyway — because the tool result says "You will be notified automatically", which reads like a guarantee of resumption. Its own verdict: the call-site form would have caught it.

**S9 · Never quote a number from one agent's report into another agent's prompt without verifying it. [M0]**
A "98" repeated from a report was really 96, and it propagated into two dispatch prompts and six committed files, including a production doc comment.
🔒 **It covers prose too, and it binds the conductor. [M1]** M1's conductor shipped four: two wrong test counts, a "~2.3×" that was 1.25×–1.58×, and — worst — telling two agents a doc question was *settled* when the authoritative site withheld a verdict. Never state a ruling landed without re-reading the site that owns it.

**S10 · Agents sharing a checkout stage explicit paths. [M0]**
Never `git add -A` or `git commit -a` when another agent may be mid-edit in the same worktree. Prefer separate worktrees; when that is not possible, say so in the prompt and name the file territory explicitly.

**S20 · Re-read `STEERING.md` at the start of every kickoff turn, including resumes, and diff it against what you last pasted. [M1]**
This file is edited *between* dispatches — by the product owner, or by a retro. M1's conductor read it once at kickoff and pasted that snapshot into eight later dispatches, carrying a rule the owner had deleted hours earlier and missing one they had added about the conductor's own behaviour. A stale paste is invisible to every downstream agent.

## Planning

**S11 · Order by producer → consumer, never by theme. [M0]**
A task that authors data or schemas starts **before** any task that validates or consumes them. M0 put the CI content-validation job and the data it validates in one wave; their composition went red on merge.

**S12 · Do not patch a component an in-flight agent is scheduled to replace. [M0]**
Queue the fix until that agent lands, or the two mechanisms will both exist. In M0 the duplicate broke 30 tests.

**S13 · No cap on agents in flight. [M0]**
M0's 3-agent cap bought nothing: it sustained 4 with no collision, and both of M0's real collisions were about *ordering* (S11) and *duplicate mechanisms* (S12), which a headcount does not address. Dispatch everything that is dispatchable, at once.

**S13b · A milestone run does not end at a lane boundary. [M0]**
After each merge, re-check every lane for a task whose own predecessor has now landed and dispatch it in the same turn. Ending the run with dispatchable work left — because something landed cleanly, or context got long — is an incomplete run, not a checkpoint.

🔒 **S13c · Plan in LANES, not waves. Parallelise as much of a milestone as the dependencies allow. [M1]**
A **wave is a barrier**: every task in it must land before the next starts. A **lane is a dependency chain that runs independently**; lanes run concurrently, and a task starts the moment *its own* predecessor lands — not when a batch does. The difference is pure wasted wall-clock, and M1 paid it eleven times: `M1-10` (energy math) depended on nothing M1 built and sat in the third wave purely for batch alignment, while `M1-02` waited on a barrier rather than on `M1-06`, the one task it actually needed.
**Two axes, and only one is a real constraint:**
- **Dependency** — hard. Task B names a type task A declares. Serialise.
- **File contention** — soft. Two tasks editing one file is a *merge* problem, not an ordering one. Resolve it by giving each a **distinct edit anchor** in the prompt and let git merge them: M1 did exactly this for `SubjectSetFloorTests` and the merge was clean. Serialising for contention is how M1 turned a 5-deep dependency graph into an 11-step queue.
Record the lane map at kickoff — lane → tasks in order → what each waits on — and re-read it after every merge (S13b).

**S14 · Never key a tracker edit on a spec reference. [M0]**
Spec refs are not unique — `14 §1.1` and `14 §14` each appear in two rows, and both times a status landed on an unrelated row. Address rows by task id or line, and re-read the result.

## Kickoff

**S15 · Ask which *platforms and surfaces are in scope*, not just how far to verify them. [M0]**
M0's kickoff asked how deep the iOS spike should go and never asked whether iOS ships. The design set said it did; the product owner did not think so. Five milestones of scope hung on the difference.

**S16 · Carry forward every doc contradiction a milestone surfaces, with a named owner. [M0]**
Agents reading specs closely find real conflicts (M0 found four). Each needs a ruling at the kickoff of the milestone that implements it — not a note nobody owns.
