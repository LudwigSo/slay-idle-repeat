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
| `content-validation` | 🟢 live | Every JSON under `SlayIdleRepeat.Data/` is validated against its schema and against the cross-file invariants: `14` §6's five failure classes (unknown IDs, missing icons, out-of-range values, orphaned references, duplicate IDs), plus malformed JSON, duplicate property names, unpaired schemas, and any JSON Schema keyword the validator does not implement. Then the 📐 audit, in three directions, against the dated baseline in `build/content/`. Runs the same code the game loads content with (M0-09). | `14` §6 🔒, `14` §13 |
| `vendor-package-uniqueness` | 🟢 live | Fails if a vendor `PackageReference` appears in more than one `.csproj` (**A9-UNIQUE**), or in a project that is not an adapter (**A9-LOCATION**). | `14` §1.1 🔒 |
| `server-image` | 🟢 live | Builds `src/SlayIdleRepeat.Server/Dockerfile`, starts the container, asserts it is **not running as root**, and waits for `GET /health` → 200 `{"status":"ok"}`. Build and smoke only — **no registry login, no push**. | `14` §14 |
| `compose-boot` | 🟢 live *(since M0-03)* | Asserts CI holds **no cloud credentials at all**, then `docker compose config` → `up --detach --wait` → wait for `/health` → integration suite → `down`. The stack it boots is `docker-compose.yml` + `infra/` — see [`infra/README.md`](../../infra/README.md). | `14` §14, `14` §13, `14` §1.1 🔒 |
| `determinism` | ⛔ gated off | Matrix shape from `14` §8.2: **Linux x64 / Android ARM64 / iOS ARM64**, 10,000 fixed `(seed, build, enemy)` triples, compare `LogHash`, fail on divergence. | `14` §8.2 🔒 |
| `android-export` | ⛔ gated off | Android debug APK **through the custom export template with the MAX plugin included** — a plain export does not produce a working ad build. | `14` §14, `12` §3.2 |

### `nightly.yml` — 03:17 UTC daily, plus manual dispatch

| Job | State | What it enforces | Doc source |
|---|---|---|---|
| `balance-harness` | 🟡 live but thin | Builds and runs `tools/BalanceHarness`, asserts exit 0. The tool is an empty shell, so this currently only catches it failing to build or starting to throw. | `14` §14, `05` §9 |
| `economy-simulator` | 🟡 live but thin | Builds and runs `tools/EconomySim`, asserts exit 0. Same shape. | `14` §14, `21` |

---

## The gated-off register

| Job | Gate | What turns it on | Why it is not `echo TODO && exit 0` |
|---|---|---|---|
| `determinism` | `if: false` | **M5-12** (cross-platform `LogHash` harness + parity test), which needs **M0-06** (`Hash64`, xxHash64 with pinned encoding) and **M0-07** (`CanonicalStateWriter`) first. | A determinism job that passes without comparing hashes across architectures asserts the exact opposite of `14` §8.2. Note for M5-12: do **not** narrow this to Linux only — §8.2 exists because ARM and x64 disagree about floating point, and a single-architecture run proves nothing. |
| `android-export` | `if: false` | **M7-10** (client CI), which needs **M7-01** to turn `SlayIdleRepeat.Client` into a real Godot project. It is a plain `net8.0` class library today so the solution builds without Godot installed. The recipe is spike **O23**, written up in `docs/spikes/O23-godot-android-export.md` (M0-05a) — M7-10 implements that document rather than re-researching it. | `12` §3.2 is explicit that a plain export yields an APK where ads do not work. A green tick over a fake export would hide precisely the failure the job exists to catch. |

Both report **skipped**, not success. Neither runs `exit 0` over an empty step.

## Jobs that are red today, on purpose

**None.** Both entries this section carried during M0 have since gone green, and neither was made to pass by weakening it:

- **`content-validation`** was red while `SlayIdleRepeat.Data/` held only `.gitkeep` files — a validator with nothing to validate reported failure rather than a green tick over an empty directory. **M0-10** landed `schema/`, `tuning/` (the 16 files of `21` §3.1), `loc/` and `content/`; **M0-09** then replaced the script's body with a call into `tools/ContentValidator`, so CI now runs the same code the game loads content with.
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
applies three rules:

1. **Test projects are discovered by glob** (`tests/*/*.csproj`). A new suite —
   `SlayIdleRepeat.Client.Tests`, or the `14` §16.6 `SchemaVersion` field-list pin
   arriving with **M0-07** — is picked up with no edit to any YAML, and is
   required to contain tests from day one.
