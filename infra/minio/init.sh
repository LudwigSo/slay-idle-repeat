#!/bin/sh
# Slay Idle Repeat — MinIO bootstrap. Runs in the `minio-init` container, then
# exits; docker-compose.yml holds the api service until it has completed.
#
# 14 §7.1 gives the object store exactly two jobs:
#
#   | S3-compatible object storage | Battle logs for replay, ghost snapshots
#     over a size threshold | MinIO locally and self-hosted; any S3 service in
#     production. |
#
# ...so this creates exactly two buckets and no more. Everything the server
# needs from object storage on a fresh machine is created here, automatically.
# Nothing about this stack should ever require someone to open the MinIO console
# and click "Create Bucket": the next developer, and CI, will not know to.
#
# Idempotent by construction — safe against both a fresh and a warm volume.
#
# Credentials in the environment are the local dev values from
# docker-compose.yml. There are no real secrets here and there must never be.

set -eu

echo "== MinIO bootstrap =================================================="

# ---------------------------------------------------------------- connect
# The minio service is already `service_healthy` before this container starts,
# but its liveness probe answers slightly before the S3 API will accept an admin
# call on a cold volume. A short retry costs nothing and removes a flake that
# would otherwise only ever show up in CI.
attempt=1
until mc alias set local "$MINIO_ENDPOINT" "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null 2>&1; do
    if [ "$attempt" -ge 30 ]; then
        echo "FAILED: could not reach $MINIO_ENDPOINT after $attempt attempts" >&2
        mc alias set local "$MINIO_ENDPOINT" "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD"   # once more, loudly
        exit 1
    fi
    echo "waiting for $MINIO_ENDPOINT (attempt $attempt)"
    attempt=$((attempt + 1))
    sleep 1
done
echo "connected to $MINIO_ENDPOINT"

# ---------------------------------------------------------------- buckets
for bucket in $SIR_BUCKETS; do
    mc mb --ignore-existing "local/$bucket"
    # Private. Battle logs and ghost snapshots are player data reached through
    # IBattleLogStore, never a public URL.
    mc anonymous set none "local/$bucket" >/dev/null
    echo "bucket ready: $bucket (private)"
done

# ------------------------------------------------------- application account
# The API does not use the root credentials. It gets its own account, so that
# the local stack models the production shape (an application identity scoped to
# the buckets it owns) rather than handing the game server the keys to the
# storage system. `|| true` on the create: re-running against a warm volume finds
# the user already there, which is success, not failure.
if mc admin user info local "$SIR_APP_ACCESS_KEY" >/dev/null 2>&1; then
    echo "application account already exists: $SIR_APP_ACCESS_KEY"
else
    mc admin user add local "$SIR_APP_ACCESS_KEY" "$SIR_APP_SECRET_KEY"
    echo "application account created: $SIR_APP_ACCESS_KEY"
fi

# `readwrite` is a MinIO built-in policy. A bucket-scoped custom policy would be
# tighter, but on a stack with two buckets and no other tenant it would be
# ceremony — and the real access boundary in production is the deployment's IAM,
# not this file.
mc admin policy attach local readwrite --user "$SIR_APP_ACCESS_KEY" >/dev/null 2>&1 \
    || echo "policy 'readwrite' already attached to $SIR_APP_ACCESS_KEY"

# ----------------------------------------------------------------- report
echo "-- buckets ---------------------------------------------------------"
mc ls local
echo "-- application account ---------------------------------------------"
mc admin user info local "$SIR_APP_ACCESS_KEY"
echo "== MinIO bootstrap complete ========================================="
