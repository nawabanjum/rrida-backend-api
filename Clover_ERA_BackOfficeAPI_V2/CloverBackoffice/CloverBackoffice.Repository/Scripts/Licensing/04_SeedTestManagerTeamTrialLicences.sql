-- Test data for one profile: a Team Manager "Test Manager", a team
-- "Test One Team" managed by them, and 10 one-year Trial licences.
--
-- Set v_profile_id below and run the whole file (F5). Re-runnable: an existing
-- manager (same email), team (same name) or seeded licence batch is reused,
-- never duplicated.
--
-- Where the licences go (same as the app, see CompanyLicenseRepository):
--   * All 10 belong to the PROFILE (CompanyLicenses.ProfileId) under their own
--     Trial subscription + $0 Trial payment, like
--     AccountRegistrationRepository.ProvisionRegistrationTrialAsync.
--   * 1 is HELD by the Team Manager (CompanyLicenses.AssignedUserId), as
--     create-manager does via AssignLicenseToUserAsync. Licences are consumed
--     per Team Manager (1 licence = 1 TM, up to 3 teams).
--   * That held licence also COVERS the team (AssignedCompanyLicenses row), as
--     EnsureTeamCoverageAsync -> TryCoverWithHeldLicenseAsync does; licence-gated
--     dashboards read this row.
--   * The other 9 stay free in the profile pool (available for new TMs).
--
-- The manager is created exactly like create-manager / Add User: Pending
-- (Status 2), not verified, no password, no invitation e-mail. Set
-- v_password_hash to a BCrypt hash to make them Active + verified so they can
-- log in straight away.

DO $$
DECLARE
    -- ===== inputs ==========================================================
    v_profile_id     int  := 0;                   -- <== SET THIS
    v_manager_email  text := NULL;                -- NULL -> test.manager.p{ProfileId}@{profile domain}
    v_manager_name   text := 'Test Manager';
    v_team_name      text := 'Test One Team';
    v_licence_count  int  := 10;
    v_validity       interval := interval '1 year';
    v_password_hash  text := NULL;                -- optional BCrypt hash ($2a$11$...)
    -- =======================================================================

    c_marker         constant text := 'Seed: Test One Team script';
    v_now            timestamptz := now();
    v_now_text       text := to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"');
    v_end            timestamptz := now() + interval '1 year';
    v_domain         text;
    v_actor_id       int;
    v_manager_id     int;
    v_manager_pid    int;
    v_unit_id        bigint;
    v_team_created   boolean := false;
    v_code           text;
    v_short          text;
    v_plan_id        int;
    v_people         int;
    v_sub_id         int;
    v_lic_id         int;
    v_key            text;
    v_seq            int := 1;
    i                int;
