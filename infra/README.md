# The local stack

Everything the Slay Idle Repeat backend needs, on a laptop, with **no cloud
account, no credentials and no registry login**.

```bash
docker compose up -d --wait     # boot; blocks until every service is healthy
docker compose ps               # what is running
docker compose logs -f api      # follow one service
docker compose down             # stop, keep the data
docker compose down -v          # stop, delete the data (next boot is a cold one)
```

Run these from the **repository root** — `docker-compose.yml` lives there because
the API image is built with the repository root as its context.

This is the requirement, `14` §1.1 🔒, verbatim:

> | Local development | `docker compose up` brings the entire stack — API,
>   Postgres, Redis, MinIO, observability — up on a laptop with no cloud account. |
> | Portability test | CI builds and boots the full stack in Docker Compose on
>   every commit. **If it cannot run on a laptop, it is locked in.** |

---

## What is running

| Service | Image (pinned by tag **and** digest) | What it is for | Host port |
|---|---|---|---|
| `api` | built from `src/SlayIdleRepeat.Server/Dockerfile` | `SlayIdleRepeat.Server` — the composition root (`23` §7.1). Today: `GET /health`. | **8080** |
| `postgres` | `postgres:16.9-alpine` | 🔒 **The system of record** (`14` §7.1) — profiles, inventory, run snapshots, idempotency outcomes, the economy event log, the inbox. | **5432** |
| `redis` | `redis:7.4.5-alpine` | 🔒 **Rebuildable cache only, never a system of record** (`14` §7.1) — hot run state, sessions, idempotency hot-cache, rate limits. | **6379** |
| `minio` | `minio/minio:RELEASE.2025-09-07T16-13-09Z` | S3-compatible object storage (`14` §7.1) — battle logs for replay, ghost snapshots over a size threshold. | **9000** (S3), **9001** (console) |
| `minio-init` | `minio/mc:RELEASE.2025-08-13T08-35-41Z` | One-shot: creates the buckets and the application account, then exits. | — |
| `otel-collector` | `otel/opentelemetry-collector-contrib:0.130.1` | The single telemetry ingestion point (`14` §10). OTLP in, Jaeger + Prometheus out. | **4317** (gRPC), **4318** (HTTP) |
| `otel-probe-tools` | `busybox:1.37.0-uclibc` | One-shot: stages a static `busybox` so the distroless collector can be health-checked. Exits in well under a second. | — |
| `jaeger` | `jaegertracing/all-in-one:1.71.0` | Trace backend and UI. In-memory storage — traces do not survive a restart. | **16686** (UI) |
| `prometheus` | `prom/prometheus:v3.5.0` | Metrics store (`14` §10). Scrapes the collector, never the API. | **9090** |
| `grafana` | `grafana/grafana-oss:12.0.2` | Dashboards (`14` §10). Datasources and dashboards are **provisioned from committed files**. | **3000** |

Every host port can be moved without editing `docker-compose.yml` — see
[`.env.example`](../.env.example). Container-side ports never change.

**Every port above is published on `127.0.0.1` only, except `api`.** Redis has no
`requirepass`, Postgres' password is committed, and Grafana is anonymous-Admin —
on a café or hotel network a `0.0.0.0` bind would hand that to the whole subnet,
and Docker's published ports land in the `DOCKER` iptables chain where a host
firewall rule does not reach them. Nothing needs them off-box: `api` talks to
them over the compose network, and you are on this machine. `api` is the one
exception, deliberately, so a Godot build on a physical handset can reach
`http://<laptop-ip>:8080`.

Quick links once the stack is up:

- API health — <http://127.0.0.1:8080/health>
- Grafana — <http://127.0.0.1:3000> (opens straight in, anonymous admin)
- Jaeger — <http://127.0.0.1:16686>
- Prometheus targets — <http://127.0.0.1:9090/targets>
- MinIO console — <http://127.0.0.1:9001>