2. **A suite may be empty only if declared** in `build/ci/test-suites.json` under
   `knownEmpty`, with the milestone that fills it.
3. **A declared-empty suite that now has tests fails the build** as a stale
   exemption. The entry cannot outlive its milestone — removing it is forced, not
   remembered.

Current exemptions, all seeded empty by M0-01:

| Suite | Filled by |
|---|---|
| `SlayIdleRepeat.Core.Tests` | M0-06 |
| `SlayIdleRepeat.Application.Tests` | M1-09 |
| `SlayIdleRepeat.Contract.Tests` | M1-09 |

Two suites **had** a row here and no longer do, and in both cases the
stale-exemption rule is what forced the removal rather than anyone remembering:

- `SlayIdleRepeat.Architecture.Tests` — M0-08 merged mid-task with 33 live rules.
- `SlayIdleRepeat.Integration.Tests` — its exemption said "needs the compose
  stack, which does not exist until M0-03". M0-03 landed the stack, so the reason
  expired. Rather than re-point the marker at M5 and leave `compose-boot`'s
  "run integration suite against the live stack" step executing zero assertions
  for five milestones, M0-03 filled the suite with its own acceptance criteria
  (`ComposeStackSmokeTests`). `14` §13's full end-to-end run still arrives with
  the adapters in M5-05 and after.

---

## The scripts

Every check lives in `build/ci/` as a script a developer can run with the same
arguments the workflow uses. A check that only exists inside a YAML step is one
nobody can reproduce when it goes red.

| Script | Run it | Used by |
|---|---|---|
| `Invoke-UnitTests.ps1` | `pwsh build/ci/Invoke-UnitTests.ps1 -Group unit` | `test`, `architecture-tests`, `compose-boot` |
| `Invoke-ContentValidation.ps1` | `pwsh build/ci/Invoke-ContentValidation.ps1` | `content-validation` |
| `Test-VendorPackageUniqueness.ps1` | `pwsh build/ci/Test-VendorPackageUniqueness.ps1` | `vendor-package-uniqueness` |
| `Test-NoCloudCredentials.ps1` | `pwsh build/ci/Test-NoCloudCredentials.ps1` | `compose-boot` |
| `Wait-ForHttpOk.ps1` | `pwsh build/ci/Wait-ForHttpOk.ps1 -Url http://127.0.0.1:8080/health` | `server-image`, `compose-boot` |

Data files: `build/ci/test-suites.json` (suite groups + empty-suite exemptions),
`build/ci/non-vendor-packages.json` (what does not count as a vendor SDK).

PowerShell, because it is the one shell that runs identically on the Windows
development machine and on the `ubuntu-24.04` runners, where `pwsh` is
preinstalled.

### Handover: `content-validation` → M0-09 · **done**

M0-02 authored `Invoke-ContentValidation.ps1` as a structural floor and asked
M0-09 to **replace the body, not the interface**. That is what happened: same
script path, same parameters, same exit codes.

The body is now a call into `tools/ContentValidator`, which runs the **same code
the game loads content with** — the loader, the JSON Schema validator, the
cross-file invariants and the 📐 audit all live in
`SlayIdleRepeat.Application/Services/Content/` and are unit-tested in
`SlayIdleRepeat.Application.Tests` against the in-memory fake. A CI-only
validator written a second time in PowerShell would drift from the runtime one,
and the day it did, CI would be green about content the game cannot load.

The pairing convention M0-02 guessed turned out to be right, so no
`schema-map.json` was needed. Two schemas govern nothing yet (`chapter`,
`event`); rather than loosening the orphan check they are named in
`ContentLoader.SchemasAwaitingContent` with the milestone that authors their
content, and the check **fails if one of them ever does govern a file** — the
exemption cannot outlive its milestone.

The one edit `ci.yml` did need: an `actions/setup-dotnet` step on the job, since
the check is .NET now rather than pure PowerShell.

📐 mismatches that exist today are recorded in
`build/content/tunable-marker-baseline.json` — dated, with a reason and a closing
milestone each. The check fails on anything that file does not record, and
equally on an entry it records that is no longer real.

### Deliberate overlap with the architecture tests

M0-08's `ProjectFileTests.Vendor_package_is_referenced_by_exactly_one_project`
asserts the same `14` §1.1 / A9 uniqueness rule. **Both are wanted, and they are
not duplicates:**

