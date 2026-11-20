-- Scrub personal data from an RRIDA non-production database restored from the
-- CloverEra production dump.
--
-- Design: row counts, foreign keys, scores, sentiment and dates are left alone,
-- so licensing, CLOVER scores and the report loop still behave like production.
-- Only identifying values and free text written by real employees are replaced.
--
-- Covers both schemas in the dump: "public" (79 tables) and "export" (13 copies,
-- which include Companies, Contacts and DailyFeedback and so carry PII too).
--
-- Run AFTER pg_restore and baseline-migrations.sql:
--   psql "host=... dbname=rrida user=rridaadmin sslmode=require" -f scrub-pii.sql
--
-- Refuses to run against anything that looks like production.

DO $$
BEGIN
    IF current_database() ILIKE '%prod%' OR current_database() = 'CloverEra' THEN
        RAISE EXCEPTION 'Refusing to scrub %: this looks like a production database.', current_database();
    END IF;
    RAISE NOTICE 'Scrubbing database %.', current_database();
END $$;

BEGIN;

-- Helper: run a statement only if the table actually exists in that schema.
CREATE OR REPLACE FUNCTION pg_temp.scrub_if_exists(target_schema text, target_table text, statement text)
RETURNS void LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = target_schema AND table_name = target_table
    ) THEN
        EXECUTE format(statement, target_schema, target_table);
        RAISE NOTICE 'Scrubbed %.%', target_schema, target_table;
    END IF;
END $$;

DO $$
DECLARE
    s text;
BEGIN
    FOREACH s IN ARRAY ARRAY['public', 'export'] LOOP

        -- 1. Credentials and sessions. Deleted outright: stale production tokens
        --    must never authenticate anywhere, and password hashes are worth stealing.
        PERFORM pg_temp.scrub_if_exists(s, 'UserTokens',         'DELETE FROM %I.%I');
        PERFORM pg_temp.scrub_if_exists(s, 'ResetPasswordLinks', 'DELETE FROM %I.%I');

        -- Nulling the hash means no one can sign in with a production password.
        -- For a usable dev login, add PGCRYPTO to azure.extensions and set a known
        -- hash, or register a fresh user through the app.
        PERFORM pg_temp.scrub_if_exists(s, 'Users', 'UPDATE %I.%I SET "Password" = NULL');

        -- 2. Direct identifiers. .invalid is reserved by RFC 2606 and can never
        --    route, so a stray dev email send cannot reach a real person.
        PERFORM pg_temp.scrub_if_exists(s, 'Users',
            'UPDATE %I.%I SET "Username" = ''user'' || "UserId" || ''@rrida-dev.invalid'', "Name" = ''User '' || "UserId"');

        PERFORM pg_temp.scrub_if_exists(s, 'Contacts',
            'UPDATE %I.%I SET "EmailAddress" = ''contact'' || "Id" || ''@rrida-dev.invalid'', "FirstName" = ''First'' || "Id", "LastName" = ''Last'' || "Id"');

        -- Company codes stay: they are join keys and business logic reads them.
        PERFORM pg_temp.scrub_if_exists(s, 'Companies',
            'UPDATE %I.%I SET "Name" = ''Company '' || "Id", "NameInvariant" = ''company '' || "Id", "DomainName" = ''company'' || "Id" || ''.invalid''');

        -- 3. Free text written by employees about their workplace. Sentiment,
        --    question and reason ids survive, so scores and themes still compute.
        PERFORM pg_temp.scrub_if_exists(s, 'DailyFeedback',
            'UPDATE %I.%I SET "FreeText" = ''[redacted]'' WHERE "FreeText" IS NOT NULL');

        PERFORM pg_temp.scrub_if_exists(s, 'EranCoachMessages',
            'UPDATE %I.%I SET "Content" = ''[redacted]'' WHERE "Content" IS NOT NULL AND "Content" <> ''''');

    END LOOP;
END $$;

-- 4. Payment provider references. Production Stripe ids are meaningless against
--    dev Stripe keys, and linking them invites a cross-environment mistake.
DO $$
DECLARE
    col record;
BEGIN
    FOR col IN
        SELECT table_schema, table_name, column_name
        FROM information_schema.columns
        WHERE table_schema IN ('public', 'export')
          AND column_name ILIKE '%stripe%'
          AND is_nullable = 'YES'
    LOOP
        EXECUTE format('UPDATE %I.%I SET %I = NULL', col.table_schema, col.table_name, col.column_name);
        RAISE NOTICE 'Cleared %.%.%', col.table_schema, col.table_name, col.column_name;
    END LOOP;
END $$;

-- 5. LLM and audit logs hold prompts and responses built from the same employee
--    text, so every plain text column on them is redacted.
DO $$
DECLARE
    col record;
    log_tables text[] := ARRAY[
        'AIRequestLogs',
        'LLMLogs',
        'FeedbackAnalysisResults',
        'EranCoachManagerMemories',
        'EranCoachConversations',
        'DailyFeedbackAuditLogs',
        'AuditLogs',
        'Notifications'
    ];
BEGIN
    FOR col IN
        SELECT table_schema, table_name, column_name
        FROM information_schema.columns
        WHERE table_schema IN ('public', 'export')
          AND table_name = ANY(log_tables)
          AND data_type = 'text'
          AND is_nullable = 'YES'
    LOOP
        EXECUTE format('UPDATE %I.%I SET %I = ''[redacted]'' WHERE %I IS NOT NULL',
                       col.table_schema, col.table_name, col.column_name, col.column_name);
        RAISE NOTICE 'Redacted %.%.%', col.table_schema, col.table_name, col.column_name;
    END LOOP;
END $$;

COMMIT;

-- 6. Prove it worked. Every count below should be 0.
SELECT 'users with a real-looking email' AS check, count(*) AS remaining
FROM public."Users" WHERE "Username" NOT LIKE '%@rrida-dev.invalid'
UNION ALL
SELECT 'users with a password hash', count(*)
FROM public."Users" WHERE "Password" IS NOT NULL
UNION ALL
SELECT 'contacts with a real-looking email', count(*)
FROM public."Contacts" WHERE "EmailAddress" NOT LIKE '%@rrida-dev.invalid'
UNION ALL
SELECT 'live user tokens', count(*) FROM public."UserTokens"
UNION ALL
SELECT 'unredacted feedback free text', count(*)
FROM public."DailyFeedback" WHERE "FreeText" IS NOT NULL AND "FreeText" <> '[redacted]';