### Approximate footprint

`docker stats --no-stream`, idle, after a cold `up -d --wait` on the development
machine. Two runs, because MinIO's Go heap moves around a lot early on:

| Service | run 1 | run 2 | `mem_limit` |
|---|---|---|---|
| api | 33 MiB | 30 MiB | 640 MiB |
| postgres | 55 MiB | 32 MiB | 512 MiB |
| redis | 12 MiB | 8 MiB | 256 MiB |
| minio | 114 MiB | 211 MiB | 512 MiB |
| otel-collector | 37 MiB | 36 MiB | 512 MiB |
| jaeger | 14 MiB | 13 MiB | 512 MiB |
| prometheus | 36 MiB | 32 MiB | 512 MiB |
| grafana | 107 MiB | 98 MiB | 384 MiB |
| **total** | **≈ 410 MiB** | **≈ 460 MiB** | ≈ 3.4 GiB |

**Call it 400–500 MiB idle.** The `mem_limit` ceilings are roughly 8× that on
purpose: generous enough never to OOM a healthy service, tight enough that a leak
is contained instead of taking the machine down with it. Disk is about 1.6 GB of
images, of which the collector is 345 MB on its own.

`14` §1.1's test is *"if it cannot run on a laptop, it is locked in."* Half a
gigabyte passes comfortably. Keeping it there is the reason self-hosted Sentry and
PostHog are not here — see below.

Nothing was trimmed to reach this number; the stack is eight services because
`14` §1.1 and §10 name eight things. What was *not added* is listed under "Why no
Loki" and "Why no Sentry and no PostHog containers" below — those three would
have taken it past 5 GB.

---

## Dev credentials

**None of these are secrets.** They are fixed, obvious, committed values for
containers on a laptop, written in plain sight in `docker-compose.yml` next to
the services that use them. `14` §1.1 says secrets are "injected as environment
variables; the platform's secret store is an implementation detail of
deployment" — so a deployed environment supplies its own and never reads a value
from this repository.

| Where | User | Password |
|---|---|---|
| Postgres (`slayidlerepeat` database) | `sir_app` | `sir_local_dev_password` |
| MinIO root (admin/console) | `sir_local_root` | `sir_local_dev_password` |
| MinIO application account (used by the API) | `sir_app` | `sir_local_dev_password` |
| Grafana | `admin` | `sir_local_dev_password` (or just click in — anonymous access is on) |

```bash
psql "postgres://sir_app:sir_local_dev_password@127.0.0.1:5432/slayidlerepeat"
docker compose exec postgres psql -U sir_app -d slayidlerepeat
docker compose exec redis redis-cli
```

`build/ci/Test-NoCloudCredentials.ps1` fails the build if a real credential, a
registry login, a private cloud registry or a CI secret expression ever appears
in the compose file or the workflows.

---

## What is set up automatically (and must stay that way)

Nothing in this stack requires a human to click anything. That is not a
convenience — a setup step that lives in a README is a step CI cannot perform and
the next developer will not know about.

**Postgres** — `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD` create the
database and the login role the API is configured for.
`infra/postgres/initdb/10-database.sql` then pins the database to UTC, ISO dates
and sane lock timeouts, and writes one row into a `meta.bootstrap` table
explaining why there are no other tables.

> ✅ **The schema arrives with the API, not with this init script.** M5-05's
> migrations — players, runs, idempotency outcomes, the append-only economy
> event log, player messages — are numbered SQL files **embedded in
> `SlayIdleRepeat.Adapters.Persistence.Postgres`** and applied **at API
> startup**, under `pg_advisory_lock`, with a checksum-verified
> `meta.migrations` history table (created by the runner itself —
> `meta.bootstrap` stays init-script bookkeeping). Two API instances starting
> together apply the history once; an edited already-applied file fails the
> boot loudly. There is no `dotnet ef database update` step and no migration
> init container: `docker compose up` alone yields a migrated database the
> moment the API is healthy. Ghosts, ratings, ladder, seasons and entitlements
> remain later milestones' files (M12 onward), appended as the next ordinals.

