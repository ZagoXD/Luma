-- Optional migration for the OLD Supabase list. The live Pages site uses D1.
-- Does not migrate contacts, grant access or reinterpret legacy consents.
BEGIN;
ALTER TABLE public.waitlist_signups
  ALTER COLUMN whatsapp DROP NOT NULL,
  ALTER COLUMN whatsapp_normalized DROP NOT NULL,
  ADD COLUMN IF NOT EXISTS email text,
  ADD COLUMN IF NOT EXISTS email_normalized text GENERATED ALWAYS AS (lower(nullif(btrim(email), ''))) STORED,
  ADD COLUMN IF NOT EXISTS terms_accepted boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS launch_email_opt_in boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS launch_whatsapp_opt_in boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS terms_version text,
  ADD COLUMN IF NOT EXISTS privacy_version text,
  ADD COLUMN IF NOT EXISTS launch_consent_version text,
  ADD COLUMN IF NOT EXISTS consent_recorded_at timestamptz;
CREATE UNIQUE INDEX IF NOT EXISTS waitlist_signups_email_normalized_key
  ON public.waitlist_signups(email_normalized) WHERE email_normalized IS NOT NULL;
-- NOT VALID preserves old rows; new writes with channel opt-in must carry their evidence.
ALTER TABLE public.waitlist_signups ADD CONSTRAINT waitlist_signups_channel_consent_check
  CHECK (
    (NOT launch_email_opt_in OR email_normalized IS NOT NULL)
    AND (NOT launch_whatsapp_opt_in OR (whatsapp_normalized IS NOT NULL AND whatsapp_normalized ~ '^\+?55[1-9][0-9]9[0-9]{8}$'))
    AND (NOT (launch_email_opt_in OR launch_whatsapp_opt_in) OR
      (terms_accepted AND consent_accepted AND terms_version IS NOT NULL AND privacy_version IS NOT NULL
        AND launch_consent_version IS NOT NULL AND consent_recorded_at IS NOT NULL))
  ) NOT VALID;
COMMIT;
