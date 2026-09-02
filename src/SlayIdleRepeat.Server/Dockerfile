# syntax=docker/dockerfile:1
#
# SlayIdleRepeat.Server container image.
#
#   docker build -f src/SlayIdleRepeat.Server/Dockerfile -t slayidlerepeat/server:dev .
#
# ⚠️ BUILD CONTEXT IS THE REPOSITORY ROOT, not this directory: the Server project
# needs global.json, Directory.Build.props and its whole ProjectReference graph
# under src/. The .dockerignore that trims the context lives at the repo root.
#
# Rules this file is holding up:
#   14 §1.1  Configuration is environment variables only (12-factor). No vendor
#            config service, no secrets baked into the image, no cloud account
#            needed to build or run it.
#   14 §14   CI builds this image on every push (.github/workflows/ci.yml).
#   23 §7.1  This project is the server composition root — the image is a thin
#            wrapper around it and holds no logic of its own.

# --------------------------------------------------------------- base images
# Pinned tag AND digest. The tag says what it is at a glance; the digest is what
# actually gets pulled, so a re-tag upstream can never silently change the build.
# Never `:latest`, never a floating `:8.0` — see 14 §14.
#
# To refresh (do it deliberately, in its own commit):
#   docker pull mcr.microsoft.com/dotnet/sdk:<new>
#   docker image inspect mcr.microsoft.com/dotnet/sdk:<new> --format '{{index .RepoDigests 0}}'
#
# sdk 8.0.423 / aspnet 8.0.29, resolved 2026-08-11.
# The SDK band satisfies global.json (8.0.100, rollForward latestFeature) — this
# image does not pin a second, competing SDK version, it just has to be inside
# the band global.json already asks for.
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0.423@sha256:e2f26f26169fd10d6f1b426e01c97397717b32e9d5ab4ee4a7d5497ed9403007
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0.29@sha256:fd7596eaea7ad453fe7ac16724a3c9ae36edcda894ba13743d6a5c83d6a3b36d


# ---------------------------------------------------------------- build stage
FROM ${DOTNET_SDK_IMAGE} AS build

WORKDIR /src

# Only what the Server's project graph needs. tests/ is deliberately
# absent: restoring the .sln would drag the xunit stack and the Godot client
# placeholder into a production image build for no reason.
#
# The whole src/ tree is copied in one layer rather than csproj-first: with 33
# projects the csproj-only trick needs either a fragile per-project COPY list or
# a BuildKit bind mount, and both rot faster than they save time. Restore is
# cached by NuGet inside the layer instead.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ ./src/

RUN dotnet restore src/SlayIdleRepeat.Server/SlayIdleRepeat.Server.csproj

# TreatWarningsAsErrors is already on via Directory.Build.props — a warning here
# fails the image build, which is the point.
# UseAppHost=false: the entrypoint is `dotnet Server.dll`, so the native apphost
# would be dead weight in the runtime layer.
RUN dotnet publish src/SlayIdleRepeat.Server/SlayIdleRepeat.Server.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false


# -------------------------------------------------------------- runtime stage
FROM ${DOTNET_RUNTIME_IMAGE} AS runtime

LABEL org.opencontainers.image.title="SlayIdleRepeat.Server" \
      org.opencontainers.image.description="Server-authoritative game host for Slay Idle Repeat" \
      org.opencontainers.image.licenses="UNLICENSED"

# 12-factor configuration (14 §1.1): every deployment-varying value arrives as an
# environment variable at run time. Nothing below is a secret, and no secret may
# ever be added here — secrets are injected by the runtime environment.
#
# appsettings.json travels with the publish output because it is part of the
# ASP.NET project; it carries log levels and AllowedHosts only. Anything that
# differs between laptop, CI and production is an env var, not a file.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

WORKDIR /app

# Copied as root and never chowned to the app user: the running process must not
# be able to rewrite its own binaries.
COPY --from=build --chown=root:root /app/publish ./

# The content set GameBackbone loads at GameData:Root — default `game-data` under
# the content root, which WORKDIR makes /app/game-data. NOT deployment-varying, so
# it ships in the image rather than as an env var or a mount: it is the build's own
# content, the same bytes the determinism baselines were taken against, and the
# stamp GET /content/current publishes is read straight off it.
#
# ⚠️ Without it the image starts and answers /health, and then every endpoint that
# touches the backbone — both command routes, /run/{id}/state and both /content
# routes — faults on the first request, because ContentLoader.Load().Require()
# has nothing to load. That is what the compose-boot probes exercise.
COPY --chown=root:root game-data/ ./game-data/

EXPOSE 8080

# Non-root. APP_UID (1654) and the matching `app` user ship with the .NET 8
# runtime images; using the variable rather than a literal keeps this correct if
# Microsoft renumbers it.
USER $APP_UID

# No HEALTHCHECK on purpose: the aspnet base image carries no curl and no wget,
# and installing one to poll a local port would widen the runtime attack surface
# for a check the platform already does better from outside. GET /health is
# polled by the compose-boot CI job and by M0-03's compose stack.
ENTRYPOINT ["dotnet", "SlayIdleRepeat.Server.dll"]
