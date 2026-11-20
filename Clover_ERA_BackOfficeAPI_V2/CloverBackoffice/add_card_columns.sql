-- Add CardLast4 + CardBrand to Payments and PaymentAuditLogs.
-- Card data never stored from the form (Stripe Checkout hosts it); these are
-- read back post-payment from the charge's payment_method_details.
-- Idempotent: safe to re-run.

ALTER TABLE public."Payments"
    ADD COLUMN IF NOT EXISTS "CardLast4" character varying(4),
    ADD COLUMN IF NOT EXISTS "CardBrand" character varying(20);

ALTER TABLE public."PaymentAuditLogs"
    ADD COLUMN IF NOT EXISTS "CardLast4" character varying(4),
    ADD COLUMN IF NOT EXISTS "CardBrand" character varying(20);
