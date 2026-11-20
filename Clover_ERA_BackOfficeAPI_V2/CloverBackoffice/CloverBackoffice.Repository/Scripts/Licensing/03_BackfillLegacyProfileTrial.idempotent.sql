-- Backfill the registration trial for profiles created before trial
-- provisioning existed (pre 2026-09-22), e.g. profile 52 (CloverERA).
--
-- Mirrors AccountRegistrationRepository.ProvisionRegistrationTrialAsync:
--   * Trial CompanySubscription (Plan "Single Team", Monthly, 30 days) + audit row
--   * $0 Trial payment
--   * 1 Trial licence CLD-TRI001-{ProfileId}, held by the profile's oldest live
--     Profile Admin, + audit row
--   * That Profile Admin also gets the Team Manager role (AddRegistrationRoles)
--
-- Idempotent: a profile that already has a live subscription is skipped, so
-- re-running mints nothing. Edit target_profiles to widen the scope
-- (other legacy profiles with users and no subscription: 56, 57, 59).

DO $$
DECLARE
    target_profiles int[] := ARRAY[52];
    trial_days      int   := 30;
    v_now           timestamptz := now();
    v_end           timestamptz := now() + make_interval(days => 30);
    v_plan_id       int;
    v_people        int;
    v_pid           int;
    v_admin_id      int;
    v_sub_id        int;
    v_lic_id        int;
BEGIN
    SELECT "Id", "MaxPeoplePerTeam" INTO v_plan_id, v_people
    FROM "Plans"
    WHERE "IsActive" AND "MaxTeams" = 1
    ORDER BY "SortOrder"
    LIMIT 1;

    IF v_plan_id IS NULL THEN
        RAISE EXCEPTION 'No active Plan with MaxTeams = 1.';
    END IF;

    FOREACH v_pid IN ARRAY target_profiles LOOP
        IF EXISTS (SELECT 1 FROM "CompanySubscriptions"
                   WHERE "ProfileId" = v_pid AND NOT "IsDel") THEN
            RAISE NOTICE 'Profile %: already has a subscription, skipped.', v_pid;
            CONTINUE;
        END IF;

        -- Oldest live Profile Admin (UserRoles, falling back to the legacy column).
        SELECT u."UserId" INTO v_admin_id
        FROM "Users" u
        WHERE u."ProfileId" = v_pid AND NOT u."IsDel"
          AND (EXISTS (SELECT 1 FROM "UserRoles" r WHERE r."UserId" = u."UserId" AND r."UserGroupId" = 1)
               OR (NOT EXISTS (SELECT 1 FROM "UserRoles" r WHERE r."UserId" = u."UserId") AND u."UserGroupId" = 1))
        ORDER BY u."UserId"
        LIMIT 1;

        IF v_admin_id IS NULL THEN
            RAISE NOTICE 'Profile %: no live Profile Admin, skipped.', v_pid;
            CONTINUE;
        END IF;

        -- Subscription (BillingCycle 1 = Monthly, Status 1 = Trial).
        INSERT INTO "CompanySubscriptions"
            ("ProfileId", "PlanId", "BillingCycle", "Status", "TeamCount",
             "MaxTeamsAllowed", "MaxPeopleAllowed", "TrialStartDate", "TrialEndDate",
             "CurrentPeriodStart", "CurrentPeriodEnd", "IsTrialUsed", "CancelAtPeriodEnd",
             "IsDel", "CreatedAt", "UpdatedAt")
        VALUES
            (v_pid, v_plan_id, 1, 1, 1,
             1, v_people, v_now, v_end,
             v_now, v_end, true, false,
             false, v_now, v_now)
        RETURNING "Id" INTO v_sub_id;

        INSERT INTO "CompanySubscriptionAuditLogs"
            ("Id", "ProfileId", "PlanId", "BillingCycle", "Status", "TeamCount",
             "MaxTeamsAllowed", "MaxPeopleAllowed", "TrialStartDate", "TrialEndDate",
             "CurrentPeriodStart", "CurrentPeriodEnd", "IsTrialUsed", "CancelAtPeriodEnd",
             "CreatedAt", "UpdatedAt", "IsDel", "LoggedAt", "VersionNumber", "OperationType")
        VALUES
            (v_sub_id, v_pid, v_plan_id, 1, 1, 1,
             1, v_people, v_now, v_end,
             v_now, v_end, true, false,
             v_now, v_now, false, v_now, 1, 'INSERT');

        -- $0 trial payment (PaymentMethod 3 = Trial, Status 2 = Succeeded).
        INSERT INTO "Payments"
            ("ProfileId", "CompanySubscriptionId", "Amount", "Currency",
             "PaymentMethod", "Status", "Description", "CreatedAt", "UpdatedAt")
        VALUES
            (v_pid, v_sub_id, 0, 'usd',
             3, 2, format('Trial activation - 1 license(s) - %s days', trial_days), v_now, v_now);

        -- 1 trial licence, held by the Profile Admin.
        INSERT INTO "CompanyLicenses"
            ("ProfileId", "LicenseKey", "LicenseType", "CompanySubscriptionId", "PlanId",
             "ExpiryDate", "AssignedUserId", "AssignedUserAt", "CreatedBy", "UpdatedBy",
             "CreatedAt", "UpdatedAt", "IsDel", "CancelAtPeriodEnd")
        VALUES
            (v_pid, format('CLD-TRI001-%s', v_pid), 'Trial', v_sub_id, v_plan_id,
             v_end, v_admin_id, v_now, v_admin_id, v_admin_id,
             v_now, v_now, false, false)
        RETURNING "Id" INTO v_lic_id;

        INSERT INTO "CompanyLicenseAuditLogs"
            ("Id", "ProfileId", "LicenseKey", "LicenseType", "CompanySubscriptionId", "PlanId",
             "ExpiryDate", "CreatedBy", "UpdatedBy", "CreatedAt", "UpdatedAt", "IsDel",
             "LoggedAt", "VersionNumber", "OperationType")
        VALUES
            (v_lic_id, v_pid, format('CLD-TRI001-%s', v_pid), 'Trial', v_sub_id, v_plan_id,
             v_end, v_admin_id, v_admin_id, v_now, v_now, false,
             v_now, 1, 'INSERT');

        -- Profile Admin also becomes Team Manager. A user with no UserRoles rows
        -- yet keeps their legacy role as a row first, so they are not demoted.
        INSERT INTO "UserRoles" ("UserId", "UserGroupId", "CreatedDate")
        SELECT v_admin_id, 1, v_now
        WHERE NOT EXISTS (SELECT 1 FROM "UserRoles" WHERE "UserId" = v_admin_id);

        INSERT INTO "UserRoles" ("UserId", "UserGroupId", "CreatedDate")
        SELECT v_admin_id, 2, v_now
        WHERE NOT EXISTS (SELECT 1 FROM "UserRoles" WHERE "UserId" = v_admin_id AND "UserGroupId" = 2);

        RAISE NOTICE 'Profile %: subscription %, licence % held by user %.', v_pid, v_sub_id, v_lic_id, v_admin_id;
    END LOOP;
END $$;

-- Check
SELECT l."ProfileId", l."Id", l."LicenseKey", l."ExpiryDate", l."AssignedUserId", u."Username"
FROM "CompanyLicenses" l
LEFT JOIN "Users" u ON u."UserId" = l."AssignedUserId"
WHERE l."ProfileId" = ANY (ARRAY[52]) AND NOT l."IsDel"
ORDER BY l."ProfileId", l."Id";
