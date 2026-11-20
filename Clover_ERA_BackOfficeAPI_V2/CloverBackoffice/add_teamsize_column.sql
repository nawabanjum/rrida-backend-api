-- Adds the Team Size field to teams (business units).
-- Nullable so teams created before this field existed stay valid; the API
-- requires a value when creating a team and treats it as optional on edit.

ALTER TABLE "BusinessUnits" ADD COLUMN IF NOT EXISTS "TeamSize" integer NULL;

-- Rollback
-- ALTER TABLE "BusinessUnits" DROP COLUMN "TeamSize";
