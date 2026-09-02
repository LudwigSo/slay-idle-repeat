# CI — what runs, what it enforces, and what is not on yet

Authored by **M0-02**. The requirement is `14` §14 ("CI on every push"), `14` §13
(the testing requirements CI must run) and `14` §1.1 (the no-lock-in rules CI is
made responsible for).

This file exists for one reason: **a gated-off job with no register outlives the
milestone that was supposed to turn it on.** Every `if: false` in `ci.yml` has a
row below naming the task that removes it. If a row's milestone has shipped and
the gate is still there, that is a bug in the milestone, not a detail.

> ⚠️ **Nothing here has been observed running.** The repository has no git remote
> and no GitHub repository (M0 kickoff decision 5), so no workflow has ever
> executed on a runner. What *has* been executed is the logic: every live job's
> real work lives in `build/ci/*.ps1` and was run locally against this checkout.
> See [Verification status](#verification-status).

---

## Jobs

### `ci.yml` — push to `main` / `milestone/**` / `feature-**`, and PRs to `main` / `milestone/**`

| Job | State | What it enforces | Doc source |
|---|---|---|---|
| `build` | 🟢 live | `dotnet restore` + `dotnet build SlayIdleRepeat.sln -c Release`. Warnings are errors via `Directory.Build.props`, so a new warning fails here. NuGet cached on the project files. | `14` §14 |
| `test` | 🟢 live | The unit and contract suites, discovered by glob. Fails on a suite that contains **zero** tests without a declared exemption — see [The empty-suite rule](#the-empty-suite-rule). | `14` §13 |
| `architecture-tests` | 🟢 live | `SlayIdleRepeat.Architecture.Tests` alone, in its own job. `23` §6 says these fail the build, so they are not lumped in with `test` where an unrelated flake could mask them. | `23` §6, `30` §9 |
| `vendor-package-uniqueness` | 🟢 live | Fails if a vendor `PackageReference` appears in more than one `.csproj` (**A9-UNIQUE**), or in a project that is not an adapter (**A9-LOCATION**). | `14` §1.1 🔒 |
| `server-image` | 🟢 live | Builds `src/SlayIdleRepeat.Server/Dockerfile`, starts the container, asserts it is **not running as root**, and waits for `GET /health` → 200 `{"status":"ok"}`. Build and smoke only — **no registry login, no push**. | `14` §14 |
| `compose-boot` | 🟢 live *(since M0-03)* | Asserts CI holds **no cloud credentials at all**, then `docker compose config` → `up --detach --wait` → probe `/health`, the MinIO liveness endpoint and the Prometheus scrape targets → `down`. Those probes are **CI steps, not a test suite** — there is no integration tier. The stack it boots is `docker-compose.yml` + `infra/` — see [`infra/README.md`](../../infra/README.md). | `14` §14, `14` §1.1 🔒 |
| `determinism` | 🟢 live *(since M5-12)* | **Linux x64 and Android ARM64** — the live legs; iOS ARM64 is the gated `determinism-ios` job (`16` D34). Each leg re-asserts every committed determinism table on its own architecture: M2-17's 10,000 DSL permutations, `14` §13's 1,000 parity sequences, both hash reference-vector tables and both field-order pins. ⚠️ `14` §8.2's own 10,000 `(seed, build, enemy)` → `LogHash` corpus **was removed** as a brittle self-generated table, so this job no longer covers the corpus §8.2 names — the legs still compare, over a smaller set of tables. *"x64 and ARM64 agree"* **is** *"both legs are green against the same committed tables"*. ARM64 is reached by **QEMU emulation in a `linux/arm64` container** — one line switches it to a native `ubuntu-24.04-arm` runner. `build/ci/Assert-DeterminismRun.ps1` reads the real TRX counts, so a filter matching nothing fails rather than passing. | `14` §8.2 🔒, `14` §13 |
| `determinism-observed` | 🟢 live *(since M5-12)* | Fails unless **both** live legs left `.trx` evidence behind. A matrix leg that is skipped or never scheduled leaves no red tick anywhere, and a single-architecture determinism run asserts the opposite of what it appears to. | `14` §8.2 🔒 |
| `determinism-ios` | ⛔ gated off | The third leg of `14` §8.2, on `macos-15`, gated with iOS itself (`16` D34). A job of its own rather than a matrix row: a matrix entry cannot gate itself, and gating every step instead would make it report success having run nothing. | `14` §8.2 🔒, `16` D34 |
| `android-export` | ⛔ gated off | Android debug APK **through the custom export template with the MAX plugin included** — a plain export does not produce a working ad build. | `14` §14, `12` §3.2 |
| `ios-export` | ⛔ gated off | iOS Xcode project export on a **`macos-15`** runner, asserting the build actually contains .NET (`<Assembly>_aot.xcframework` + `godot-publish-dotnet/`). macOS is not a preference: Godot 4.7.1's exporter hard-refuses .NET iOS builds off macOS. | `14` §14, `12` §3.2 |

---

## The gated-off register

| Job | Gate | What turns it on | Why it is not `echo TODO && exit 0` |
|---|---|---|---|
| `determinism-ios` | `if: false` | ⚠️ **Nothing in the current plan** — it turns on with iOS itself (**D34**), and it is the *first* leg to re-enable if iOS returns: NativeAOT is a different **runtime** from the CoreCLR path the two live legs exercise, so it is the leg most likely to disagree and the one currently asserting nothing. On the day it is re-enabled, `ios-arm64` must also be added to `determinism-observed`'s expected list — otherwise this leg can fail and the guard will still pass. | A determinism job that passes without comparing hashes across architectures asserts the exact opposite of `14` §8.2 — which is why the *live* two legs are not gated, why neither of them trusts an exit code, and why `determinism-observed` fails when a leg leaves no evidence behind. |
| `android-export` | `if: false` | **M7-10** (client CI), which needs **M7-01** to turn `SlayIdleRepeat.Client` into a real Godot project. It is a plain `net8.0` class library today so the solution builds without Godot installed. The recipe is spike **O23** (M0-05a) — M7-10 implements that write-up rather than re-researching it. | `12` §3.2 is explicit that a plain export yields an APK where ads do not work. A green tick over a fake export would hide precisely the failure the job exists to catch. |
| `ios-export` | `if: false` | ⚠️ **Nothing in the current plan.** **D34** (M0 review) descopes iOS to post-launch: Android ships alone. This job and the `ios-arm64` determinism leg stay **authored and gated off rather than deleted**, because D34 is a shipping decision, not an architectural one, and its binding four-part reopen condition should be a one-line change to act on. The iOS recipe (M0-05b) — unlike the Android one — **has never been run**: O23 closes on the iOS side by descoping, not by evidence. Whoever reopens iOS must expect to debug that document, not transcribe it. | Same argument, sharper: iOS is the half of O23 that `16` flagged as *especially* risky. It also fails vacuously in a second way the others do not — a missing `.sln` makes Godot skip the .NET publish and still emit a buildable Xcode project with **zero managed code**, so the job must assert on `<Assembly>_aot.xcframework` and `godot-publish-dotnet/`, never on the exit code. |

All three report **skipped**, not success. None runs `exit 0` over an empty step.

One consequence worth stating: a *skipped* job is not a *green* job, and neither is a
running job that asserted nothing. That is what `determinism-observed` exists for — see
its row above.

Two notes for whoever turns the macOS jobs on:

- **`macos-15` is the pin, deliberately.** `macos-14` is marked *deprecated* in
  [`actions/runner-images`](https://github.com/actions/runner-images) as of the
  2026-07 image manifest. The `determinism` job's `ios-arm64` leg named
  `macos-14` until the M0 review moved it to `macos-15` — the deadline was
  ~3 months out and recorded only as prose sixty lines from the job that broke
  it. Both macOS jobs are `if: false`, so nothing was red; a dormant job that is
  wrong is worse than one that is right, because whoever turns it on will be
  doing determinism work, not auditing runner images.
- **macOS runners bill at a 10× minute multiplier** on private repositories, and
  the Godot mono export templates are ~1.1 GB to fetch. Neither macOS job should
  inherit this workflow's every-push trigger without someone deciding to pay for
  it. Cache the templates and NuGet, and consider restricting to `main` /
  `milestone/**` / `workflow_dispatch`.

## Jobs that are red today, on purpose

**None.** The entry this section carried during M0 has since gone green, and it was not made to pass by weakening it:

- **`compose-boot`** was red while `docker-compose.yml` did not exist; the job checked for it explicitly so the failure read as "M0-03 has not landed" rather than Docker's bare `no configuration file provided`. **M0-03** landed the stack, and the sequence was walked locally against Docker 28.4.0 — `config` → `up -d --wait` (all 8 services healthy, 2 init containers completed) → `/health` → 200 `{"status":"ok"}` → integration suite 3/3 → `down -v`.

Keep this section honest: a job that is red for a *planned* reason belongs here with the task that clears it. A job that is red for any other reason is a defect, not a row.

---

## The empty-suite rule

`dotnet test` **exits 0 on an assembly containing zero tests** — verified on this
repository on 2026-08-11: an empty suite produces a TRX with
`Counters total="0"` and a green exit code. A `test` job that only checks the exit
code therefore reports success for a suite that was emptied, never wired up, or
whose test adapter stopped being registered.

`build/ci/Invoke-UnitTests.ps1` reads the real per-suite count out of the TRX and
applies six rules:

1. **Test projects are discovered by glob** (`tests/*/*.csproj`). A new suite —
   `SlayIdleRepeat.Client.Tests`, or the `14` §16.6 `SchemaVersion` field-list pin
   arriving with **M0-07** — is picked up with no edit to any YAML, and is
   required to contain tests from day one.
2. **A suite may be empty only if declared** in `build/ci/test-suites.json` under
   `knownEmpty`, with the milestone that fills it.
3. **A declared-empty suite that now has tests fails the build** as a stale
   exemption. The entry cannot outlive its milestone — removing it is forced, not
   remembered.

Three further rules were added by the M0 review, all of the same kind — a
declaration that matches nothing is a declaration that changes what runs without
changing what goes red:

4. **A name in a group's `include`/`exclude` that matches no discovered suite
   fails the build.** Neither list errors on its own: a typo in `include`
   quietly shrinks the group, a typo in `exclude` quietly stops excluding.
5. **A `knownEmpty` entry naming a project that is not among the discovered
   suites fails the build** — a renamed or deleted project takes its exemption
   with it, rather than leaving one pre-armed for whatever lands on that name.
6. **A `knownEmpty` entry must carry a well-formed `turnsOn` and a non-empty
   `reason`.** An exemption with no milestone has no expiry.

### Current exemptions: **none**

`knownEmpty` in `build/ci/test-suites.json` is empty. Every suite under `tests/`
contains tests and is required to keep containing them.

All five suites **had** a row here and no longer do, and in every case the
stale-exemption rule is what forced the removal rather than anyone remembering:

- `SlayIdleRepeat.Core.Tests` — filled by M0-06 (Rng), M0-07 (the `14` §16.6
  field-order pin) and M0-09 (the Content value tree). **799 tests.**
- `SlayIdleRepeat.Application.Tests` — filled by M0-09's content services.
  **256 tests.**
- `SlayIdleRepeat.Contract.Tests` — its exemption said "there are no ports with
  two implementations yet", and M0-09 landed `IContentSourcePort` with a shared
  contract suite and two derived fixtures. **24 tests.** Its `turnsOn` was
  independently wrong: it said M1-09, and M1-09 is the `BEGIN_SESSION` handler —
  the port catalogue and the shared contract suites are M5-01. So an entry can be
  stale on its milestone as well as on its test count, and only the count is
  mechanically checkable. See `$knownGapInThisMechanism` in
  `build/ci/test-suites.json` for what these six rules deliberately do NOT catch.
- `SlayIdleRepeat.Architecture.Tests` — M0-08 merged mid-task with live rules;
  **38** as of the M0 review.
- `SlayIdleRepeat.Integration.Tests` — **deleted.** Its exemption said "needs the
  compose stack, which does not exist until M0-03"; M0-03 landed the stack and
  filled the suite with three smoke assertions. The project has since been removed
  along with the entire integration/E2E tier, and the `integration` group with it.
  Its three assertions live on as direct probes inside `compose-boot`. See
  `$noIntegrationTier` in `build/ci/test-suites.json` — **this tier is not to be
  recreated, under any name.**

---

## The scripts

Every check lives in `build/ci/` as a script a developer can run with the same
arguments the workflow uses. A check that only exists inside a YAML step is one
nobody can reproduce when it goes red.

| Script | Run it | Used by |
|---|---|---|
| `Invoke-UnitTests.ps1` | `pwsh build/ci/Invoke-UnitTests.ps1 -Group unit` | `test`, `architecture-tests`, `compose-boot` |
| `Test-VendorPackageUniqueness.ps1` | `pwsh build/ci/Test-VendorPackageUniqueness.ps1` | `vendor-package-uniqueness` |
| `Test-NoCloudCredentials.ps1` | `pwsh build/ci/Test-NoCloudCredentials.ps1` | `compose-boot` |
| `Wait-ForHttpOk.ps1` | `pwsh build/ci/Wait-ForHttpOk.ps1 -Url http://127.0.0.1:8080/health` | `server-image`, `compose-boot` |

Data files: `build/ci/test-suites.json` (suite groups + empty-suite exemptions),
`build/ci/non-vendor-packages.json` (what does not count as a vendor SDK).

PowerShell, because it is the one shell that runs identically on the Windows
development machine and on the `ubuntu-24.04` runners, where `pwsh` is
preinstalled.

### Deliberate overlap with the architecture tests

M0-08's `ProjectFileTests.Vendor_package_is_referenced_by_exactly_one_project`
asserts the same `14` §1.1 / A9 uniqueness rule. **Both are wanted, and they are
not duplicates:**

| | Architecture test (M0-08) | `Test-VendorPackageUniqueness.ps1` |
|---|---|---|
| Scope | `src/` only — test projects legitimately share xUnit | Whole repository, with an explicit allow-list |
| Location rule | Covered indirectly (`Core` references nothing, `Application` references only Core + Contracts) | **A9-LOCATION**, directly: a vendor package anywhere outside `src/adapters/**` fails, including `Server`, `Contracts` and `tests/` |
| Runs when the solution does not compile | No | Yes |
| New package appears | Passes if it is unique | Fails until it is either in an adapter or justified in `non-vendor-packages.json` |

The allow-list is the point of the second one: it makes "this is test
infrastructure, not a vendor SDK" an argument someone writes down. It already
earned its keep — M0-08's `Mono.Cecil` was caught by A9-LOCATION and is now
allow-listed with a reason.

Do not delete one because the other exists.

---

## No credentials, ever, in CI

`14` §1.1 🔒 requires the whole stack to boot on a laptop with no cloud account.
A stack that comes up *because* CI first authenticated to a cloud registry has
failed that test while reporting success — so `Test-NoCloudCredentials.ps1` runs
before `docker compose up` and fails on any of:

- a `${{ secrets.* }}` expression in any workflow
- `docker/login-action`, `aws-actions/configure-aws-credentials`,
  `google-github-actions/auth`, `azure/login`
- `docker login` or `docker push`
- `id-token: write` (OIDC federation to a cloud provider)
- an image from a private cloud registry (ECR / GCR / ACR / Artifact Registry),
  in a workflow *or* in `docker-compose.yml`
- a `${{ … }}` GitHub expression inside `docker-compose.yml` — compose must run
  verbatim on a laptop

It scans what the workflows *do*, with comment lines stripped, so a comment
explaining a prohibition is never mistaken for a violation.

Credential-shaped **variable names** in `docker-compose.yml` are deliberately not
flagged — only real cloud endpoints and real secret injection are. As landed by
M0-03 those are `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD`, `POSTGRES_PASSWORD`,
`ObjectStore__AccessKey` / `__SecretKey` and `GF_SECURITY_ADMIN_PASSWORD`: fixed,
committed, obviously-local values for containers on a laptop, all of them listed
in [`infra/README.md`](../../infra/README.md#dev-credentials). MinIO speaks the S3
API, so an S3-shaped key pair there is not a cloud account.

It scans **every** `.github/workflows/*.yml`, not a list of filenames. Until the
M0 review it named the workflow files literally, which meant renaming one — or
adding `codeql.yml` in M1 — left the new file silently never
scanned while the check still reported success. This is the one gate whose entire
value is "it looked at everything CI can reach for", so it also fails when a glob
matches nothing at all.

`14` §14's **server release** (image → registry, rolling deploy, migrations as a
pre-deploy job) genuinely needs a registry credential. When that workflow is
written it goes in **its own file**, and that file's name goes in
`Test-NoCloudCredentials.ps1`'s `-WorkflowExclusion` **with its reason** — never
by narrowing the glob, and never by relaxing a ban for a file that is CI. An
exclusion naming a workflow that does not exist fails the check, so the
exemption cannot outlive the file it was written for.

## Not in scope here

| Thing | Owner |
|---|---|
| ~~`docker-compose.yml` itself~~ | ✅ landed with M0-03 — `docker-compose.yml` + `infra/`, documented in [`infra/README.md`](../../infra/README.md) |
| ~~The `SchemaVersion` snapshot field-list pin (`14` §16.6)~~ | ✅ landed with M0-07 — `tests/SlayIdleRepeat.Core.Tests/Model/Snapshots/SnapshotFieldOrderPin.cs`, picked up by the `test` job through glob discovery with no YAML edit, exactly as this row predicted |
| ~~The determinism + parity harness~~ | ✅ landed with M5-12 — `determinism` (Linux x64 + Android ARM64) and `determinism-observed` are live; `determinism-ios` stays gated under D34. The corpora are `tests/SlayIdleRepeat.Core.Tests/Rules/Effects/Determinism/` and `tests/SlayIdleRepeat.Application.Tests/Parity/` (the `Rules/Combat/Determinism/` LogHash corpus was removed), driven by `build/ci/Assert-DeterminismRun.ps1` |
| The real Android client CI | M7-10. Recipe: spike **O23** (executed — a real signed, .NET-bearing APK through the custom-template path). **iOS is out of scope for v1** per D34; the iOS half of O23 is written up but was never executed, and reopening iOS reopens O23 |
| Server release: registry push, rolling deploy, pre-deploy migrations | Not yet scheduled — `14` §14 |
| Client release: AAB / IPA, 5 % staged rollout | Not yet scheduled — `14` §14 |
| Kill switches (remote config flags) | Runtime config, not CI — `14` §14 |

---

## Verification status

Run locally against this checkout on 2026-08-11 (Windows 10, Docker 28.4.0,
`dotnet` 8.0.319, `pwsh` 7):

| Verified by execution | Unverifiable without a runner |
|---|---|
| `dotnet restore` + `dotnet build -c Release` — 34 projects, 0 warnings, 0 errors | Every `actions/*` step (`checkout`, `setup-dotnet`, `cache`, `upload-artifact`) |
| All three test groups via `Invoke-UnitTests.ps1`, plus **both** failure modes of the empty-suite rule (undeclared-empty, and stale-exemption) proven against a throwaway suite, and all four manifest-validation failures (unmatched `include`/`exclude` name, `knownEmpty` naming a missing project, malformed `turnsOn`, empty `reason`) proven against a throwaway manifest | `global-json-file: global.json` actually selecting the 8.0 SDK on a runner |
| `Test-VendorPackageUniqueness.ps1` — passes on the real tree (34 projects, 12 with a `PackageReference`, 8 vendor SDKs); A9-UNIQUE and A9-LOCATION both proven to fire, including through a project carrying the legacy MSBuild `xmlns` that the previous namespace-sensitive XPath returned zero nodes for; the "scanned N projects and found no PackageReference at all" vacuity guard proven against a scratch tree | NuGet cache hit/miss behaviour |
| — | Runner-label availability (`ubuntu-24.04`, `macos-15`). `macos-14` is deprecated and unsupported from **2026-11-02**; no job names it any more — the M0 review moved the iOS ARM64 determinism leg to `macos-15`, and M5-12 made it a job of its own |
| `Test-NoCloudCredentials.ps1` — passes on the real workflows; all seven bans proven to fire on a fixture; the glob proven to pick up a newly-added `codeql.yml` that the previous hard-coded file list would never have read, and the stale-`-WorkflowExclusion` failure proven | `schedule:` firing, and GitHub's 60-day disable of scheduled workflows on an inactive repo |
| `docker build` of the server image, non-root uid **1654**, `GET /health` → 200 `{"status":"ok"}`, and `ASPNETCORE_HTTP_PORTS` override proving 12-factor config | Concurrency cancellation, artifact upload |
| The whole `compose-boot` command sequence (`config` → `up --wait` → health poll → `down`) against a throwaway stack in a scratch directory | |
| YAML parse + `yamllint` clean on the workflow | `actionlint` (not installed; not fetched — no unvetted binaries) |
| **Integration rehearsal**, re-run on `review/M0` at the end of the M0 review: build clean (34 projects, 0 warnings), architecture suite **38/38**, `Core.Tests` 799, `Application.Tests` 256, `Contract.Tests` 24, `Integration.Tests` 3 against a live stack, all four `build/ci/` checks exit 0, and **no job red** — the two documented reds of the earlier rehearsal were cleared by M0-03 and M0-09/M0-10, see "Jobs that are red today" above | |

### Follow-ups for when the repository exists

1. **Pin the `actions/*` steps to commit SHAs** rather than `@v4`. Not done here
   because the SHAs cannot be resolved or verified without a remote.
2. **Run `actionlint`** over the workflow, and consider adding it as a job.
3. **Branch protection**: `build`, `test`, `architecture-tests` and
   `vendor-package-uniqueness` are the checks that pass today and are safe to
   require immediately. `compose-boot` joined them with M0-03, so all five are
   now safe to require.
