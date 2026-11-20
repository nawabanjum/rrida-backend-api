#!/usr/bin/env bash
# Regenerates 01_schema.sql and 02_reference_data.sql from a source database
# (normally Dev). Read-only against the source.
#
#   export PGHOST=pg-rrida-dev-001.postgres.database.azure.com PGDATABASE=rrida \
#          PGUSER=rridaadmin PGPASSWORD=... PGSSLMODE=require
#   ./export-bootstrap.sh
#
# Only needed when the baseline should move forward. It is never required for
# new migrations: the pipeline applies those on top of the baseline.
#
# Review the diff before committing: 02_reference_data.sql must never contain
# tenant or personal data, and ModelConfiguration.ApiKey is always stripped.
set -euo pipefail

OUT="$(cd "$(dirname "$0")" && pwd)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
export PGOPTIONS='-c default_transaction_read_only=on'

REFERENCE_TABLES=(
  __EFMigrationsHistory ContentCultures CurrentRelationshipStatuses Industries
  NumberOfEmployees OpportunityTypes UserGroups SystemModules AssignUserRights
  PromptTypes Plans DailyQuestionSetGroups DailyQuestionSets DailyQuestions
  DailyQuestionSpecifications DailyReasons DailyMicroActions TeamQuestionCategories
  TeamDailyQuestions TeamDailyQuestionSpecifications TeamDailyReasons
  TeamDailyMicroActions workcategories SeedingHistories LLMPrompts PromptTemplates
  AIModelConfigs ModelConfigurationTypes
)

# pg_dump 17 emits settings and psql meta-commands a PostgreSQL 16 server or an
# older psql client rejects; the schema already exists as "public".
clean() { grep -vE '^\\(restrict|unrestrict) |^SET transaction_timeout|^CREATE SCHEMA public;|^-- Dumped by pg_dump' ; }

pg_dump --schema-only --no-owner --no-privileges --no-comments --schema=public -f "$TMP/schema.sql"

TABLE_ARGS=()
for t in "${REFERENCE_TABLES[@]}"; do TABLE_ARGS+=("--table=public.\"$t\""); done
pg_dump --data-only --no-owner --no-privileges "${TABLE_ARGS[@]}" -f "$TMP/data.sql"

psql -X -A -t -v ON_ERROR_STOP=1 -c "SELECT format('INSERT INTO public.\"ModelConfiguration\" OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_record(NULL::public.\"ModelConfiguration\", %L);', (to_jsonb(m) - 'ApiKey')::text) FROM public.\"ModelConfiguration\" m ORDER BY m.\"Id\";" > "$TMP/model_configuration.sql"

# Keep each file's hand-written header (everything up to the first blank line).
{ sed '/^$/q' "$OUT/01_schema.sql"; clean < "$TMP/schema.sql"; } > "$TMP/01_schema.sql"
{
  sed '/^$/q' "$OUT/02_reference_data.sql"
  clean < "$TMP/data.sql"
  printf '\n--\n-- ModelConfiguration (ApiKey removed)\n--\n'
  grep -v '^$' "$TMP/model_configuration.sql"
  cat <<'SQL'
SELECT pg_catalog.setval(pg_catalog.pg_get_serial_sequence('public."ModelConfiguration"', 'Id'),
                         (SELECT COALESCE(MAX("Id"), 1) FROM public."ModelConfiguration"));
SQL
} > "$TMP/02_reference_data.sql"

mv "$TMP/01_schema.sql" "$OUT/01_schema.sql"
mv "$TMP/02_reference_data.sql" "$OUT/02_reference_data.sql"
echo "Wrote $OUT/01_schema.sql and $OUT/02_reference_data.sql. Review the diff before committing."