**MinIO** — `infra/minio/init.sh` runs in the `minio-init` container and creates
the two buckets `14` §7.1 calls for, `battle-logs` and `ghost-snapshots`, both
private; then creates the non-root `sir_app` account the API uses. It is
idempotent, so it is safe against a warm volume. The `api` service will not start
until it has completed successfully.

**Grafana** — datasources (`infra/grafana/provisioning/datasources/`) and
dashboards (`infra/grafana/dashboards/`, registered by
`infra/grafana/provisioning/dashboards/dashboards.yml`) are read from disk at
boot and are read-only in the UI. To change a dashboard: edit it in the browser,
*Export → Save to file* with "export for sharing externally" **off**, and commit
the JSON over the existing file.

---

## Observability: what is proven today, and what is not

The pipeline is `api → OTLP → otel-collector → { Jaeger (traces), Prometheus
(metrics) } → Grafana`. The application only ever knows the first arrow; where
telemetry goes afterwards is decided in `infra/otel-collector/config.yaml` and
nowhere else. That is what `14` §10's "Vendor-neutral instrumentation. Swap the
backend freely" buys, and it is why **Prometheus does not scrape the API
directly** — that would put a Prometheus-shaped dependency back into the server.

**Proven, and re-checked by `ComposeStackSmokeTests`:**

- The collector accepts OTLP on 4317/gRPC and 4318/HTTP and reports healthy on
  its `health_check` extension.
- A synthetic span posted to the collector arrives in **Jaeger**, with its
  resource and scope attributes intact.
- The collector's own telemetry (`otelcol_*`) is scraped by **Prometheus** — so
  `otelcol_receiver_accepted_spans_total` and `otelcol_exporter_sent_spans_total`
  both move when a span goes through.
- All four Prometheus scrape targets are up, including `otel-collector-app`, the
  job that will carry application metrics.
- **Grafana** comes up with the Prometheus and Jaeger datasources and the
  *Telemetry pipeline* dashboard already there, from committed files.

Send the synthetic span yourself, from the repository root:

```bash
curl -X POST http://127.0.0.1:4318/v1/traces \
     -H 'Content-Type: application/json' \
     -d @infra/otel-collector/testdata/span.json
# -> {"partialSuccess":{}}
```

Then look for the service `sir-pipeline-probe` in Jaeger. The span is stamped
`2026-01-01T00:00:00Z`, so widen the time range.

> **M5-11 registered the emitters.** `builder.AddObservability()`
> (`Composition/ObservabilityComposition.cs`) wires Serilog (compact one-line
> JSON to stdout), the OpenTelemetry SDK subscribed to the server's own
> `ActivitySource`/`Meter` (both named `SlayIdleRepeat.Server`) with OTLP
> exporters that read the `OTEL_*` variables below, Sentry from `Sentry__Dsn`,
> and the PostHog `/batch` sink from `PostHog__*` with a 10-second flush loop.
> What travels today: a `run_command`/`player_command` span per command POST, a
> `domain_events` histogram tagged by event type, and every exception that
> escapes the pipeline. With the local `Sentry__Dsn=''` / `PostHog__Enabled=false`
> both vendor sinks stay silent by configuration, not by absence.

### Why no Loki

`14` §10 offers a choice — *"**Loki** (or plain stdout + the platform's log
store)"* — and this stack takes the second option:

- Serilog writes structured JSON to stdout; Docker captures it; `docker compose
  logs` reads it. The `json-file` driver is capped at 3 × 10 MB per service so it
  cannot quietly eat a disk.
- Loki plus a collection agent is another ~200–300 MiB and a second query
  language for a stack whose whole footprint is currently 410 MiB, to aggregate
  logs across the eight containers of a single laptop — where `docker compose
  logs` already does that.
