-- Adds UserTeams.IsNewTeam: was this assignment's team created by the very request
-- that made the assignment?
--
-- Mirrors migration 20260908180000_AddIsNewTeamToUserTeams. Re-runnable.
--
-- It exists because post-password-landing has to report it. That endpoint runs when
-- the invited manager sets their password - days after create-manager ran, off
-- nothing but the encrypted invitation link - and the answer was previously held
-- only in create-manager's response body.
--
-- NOT NULL DEFAULT false, the truthful value for every existing row: they were all
-- made against a team that already existed. Only manager onboarding sets it true.

ALTER TABLE public."UserTeams"
    ADD COLUMN IF NOT EXISTS "IsNewTeam" boolean NOT NULL DEFAULT false;
