-- Adds UserTeams.AssignmentSeenAt: when the manager acknowledged a team assignment.
-- NULL = unseen -> login returns HasNewTeamAssignment = true and
-- ManagedTeams[].IsNewAssignment = true so the client shows the new-team popup.
--
-- Mirrors migration 20260923090000_AddAssignmentSeenAtToUserTeams. Re-runnable (F5).
-- Existing rows are backfilled as seen ONLY when the column is first added.

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'UserTeams'
          AND column_name = 'AssignmentSeenAt'
    ) THEN
        ALTER TABLE public."UserTeams" ADD COLUMN "AssignmentSeenAt" timestamp with time zone NULL;
        UPDATE public."UserTeams" SET "AssignmentSeenAt" = now();
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_UserTeams_Unseen_UserId"
    ON public."UserTeams" ("UserId")
    WHERE "AssignmentSeenAt" IS NULL;

-- Summary
SELECT COUNT(*)                                        AS total_rows,
       COUNT(*) FILTER (WHERE "AssignmentSeenAt" IS NULL) AS unseen_rows,
       COUNT(*) FILTER (WHERE "AssignmentSeenAt" IS NOT NULL) AS seen_rows
FROM public."UserTeams";