- Deployed environments have a log store of their own, which is exactly what the
  parenthetical in `14` §10 anticipates.

Adding Loki later is small and additive: one service, one exporter in the
collector's `logs` pipeline (which already exists and currently writes to
stdout), and one Grafana datasource file. **No application code changes**, which
is the test of whether this was a real decision or a lock-in.

### Why no Sentry and no PostHog containers

This was decided at the M0 kickoff and it is deliberate. `14` §10 says of
Sentry: *"Self-hosted **or** SaaS — identical SDK, so the choice is a deployment
detail"*, and of PostHog: *"Self-hostable"*. That is latitude, and it is being
used.

- Self-hosted Sentry is roughly nine containers and wants 4+ GB of RAM on its
  own. Self-hosted PostHog drags in ClickHouse, Kafka and Zookeeper.
- Either one would take this stack from 410 MiB to multiple gigabytes and make
  `docker compose up` something developers avoid — and the first thing anyone
  would do is comment them out, at which point the stack is lying about what it
  boots.
- Neither was on the critical path before M5-11, and neither is one now: M5-11
  registered both adapters, and both stay switched off in this stack by
  configuration (below), so the data they would carry has somewhere to go the
  moment a deployment supplies a DSN and a project key.

The **adapters exist and are registered** (`Adapters.Analytics.PostHog` behind
`IAnalyticsSinkPort`, Sentry initialised unconditionally at startup) but are
configured off here: the API gets `Sentry__Dsn=` (empty — the SDK's own
documented "disabled" value) and `PostHog__Enabled=false`, which the server
announces with one startup warning that analytics is being dropped. A deployed
environment sets a real DSN and host, which is precisely the "deployment
detail" `14` §10 calls it.

**If you have come here to "complete" the observability stack: don't.** Bring it
up at a kickoff instead.

---

## Configuration: 12-factor, and what M5 must change

`14` §1.1: *"Configuration | Environment variables only (12-factor). No vendor
config service."* Every deployment-varying value reaches the API as an
environment variable, listed in the `api` service in `docker-compose.yml`.

> ⚠️ **Every group below is read by the server now.** M5-10 landed the
> `RemoteConfig__*` pair, M5-05 the `ConnectionStrings__*` / `Cache__*` /
> `ObjectStore__*` groups, M5-11 the `OTEL_*`, `Sentry__Dsn` and `PostHog__*`
> groups, and M5-06 the `Auth__*` group. The one exception is called out in its
> own row: `ObjectStore__GhostSnapshotBucket`, defined ahead of the code that will
> bind it. `__` is ASP.NET Core's configuration separator:
> `ConnectionStrings__Postgres` binds to the key `ConnectionStrings:Postgres`.