BEGIN
    v_end := v_now + v_validity;

    -- ---------- profile ----------
    SELECT p."DomainName" INTO v_domain
    FROM "Profiles" p
    WHERE p."Id" = v_profile_id AND NOT p."IsDel";

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Profile % not found or deleted. Set v_profile_id.', v_profile_id;
    END IF;

    -- DomainName holds a host, a few historic rows a whole address (HostOf).
    v_domain := btrim(coalesce(v_domain, ''));
    IF position('@' IN v_domain) > 0 THEN
        v_domain := substring(v_domain FROM '@([^@]*)$');
    END IF;
    IF v_domain = '' THEN v_domain := 'yopmail.com'; END IF;

    v_manager_email := coalesce(nullif(btrim(v_manager_email), ''),
                                format('test.manager.p%s@%s', v_profile_id, v_domain));

    -- Actor for CreatedBy columns: oldest live Profile Admin of the profile.
    SELECT u."UserId" INTO v_actor_id
    FROM "Users" u
    WHERE u."ProfileId" = v_profile_id AND NOT coalesce(u."IsDel", false)
      AND (EXISTS (SELECT 1 FROM "UserRoles" r WHERE r."UserId" = u."UserId" AND r."UserGroupId" = 1)
           OR u."UserGroupId" = 1)
    ORDER BY u."UserId"
    LIMIT 1;

    -- Same per-profile licence lock the app holds (Add User, create-manager, team create).
    PERFORM pg_advisory_xact_lock(16016, v_profile_id);

    -- ---------- 1. Team Manager ----------
    SELECT u."UserId", u."ProfileId" INTO v_manager_id, v_manager_pid
    FROM "Users" u
    WHERE lower(u."Username") = lower(v_manager_email) AND NOT coalesce(u."IsDel", false);

    IF v_manager_id IS NOT NULL AND v_manager_pid IS DISTINCT FROM v_profile_id THEN
        RAISE EXCEPTION 'E-mail % already belongs to user % of profile %.', v_manager_email, v_manager_id, v_manager_pid;
    END IF;

    IF v_manager_id IS NULL THEN
        -- Values as AccountRegistrationService.UpsertUserAsync writes them.
        INSERT INTO "Users"
            ("TenantId", "Username", "Name", "CreatedDate", "ModifiedDate", "CreatedBy",
             "IsDel", "IsActive", "IsVerified", "Password", "Status", "Type",
             "IsEntraId", "IsFirstTimePasswordChange", "ProfileId", "FailedLoginAttempts", "UserGroupId")
        VALUES
            ('', v_manager_email, v_manager_name, v_now_text, v_now_text, 'System',
             false, true, v_password_hash IS NOT NULL, coalesce(v_password_hash, ''),
             CASE WHEN v_password_hash IS NULL THEN '2' ELSE '1' END, 'PatnerUser',
             false, false, v_profile_id, 0, 2)
        RETURNING "UserId" INTO v_manager_id;

        RAISE NOTICE 'Manager created: user % (%).', v_manager_id, v_manager_email;
    ELSE
        RAISE NOTICE 'Manager reused: user % (%).', v_manager_id, v_manager_email;
    END IF;

    -- Team Manager role (UserRoles is the source of truth; 2 = Team Manager).
    INSERT INTO "UserRoles" ("UserId", "UserGroupId", "CreatedBy", "CreatedDate")
    VALUES (v_manager_id, 2, v_actor_id, v_now)
    ON CONFLICT ("UserId", "UserGroupId") DO NOTHING;

    IF v_actor_id IS NULL THEN v_actor_id := v_manager_id; END IF;

    -- ---------- 2. Team ----------
    SELECT b."Id" INTO v_unit_id
    FROM "BusinessUnits" b
    WHERE b."ProfileId" = v_profile_id AND b."IsDel" IS NOT TRUE
      AND lower(btrim(b."Name")) = lower(btrim(v_team_name))
    ORDER BY b."Id"
    LIMIT 1;

    IF v_unit_id IS NULL THEN
        -- Values as CompanyTeamService.UpsertBusinessUnitAsync writes them.
        v_code  := gen_random_uuid()::text;
        v_short := left(v_code, 8);

        INSERT INTO "BusinessUnits"
            ("BusinessUnitCode", "BusinessUnitShortCode", "Name", "NameInvariant",
             "RegistrationDate", "ProfileId", "ActiveDailyQuestionSetGroupId", "IsDel",
             "ParentId", "TeamSize", "ContactEmailAddress", "ContactFirstName", "ContactLastName")
        VALUES
            (v_code, v_short, v_team_name, format('%s(%s, %s)', v_team_name, v_code, v_short),
             v_now, v_profile_id, 1, false,
             NULL, NULL, format('default(%s, %s)@%s', v_code, v_short, v_domain), v_team_name, v_team_name)
        RETURNING "Id" INTO v_unit_id;

        v_team_created := true;
        RAISE NOTICE 'Team created: unit % (%).', v_unit_id, v_team_name;
    ELSE
        RAISE NOTICE 'Team reused: unit % (%).', v_unit_id, v_team_name;
    END IF;

    -- Manager on the team (UserTeams). One manager per team: any other active
    -- manager / pending replacement is unassigned, as create-manager does.
    IF NOT EXISTS (SELECT 1 FROM "UserTeams"
                   WHERE "UnitId" = v_unit_id AND "UserId" = v_manager_id
                     AND NOT coalesce("IsUnAssigned", false)) THEN
        UPDATE "UserTeams"
        SET "IsUnAssigned" = true, "IsPendingActivation" = false
        WHERE "UnitId" = v_unit_id
          AND (NOT coalesce("IsUnAssigned", false) OR "IsPendingActivation");

        INSERT INTO "UserTeams"
            ("UserId", "CreatedBy", "CreatedDate", "UnitId", "IsUnAssigned", "IsReadOnly",
             "IsNewTeam", "IsPendingActivation")
        VALUES
            (v_manager_id, v_actor_id, v_now, v_unit_id, false, false,
             v_team_created, false);
    END IF;

    -- ---------- 3. Licences (profile pool, own Trial subscription) ----------
    IF NOT EXISTS (SELECT 1 FROM "CompanyLicenses"
                   WHERE "ProfileId" = v_profile_id AND "Notes" = c_marker) THEN

        -- Plan sized for the licence count (same MaxTeams lookup the app uses).
        SELECT "Id", "MaxPeoplePerTeam" INTO v_plan_id, v_people
        FROM "Plans"
        WHERE "IsActive" AND "MaxTeams" >= v_licence_count
        ORDER BY "MaxTeams", "SortOrder"
        LIMIT 1;

        IF v_plan_id IS NULL THEN
            SELECT "Id", "MaxPeoplePerTeam" INTO v_plan_id, v_people
            FROM "Plans" WHERE "IsActive" ORDER BY "SortOrder" LIMIT 1;
        END IF;

        -- Subscription (BillingCycle 2 = Annual, Status 1 = Trial) + audit row.
        INSERT INTO "CompanySubscriptions"
            ("ProfileId", "PlanId", "BillingCycle", "Status", "TeamCount",
             "MaxTeamsAllowed", "MaxPeopleAllowed", "TrialStartDate", "TrialEndDate",
             "CurrentPeriodStart", "CurrentPeriodEnd", "IsTrialUsed", "CancelAtPeriodEnd",
             "IsDel", "CreatedAt", "UpdatedAt")
        VALUES
            (v_profile_id, v_plan_id, 2, 1, v_licence_count,
             v_licence_count, v_licence_count * coalesce(v_people, 20), v_now, v_end,
             v_now, v_end, true, false,
             false, v_now, v_now)
        RETURNING "Id" INTO v_sub_id;

        INSERT INTO "CompanySubscriptionAuditLogs"
            ("Id", "ProfileId", "PlanId", "BillingCycle", "Status", "TeamCount",
             "MaxTeamsAllowed", "MaxPeopleAllowed", "TrialStartDate", "TrialEndDate",
             "CurrentPeriodStart", "CurrentPeriodEnd", "IsTrialUsed", "CancelAtPeriodEnd",
             "CreatedAt", "UpdatedAt", "IsDel", "LoggedAt", "VersionNumber", "OperationType")
        VALUES
            (v_sub_id, v_profile_id, v_plan_id, 2, 1, v_licence_count,
             v_licence_count, v_licence_count * coalesce(v_people, 20), v_now, v_end,
             v_now, v_end, true, false,
             v_now, v_now, false, v_now, 1, 'INSERT');

        -- $0 trial payment (PaymentMethod 3 = Trial, Status 2 = Succeeded).
        INSERT INTO "Payments"
            ("ProfileId", "CompanySubscriptionId", "Amount", "Currency",
             "PaymentMethod", "Status", "Description", "CreatedAt", "UpdatedAt")
        VALUES
            (v_profile_id, v_sub_id, 0, 'usd', 3, 2,
             format('Trial activation - %s license(s) - %s days', v_licence_count,
                    extract(day FROM v_end - v_now)::int),
             v_now, v_now);

        -- Licences: next free CLD-TRI{nnn}-{ProfileId} keys (as the backfill does).
        FOR i IN 1..v_licence_count LOOP
            LOOP
                v_key := format('CLD-TRI%s-%s', lpad(v_seq::text, 3, '0'), v_profile_id);
                v_seq := v_seq + 1;
                EXIT WHEN NOT EXISTS (SELECT 1 FROM "CompanyLicenses" WHERE "LicenseKey" = v_key);
            END LOOP;

            INSERT INTO "CompanyLicenses"
                ("ProfileId", "LicenseKey", "LicenseType", "CompanySubscriptionId", "PlanId",
                 "ExpiryDate", "Notes", "CreatedBy", "UpdatedBy", "CreatedAt", "UpdatedAt",
                 "IsDel", "CancelAtPeriodEnd")
            VALUES
                (v_profile_id, v_key, 'Trial', v_sub_id, v_plan_id,
                 v_end, c_marker, v_actor_id, v_actor_id, v_now, v_now,
                 false, false)
            RETURNING "Id" INTO v_lic_id;

            INSERT INTO "CompanyLicenseAuditLogs"
                ("Id", "ProfileId", "LicenseKey", "LicenseType", "CompanySubscriptionId", "PlanId",
                 "ExpiryDate", "Notes", "CreatedBy", "UpdatedBy", "CreatedAt", "UpdatedAt", "IsDel",
                 "LoggedAt", "VersionNumber", "OperationType")
            VALUES
                (v_lic_id, v_profile_id, v_key, 'Trial', v_sub_id, v_plan_id,
                 v_end, c_marker, v_actor_id, v_actor_id, v_now, v_now, false,
                 v_now, 1, 'INSERT');
        END LOOP;

        RAISE NOTICE 'Licences created: % on subscription %, expiring %.', v_licence_count, v_sub_id, v_end;
    ELSE
        RAISE NOTICE 'Seeded licences already exist for profile %, none minted.', v_profile_id;
    END IF;

    -- ---------- 4. Manager holds one licence (AssignLicenseToUserAsync) ----------
    -- Their current live licence, else the first free seeded one.
    SELECT l."Id" INTO v_lic_id
    FROM "CompanyLicenses" l
    WHERE l."ProfileId" = v_profile_id AND NOT l."IsDel"
      AND (l."ExpiryDate" IS NULL OR l."ExpiryDate" > v_now)
      AND l."AssignedUserId" = v_manager_id
    ORDER BY l."Id"
    LIMIT 1;

    IF v_lic_id IS NULL THEN
        SELECT l."Id" INTO v_lic_id
        FROM "CompanyLicenses" l
        WHERE l."ProfileId" = v_profile_id AND NOT l."IsDel" AND l."Notes" = c_marker
          AND (l."ExpiryDate" IS NULL OR l."ExpiryDate" > v_now)
          AND l."AssignedUserId" IS NULL
          AND NOT EXISTS (SELECT 1 FROM "AssignedCompanyLicenses" a
                          JOIN "BusinessUnits" b ON b."Id" = a."BusinessUnitId" AND b."IsDel" IS NOT TRUE
                          WHERE a."CompanyLicenseId" = l."Id" AND a."IsActive" AND NOT a."IsDel")
        ORDER BY l."Id"
        LIMIT 1;

        IF v_lic_id IS NOT NULL THEN
            UPDATE "CompanyLicenses"
            SET "AssignedUserId" = v_manager_id, "AssignedUserAt" = v_now,
                "UpdatedAt" = v_now, "UpdatedBy" = v_actor_id
            WHERE "Id" = v_lic_id;
        END IF;
    END IF;

    IF v_lic_id IS NULL THEN
        RAISE NOTICE 'No free licence for manager %; left unlicensed.', v_manager_id;
    END IF;

    -- ---------- 5. Team covered by that licence (TryCoverWithHeldLicenseAsync) ----------
    IF v_lic_id IS NOT NULL
       AND NOT EXISTS (   -- team already has a live licence (purchased or complimentary)
            SELECT 1 FROM "CompanyLicenses" l
            WHERE NOT l."IsDel" AND (l."ExpiryDate" IS NULL OR l."ExpiryDate" > v_now)
              AND (EXISTS (SELECT 1 FROM "AssignedCompanyLicenses" a
                           WHERE a."CompanyLicenseId" = l."Id" AND a."BusinessUnitId" = v_unit_id
                             AND a."IsActive" AND NOT a."IsDel")
                   OR EXISTS (SELECT 1 FROM "ComplimentaryLicenseGrants" g
                              WHERE g."CompanyLicenseId" = l."Id" AND g."ComplimentaryBusinessUnitId" = v_unit_id
                                AND g."IsActive" AND NOT g."IsDel")))
    THEN
        IF EXISTS (SELECT 1 FROM "AssignedCompanyLicenses"
                   WHERE "CompanyLicenseId" = v_lic_id AND "IsActive") THEN
            RAISE NOTICE 'Licence % is already on another team; team % left uncovered.', v_lic_id, v_unit_id;
        ELSE
            INSERT INTO "AssignedCompanyLicenses"
                ("CompanyLicenseId", "BusinessUnitId", "AssignedAt", "IsActive",
                 "CreatedBy", "UpdatedBy", "CreatedAt", "UpdatedAt", "IsDel")
            VALUES
                (v_lic_id, v_unit_id, v_now, true,
                 v_actor_id, v_actor_id, v_now, v_now, false);
        END IF;
    END IF;

    RAISE NOTICE 'Done: profile %, manager %, team %, manager licence %.', v_profile_id, v_manager_id, v_unit_id, v_lic_id;
