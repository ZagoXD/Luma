using Luma.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Billing;

public static class BillingSchema
{
    public static Task EnsureAsync(LumaDbContext db) => db.Database.ExecuteSqlRawAsync("""
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "BillingProvider" varchar(32) NOT NULL DEFAULT 'legacy';
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "PaymentMethod" varchar(16) NOT NULL DEFAULT 'card';
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "PaymentAttemptedAt" timestamptz;
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "ReplacesSubscriptionId" uuid;
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "ReplacedBySubscriptionId" uuid;
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "ReplacementConfirmedAt" timestamptz;
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "PendingPlanCode" varchar(32);
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "PendingBillingInterval" varchar(32);
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "ContractAmount" numeric(18,2);
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "PendingContractAmount" numeric(18,2);
UPDATE account_subscriptions SET "ContractAmount" = CASE
 WHEN "PlanCode"='basico' AND "BillingInterval"='annual' THEN 70.80
 WHEN "PlanCode"='basico' THEN 11.00
 WHEN "PlanCode"='essencial' AND "BillingInterval"='annual' THEN 118.80
 WHEN "PlanCode"='essencial' THEN 20.00 END WHERE "ContractAmount" IS NULL;
UPDATE account_subscriptions SET "PendingContractAmount" = CASE
 WHEN "PendingPlanCode"='basico' AND "PendingBillingInterval"='annual' THEN 70.80
 WHEN "PendingPlanCode"='basico' THEN 11.00
 WHEN "PendingPlanCode"='essencial' AND "PendingBillingInterval"='annual' THEN 118.80
 WHEN "PendingPlanCode"='essencial' THEN 20.00 END
 WHERE "PendingContractAmount" IS NULL AND "PendingPlanCode" IS NOT NULL;
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "TerminatedAt" timestamptz;
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "TerminationConfirmedAt" timestamptz;
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "WithdrawalRequestedAt" timestamptz;
ALTER TABLE account_subscriptions ADD COLUMN IF NOT EXISTS "WithdrawalCompletedAt" timestamptz;
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "TermsVersion" text;
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "TermsAcceptedAt" timestamptz;
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "PrivacyVersion" text;
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "HealthConsentAcceptedAt" timestamptz;
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "DeletionRequestedAt" timestamptz;
ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "DeletionCompletedAt" timestamptz;
CREATE TABLE IF NOT EXISTS billing_payments (
    "Id" varchar(160) PRIMARY KEY,
    "AccountSubscriptionId" uuid NOT NULL REFERENCES account_subscriptions ("Id") ON DELETE CASCADE,
    "Status" varchar(32) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" varchar(3) NOT NULL,
    "ApprovedAt" timestamptz NOT NULL,
    "PeriodEndsAt" timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS "IX_billing_payments_AccountSubscriptionId" ON billing_payments ("AccountSubscriptionId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_billing_provider_subscription" ON account_subscriptions ("BillingProvider", "ProviderSubscriptionId") WHERE "ProviderSubscriptionId" IS NOT NULL;
-- One-way data migration only. No legacy payment SDK or runtime integration is retained.
DO $$ BEGIN
IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='account_users' AND column_name='StripeCustomerId') THEN
    UPDATE account_users SET "BillingCustomerId" = COALESCE("BillingCustomerId", "StripeCustomerId");
    ALTER TABLE account_users DROP COLUMN "StripeCustomerId";
END IF;
IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='account_subscriptions' AND column_name='StripeSubscriptionId') THEN
    UPDATE account_subscriptions SET "ProviderSubscriptionId" = COALESCE("ProviderSubscriptionId", "StripeSubscriptionId"), "ProviderPlanId" = COALESCE("ProviderPlanId", "StripePriceId");
    ALTER TABLE account_subscriptions DROP COLUMN "StripeSubscriptionId";
    ALTER TABLE account_subscriptions DROP COLUMN "StripePriceId";
END IF;
END $$;
""");
}