| Variable | Value in this stack | Consumed by |
|---|---|---|
| `RemoteConfig__Path` | `/app/remote-config/flags.json` (the committed identity document, mounted read-only) | **M5-10 — shipped, the server reads this** |
| `RemoteConfig__ReloadSeconds` | `60` (an ops number, `14` §16.5 — not a tunable) | **M5-10 — shipped, the server reads this** |
| `ConnectionStrings__Postgres` | `Host=postgres;Port=5432;Database=slayidlerepeat;Username=sir_app;…` | **M5-05 — shipped, the server reads this** |
| `ConnectionStrings__Redis` | `redis:6379,abortConnect=false` | **M5-05 — shipped, the server reads this** |
| `Cache__RunStateTtlHours` | `48` (`14` §7.1: "Run-state cache TTL 48 h") | **M5-05 — shipped, the server reads this** |
| `ObjectStore__ServiceUrl` | `http://minio:9000` | **M5-05 — shipped, the server reads this** |
| `ObjectStore__Region` / `__ForcePathStyle` | `us-east-1` / `true` | **M5-05 — shipped, the server reads this** |
| `ObjectStore__AccessKey` / `__SecretKey` | `sir_app` / `sir_local_dev_password` | **M5-05 — shipped, the server reads this** |
| `ObjectStore__BattleLogBucket` | `battle-logs` | **M5-05 — shipped, the server reads this** |
| `ObjectStore__GhostSnapshotBucket` | `ghost-snapshots` | ⚠️ nothing yet — M12 lands the ghost-snapshot producer |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://otel-collector:4317` | **M5-11 — shipped, the OTel SDK reads this** |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` | **M5-11 — shipped, the OTel SDK reads this** |
| `OTEL_SERVICE_NAME` | `slayidlerepeat-server` | **M5-11 — shipped, the OTel SDK reads this** |
| `OTEL_RESOURCE_ATTRIBUTES` | `service.namespace=slayidlerepeat,deployment.environment=local` | **M5-11 — shipped, the OTel SDK reads this** |
| `OTEL_TRACES_SAMPLER` | `always_on` (local only) | **M5-11 — shipped, the OTel SDK reads this** |
| `Sentry__Dsn` | *(empty — disabled)* | **M5-11 — shipped, the server reads this** |
| `PostHog__Enabled` | `false` | **M5-11 — shipped, the server reads this** |
| `Auth__JwtSigningKey` | `sir_local_dev_jwt_signing_key_not_a_secret` (a local dev default like every other value here; **a real secret in a deployed environment**, injected per `14` §1.1) | **M5-06 — shipped, the server reads this** |
| `Auth__AccessTokenLifetimeMinutes` | `60` (`14` §16.5's access-token lifetime) | **M5-06 — shipped, the server reads this** |
| `Auth__RefreshTokenLifetimeDays` | `30` (`14` §16.5's refresh-family lifetime) | **M5-06 — shipped, the server reads this** |
| `Auth__SilentRenewalFraction` | `0.8` (`14` §16.5: the client renews at ~80 % of the access lifetime; the server hands it out as `renewAfterSeconds`) | **M5-06 — shipped, the server reads this** |

The `OTEL_*` names are the OpenTelemetry specification's own, which the .NET
OTel SDK reads with no code at all — M5-11 registers the SDK and it picks these
up as they are.

`Auth__JwtSigningKey` has **no default in code**. An absent or blank value, or
one under 32 UTF-8 bytes, fails the first auth request with the variable's own
spelling in the message. A generated default would invalidate every live token
on each restart while looking like it worked, and a baked-in one would ship a
public secret — so there is neither.

> 📄 **Doc errata — `14` §16.5's 📐 markers on the three token lifetimes.**
> §16.5 marks the access-token lifetime, the refresh lifetime and the silent-renewal
> fraction with 📐, but `21` §6 says a 📐-marked number outside `game-data/tuning/`
> is a bug — while §16.5's own **Config home** row says these three are *"server-operations
> numbers: **environment configuration** … deliberately **not** in `game-data/tuning/`
> — they are not economy tunables and must never ride a content push."* The two
> statements cannot both hold. The Config-home row is the ruling and is what this
> stack implements: the numbers are `Auth__*` environment variables. **The 📐 markers
> in §16.5 should be struck**; recorded here rather than silently ignored, because
> the next reader of §16.5 will otherwise reach the opposite conclusion.

### The M5 checklist

**M5-10 shipped `GET /config` and the flags file.** The server serves
[`infra/remote-config/flags.json`](remote-config/flags.json) verbatim on
`GET /config` and gates commands on it. To throw a kill switch locally, edit the
file — the server re-reads it every 60 s (`RemoteConfig__ReloadSeconds`), no
restart needed. A document with a typo in it is refused whole and logged with a
`[remote-config]` marker; the last good document stays in force.

1. ✅ **M5-05** — done. Migrations run **at API startup** (see "What is set up
   automatically" above): embedded numbered SQL files, `pg_advisory_lock`,
   checksummed `meta.migrations`. `Composition/PersistenceComposition.cs` binds
   `ConnectionStrings__Postgres` (system of record — without it the process
   runs on volatile placeholders), `ConnectionStrings__Redis` (hot cache,
   optional decorator), `Cache__RunStateTtlHours`, and the `ObjectStore__*`
   group (battle-log store + write-behind drain; `GhostSnapshotBucket` stays
   unread until M12). The compose-boot job now also runs four probes against
   this wiring: a command round-trip persisted in Postgres, a byte-identical
   idempotent replay across an API restart, a Redis flush that costs latency
   but no progress, and the battle-log drain's startup marker.
2. ✅ **M5-11 — done for the SDK and Serilog**: `builder.AddObservability()`
   registers both, and the `OTEL_*` variables are picked up by the SDK as they
   are. If spans do not appear in Jaeger, check
   `docker compose logs otel-collector` before suspecting this stack. Still
   open here: the real game dashboards (`14` §10.1's event set — perk pick
   rate, run abandonment by tile index, disconnect rate, season rating drift)
   as new files in `infra/grafana/dashboards/`, which need source events the
   domain does not emit yet (see the unemittable-event register in
   `Architecture.Tests`).
3. **Both** — tighten the stack assertions in the `compose-boot` job
   (`.github/workflows/ci.yml`) to cover the new signals: real server spans and
   metrics rather than only "every scrape target is up".

   🔒 Those assertions are **CI steps, not a test suite**. This repository has no
   integration or end-to-end tier — `SlayIdleRepeat.Integration.Tests` was deleted
   and is not to be recreated under any name. `14` §13's *"full run played
   end-to-end against a Docker Compose stack"* is explicitly **not** adopted here.
   No test project may depend on this stack.

### Rules for changing this stack

- **Pin every image by tag *and* digest.** Never `:latest`, never a floating
  minor. To refresh one deliberately:
  ```bash
  docker pull <image>:<new-tag>
  docker image inspect <image>:<new-tag> --format '{{index .RepoDigests 0}}'
  ```
- **Every service gets a healthcheck.** `docker compose up --wait` is only
  meaningful if it blocks until the stack is genuinely usable, and the CI job
  polls `/health` the instant it returns.
- **No credentials, ever** beyond the obviously-local dev values above. No
  registry login, no cloud secret store, no `${{ }}` templating in the compose
  file.
- **No setup step that a human performs.** If a new service needs bootstrapping,
  write an init container.

---

## Troubleshooting

**A host port is already taken.** `cp .env.example .env` and change the number.
Container-side ports and every internal URL stay as they are.

**`up --wait` says a service is unhealthy.** `docker compose ps` for the state,
then `docker inspect --format '{{json .State.Health}}' slayidlerepeat-<svc>-1`
for the last few probe outputs, then `docker compose logs <svc>`.

**Grafana takes a long time on the first boot.** It runs ~670 SQLite migrations
on a fresh volume. `GF_DATABASE_WAL=true` keeps this well under a minute on most
machines; its healthcheck has a 180 s `start_period` for the ones where it is
not. A warm volume boots in seconds — this only bites after `down -v`.

**The API is healthy but nothing else works.** Expected. The server is a
`/health` endpoint and nothing more until M5.

**Traces vanished.** Jaeger stores them in memory, capped at 20 000. Restarting
Jaeger clears them. That is the right trade for a laptop; a deployed environment
uses a real backend, and the application cannot tell the difference because it
only ever speaks OTLP to the collector.

**Redis lost its data.** By design — persistence is switched off and there is no
volume, because `14` §7.1 🔒 says Redis is a *"rebuildable cache only, never a
system of record. On loss or inconsistency, rebuilt from Postgres."* Code that
starts treating Redis as durable should fail on a developer's machine, not in
production.

**A full reset.**

```bash
docker compose down -v --remove-orphans
docker compose up -d --wait
```