| | Architecture test (M0-08) | `Test-VendorPackageUniqueness.ps1` |
|---|---|---|
| Scope | `src/` only — test projects legitimately share xUnit | Whole repository, with an explicit allow-list |
| Location rule | Covered indirectly (`Core` references nothing, `Application` references only Core + Contracts) | **A9-LOCATION**, directly: a vendor package anywhere outside `src/adapters/**` fails, including `Server`, `Contracts`, `tools/` and `tests/` |
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

- a `${{ secrets.* }}` expression in `ci.yml` or `nightly.yml`
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

`14` §14's **server release** (image → registry, rolling deploy, migrations as a
pre-deploy job) genuinely needs a registry credential. When that workflow is
written it goes in **its own file** — `Test-NoCloudCredentials.ps1`'s
`-WorkflowGlob` keeps pointing at the CI workflows only, and must never be
relaxed to let CI itself log in.

## Not in scope here

| Thing | Owner |
|---|---|
| ~~`docker-compose.yml` itself~~ | ✅ landed with M0-03 — `docker-compose.yml` + `infra/`, documented in [`infra/README.md`](../../infra/README.md) |
| ~~The real content-validation harness~~ | ✅ landed with M0-09 — `tools/ContentValidator` over `SlayIdleRepeat.Application/Services/Content/`, so CI runs the same code the game loads content with |
| The `SchemaVersion` snapshot field-list pin (`14` §16.6) | M0-07 — it lands as a test and the `test` job picks it up automatically via glob discovery |
| The determinism + parity harness | M5-12 |
| The real Android/iOS client CI | M7-10; the iOS recipe is M0-05b |
| Server release: registry push, rolling deploy, pre-deploy migrations | Not yet scheduled — `14` §14 |
| Client release: AAB / IPA, 5 % staged rollout | Not yet scheduled — `14` §14 |
| Kill switches (remote config flags) | Runtime config, not CI — `14` §14 |

---

## Verification status

Run locally against this checkout on 2026-08-11 (Windows 10, Docker 28.4.0,
`dotnet` 8.0.319, `pwsh` 7):

| Verified by execution | Unverifiable without a runner |
|---|---|
| `dotnet restore` + `dotnet build -c Release` — 33 projects, 0 warnings, 0 errors | Every `actions/*` step (`checkout`, `setup-dotnet`, `cache`, `upload-artifact`) |
| All three test groups via `Invoke-UnitTests.ps1`, plus **both** failure modes of the empty-suite rule (undeclared-empty, and stale-exemption) proven against a throwaway suite | `global-json-file: global.json` actually selecting the 8.0 SDK on a runner |
| `Test-VendorPackageUniqueness.ps1` — passes on the real tree; A9-UNIQUE and A9-LOCATION both proven to fire | NuGet cache hit/miss behaviour |
| `Invoke-ContentValidation.ps1` — pass, bad-parse, duplicate-key and both orphan directions proven on fixtures. **Superseded by M0-09**: the body now calls `tools/ContentValidator`, and the schema-awaiting-content declaration moved from `schema-map.json` into `ContentLoader.SchemasAwaitingContent` | Runner-label availability (`ubuntu-24.04`, `macos-14`) |
| `Test-NoCloudCredentials.ps1` — passes on the real workflows; all seven bans proven to fire on a fixture | `schedule:` firing, and GitHub's 60-day disable of scheduled workflows on an inactive repo |
| `docker build` of the server image, non-root uid **1654**, `GET /health` → 200 `{"status":"ok"}`, and `ASPNETCORE_HTTP_PORTS` override proving 12-factor config | Concurrency cancellation, artifact upload |
| The whole `compose-boot` command sequence (`config` → `up --wait` → health poll → `down`) against a throwaway stack in a scratch directory | |
| Both `nightly.yml` commands (`dotnet run` on each tool, exit 0) | |
| YAML parse + `yamllint` clean on both workflows | `actionlint` (not installed; not fetched — no unvetted binaries) |
| **Integration rehearsal**: every live check re-run against a scratch export of `milestone/M0` **with M0-08 merged** — build clean, architecture suite **33/33**, all checks green except the two documented reds | |

### Follow-ups for when the repository exists

1. **Pin the `actions/*` steps to commit SHAs** rather than `@v4`. Not done here
   because the SHAs cannot be resolved or verified without a remote.
2. **Run `actionlint`** over both workflows, and consider adding it as a job.
3. **Branch protection**: `build`, `test`, `architecture-tests` and
   `vendor-package-uniqueness` are the checks that pass today and are safe to
   require immediately. `compose-boot` joined them with M0-03 and
   `content-validation` with M0-09, so all six are now safe to require.
