#!/bin/sh
# Entrypoint of job-migrate (ADR 0032): apply the EF Core migration bundle, then the idempotent grant script.
# Runs as the SQL Entra admin (id-rd-migrator) through managed identity: no password anywhere.
set -eu

: "${SQL_SERVER:?SQL_SERVER (fully qualified server name) is required}"
: "${SQL_DATABASE:?SQL_DATABASE is required}"
: "${AZURE_CLIENT_ID:?AZURE_CLIENT_ID (client id of the migrator identity) is required}"
: "${API_IDENTITY_NAME:?API_IDENTITY_NAME is required}"
: "${API_IDENTITY_CLIENT_ID:?API_IDENTITY_CLIENT_ID is required}"
# The race import identity (M8, ADR 0038) exists only after the M8 provision (gate G-R2); until then the grant script
# skips that user, so a deploy of the M8 code before the provision still succeeds.
RACE_IMPORT_IDENTITY_NAME="${RACE_IMPORT_IDENTITY_NAME:-}"
RACE_IMPORT_IDENTITY_CLIENT_ID="${RACE_IMPORT_IDENTITY_CLIENT_ID:-}"
if [ -n "$RACE_IMPORT_IDENTITY_NAME" ] && [ -z "$RACE_IMPORT_IDENTITY_CLIENT_ID" ]; then
  echo "migrate: RACE_IMPORT_IDENTITY_CLIENT_ID is required when RACE_IMPORT_IDENTITY_NAME is set" >&2; exit 1
fi
export API_IDENTITY_NAME API_IDENTITY_CLIENT_ID RACE_IMPORT_IDENTITY_NAME RACE_IMPORT_IDENTITY_CLIENT_ID

# Serverless SQL on the free offer may be paused: the first connection resumes it, which takes up to about a minute and
# can fail while it does. Both steps are idempotent, so retrying is safe.
retry() {
  attempts=$1; delay=$2; shift 2
  n=1
  until "$@"; do
    if [ "$n" -ge "$attempts" ]; then
      echo "migrate: giving up after $n attempts: $*" >&2
      return 1
    fi
    echo "migrate: attempt $n failed, retrying in ${delay}s (the database may be resuming)" >&2
    n=$((n + 1))
    sleep "$delay"
  done
}

connection="Server=tcp:${SQL_SERVER},1433;Database=${SQL_DATABASE};Authentication=Active Directory Managed Identity;User Id=${AZURE_CLIENT_ID};Encrypt=True;Connect Timeout=120"

echo "migrate: applying EF Core migrations"
retry 5 20 ./efbundle --connection "$connection"

echo "migrate: granting the API database user (and the race import user when its identity is set)"
retry 5 20 ./sqlcmd -S "tcp:${SQL_SERVER},1433" -d "$SQL_DATABASE" \
  --authentication-method ActiveDirectoryManagedIdentity -U "$AZURE_CLIENT_ID" \
  -i ./grant-app-users.sql -b

echo "migrate: done"
