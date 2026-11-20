#!/usr/bin/env bash
# Prepares an RRIDA database for the EF migrations bundle.
#
#   bootstrap-database.sh "<Npgsql connection string>" [bootstrap dir]
#
# Run by the deploy pipeline before every migration, in every environment:
#   1. CREATE EXTENSION vector (needed by DocumentEmbeddings; idempotent).
#   2. Empty database (no tables in public)  -> apply 01_schema.sql and
#      02_reference_data.sql in one transaction. This is how a brand-new
#      environment such as Pre-Prod gets its database: the EF migrations alone
#      cannot build it, because the original schema came from hand-run scripts.
#   3. Tables AND __EFMigrationsHistory       -> nothing to do (Dev, or a
#      Pre-Prod that was already bootstrapped).
#   4. Tables but NO __EFMigrationsHistory    -> stop. Something else put data
#      there; applying the baseline or the bundle could damage it.
#
# The EF bundle runs after this script and applies any migration newer than
# the baseline, exactly as it does on Dev.
set -euo pipefail

CONNECTION_STRING="${1:?connection string is required}"
BOOTSTRAP_DIR="${2:-$(cd "$(dirname "$0")" && pwd)}"

if ! command -v psql >/dev/null 2>&1; then
  echo "psql not found, installing postgresql-client..."
  sudo apt-get update -qq
  sudo apt-get install -y -qq postgresql-client >/dev/null
fi

# Npgsql "Key=Value;..." -> libpq environment variables.
eval "$(python3 - "$CONNECTION_STRING" <<'PY'
import shlex, sys
aliases = {
    "host": "PGHOST", "server": "PGHOST",
    "port": "PGPORT",
    "database": "PGDATABASE",
    "username": "PGUSER", "user id": "PGUSER", "userid": "PGUSER", "user": "PGUSER",
    "password": "PGPASSWORD",
}
for part in sys.argv[1].split(";"):
    if "=" not in part:
        continue
    key, value = part.split("=", 1)
    env = aliases.get(key.strip().lower())
    if env:
        print(f"export {env}={shlex.quote(value.strip())}")
PY
)"
export PGSSLMODE="${PGSSLMODE:-require}"

run_sql() { psql -X -q -v ON_ERROR_STOP=1 "$@"; }
scalar() { psql -X -A -t -v ON_ERROR_STOP=1 -c "$1" | tr -d '[:space:]'; }

echo "Database: $PGDATABASE on $PGHOST"

run_sql -c 'CREATE EXTENSION IF NOT EXISTS vector WITH SCHEMA public;'

TABLES=$(scalar "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE';")
HAS_HISTORY=$(scalar "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL;")

if [ "$TABLES" = "0" ]; then
  echo "Empty database: applying schema baseline and reference data..."
  run_sql -1 -f "$BOOTSTRAP_DIR/01_schema.sql" -f "$BOOTSTRAP_DIR/02_reference_data.sql"
  echo "Bootstrap applied: $(scalar "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';") tables, $(scalar 'SELECT count(*) FROM public."__EFMigrationsHistory";') migrations recorded."
elif [ "$HAS_HISTORY" = "t" ]; then
  echo "Database already initialised ($TABLES tables). Bootstrap skipped."
else
  echo "##vso[task.logissue type=error]Database has $TABLES tables but no __EFMigrationsHistory. Refusing to bootstrap or migrate; check what is in this database."
  exit 1
fi
