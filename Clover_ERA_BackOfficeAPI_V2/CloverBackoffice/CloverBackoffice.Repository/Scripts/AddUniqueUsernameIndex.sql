-- Users.Username uniqueness — DB-level safety net.
--
-- Background: Contacts.EmailAddress already carries a UNIQUE index
-- (IX_Contacts_EmailAddress), but Users.Username — the actual login identity —
-- has none. Nothing at the DB level stopped the same email being registered
-- twice; the only guard was an application check that queried the wrong table.
--
-- Run STEP 1 first. The index in STEP 2 will NOT be created while duplicates
-- remain, so the existing rows must be resolved by hand.

--------------------------------------------------------------------------------
-- STEP 1 — inspect current duplicates
--------------------------------------------------------------------------------
SELECT lower("Username")                            AS email,
       count(*)                                     AS row_count,
       array_agg("UserId"     ORDER BY "UserId")    AS user_ids,
       array_agg("Type"       ORDER BY "UserId")    AS types,
       array_agg("CompanyId"  ORDER BY "UserId")    AS company_ids,
       array_agg("IsDel"      ORDER BY "UserId")    AS is_deleted,
       array_agg("CreatedDate" ORDER BY "UserId")   AS created
FROM   public."Users"
WHERE  NOT "IsDel"
GROUP  BY 1
HAVING count(*) > 1
ORDER  BY 2 DESC;

--------------------------------------------------------------------------------
-- STEP 1b — remediation, one group at a time. NOT automated on purpose: which
-- row survives is a business call (the older account may hold the real data, the
-- newer one may hold the live subscription).
--
-- Preferred: retire the losing row instead of deleting it, so audit history and
-- any FK references (CompanyLinks, UserTokens, licence audit rows) stay intact.
--
--   UPDATE public."Users"
--   SET    "Username"     = 'dup_' || "UserId" || '_' || "Username",
--          "IsDel"        = true,
--          "IsActive"     = false,
--          "ModifiedDate" = to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
--   WHERE  "UserId" = <losing_user_id>;
--
-- Known groups in CloverEra_Dev as of 2026-08-04:
--   xyzca@yopmail.com              UserId 173 (PatnerUser, co 35)  + 371 (Company, co 251)
--   LLMCA@yopmail.com              UserId 127 (PatnerUser, co 73)  + 327 (Company, co 209)
--   clive.hays@cloverera.com       UserId 85  + 88   -- legacy import, CompanyId NULL on 88
--   neil.hays@cloverera.com        UserId 91  + 93   -- legacy import, 93 already IsDel
--   del_demouserbug22@yopmail.com  UserId 145, 148, 149 -- all IsDel; soft-delete rename collision
--------------------------------------------------------------------------------

--------------------------------------------------------------------------------
-- STEP 2 — enforce uniqueness
--
-- Case-insensitive: the app compares with ToLower(), so a plain btree on
-- "Username" would still let 'User@x.com' and 'user@x.com' coexist (exactly what
-- happened on Contacts, where Clive.Hays@ and clive.hays@ both exist).
--
-- Partial (WHERE NOT "IsDel"): DeleteUserAsync only flips IsDel, so a deleted
-- account must not hold its email hostage forever.
--------------------------------------------------------------------------------
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS "IX_Users_Username_Unique"
    ON public."Users" (lower("Username"))
    WHERE NOT "IsDel";

--------------------------------------------------------------------------------
-- STEP 3 — verify
--------------------------------------------------------------------------------
SELECT i.relname, ix.indisunique, pg_get_indexdef(ix.indexrelid)
FROM   pg_index ix
JOIN   pg_class i ON i.oid = ix.indexrelid
JOIN   pg_class t ON t.oid = ix.indrelid
WHERE  t.relname = 'Users';