END $$;

-- ---------- Check (set the same profile id) ----------
WITH p AS (SELECT 0::bigint AS pid)   -- <== SET THIS
SELECT 'licence' AS row_type, l."Id", l."LicenseKey" AS name, l."LicenseType" AS detail,
       l."ExpiryDate", l."AssignedUserId" AS held_by_user,
       (SELECT a."BusinessUnitId" FROM "AssignedCompanyLicenses" a
        WHERE a."CompanyLicenseId" = l."Id" AND a."IsActive" AND NOT a."IsDel" LIMIT 1) AS on_team,
       l."CompanySubscriptionId" AS subscription
FROM "CompanyLicenses" l, p
WHERE l."ProfileId" = p.pid AND l."Notes" = 'Seed: Test One Team script'
UNION ALL
SELECT 'team', b."Id", b."Name", b."BusinessUnitShortCode", NULL,
       ut."UserId", NULL, NULL
FROM "BusinessUnits" b
CROSS JOIN p
LEFT JOIN "UserTeams" ut ON ut."UnitId" = b."Id" AND NOT coalesce(ut."IsUnAssigned", false)
WHERE b."ProfileId" = p.pid AND b."Name" = 'Test One Team' AND b."IsDel" IS NOT TRUE
UNION ALL
SELECT 'manager', u."UserId", u."Username", 'Status ' || u."Status", NULL,
       NULL, NULL, NULL
FROM "Users" u, p
WHERE u."ProfileId" = p.pid AND u."Name" = 'Test Manager' AND NOT coalesce(u."IsDel", false)
ORDER BY 1, 2;
