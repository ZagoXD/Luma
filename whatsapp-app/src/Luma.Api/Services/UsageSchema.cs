using Luma.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace Luma.Api.Services;

public static class UsageSchema
{
    public static Task EnsureAsync(LumaDbContext db) => db.Database.ExecuteSqlRawAsync("""
CREATE TABLE IF NOT EXISTS usage_buckets ("Key" text PRIMARY KEY,"AccountId" uuid NOT NULL,"Kind" text NOT NULL,"Units" bigint NOT NULL,"ExpiresAt" timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS usage_receipts ("Id" text PRIMARY KEY,"AccountId" uuid NOT NULL,"Kind" text NOT NULL,"Units" integer NOT NULL,"CreatedAt" timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS usage_calls ("Id" uuid PRIMARY KEY,"AccountId" uuid NOT NULL,"Provider" text NOT NULL,"Model" text NOT NULL,"Status" text NOT NULL,"InputTokens" integer NOT NULL,"CachedTokens" integer NOT NULL,"OutputTokens" integer NOT NULL,"EstimatedUsd" numeric(18,8) NOT NULL,"ReferenceCostUsd" numeric(18,8),"CreatedAt" timestamptz NOT NULL);
CREATE INDEX IF NOT EXISTS ix_usage_bucket_account ON usage_buckets("AccountId");
CREATE INDEX IF NOT EXISTS ix_usage_receipt_account ON usage_receipts("AccountId","CreatedAt");
CREATE INDEX IF NOT EXISTS ix_usage_call_account ON usage_calls("AccountId","CreatedAt");
ALTER TABLE usage_calls ADD COLUMN IF NOT EXISTS "ReferenceCostUsd" numeric(18,8);
CREATE TABLE IF NOT EXISTS fiscal_documents ("Id" uuid PRIMARY KEY,"PaymentId" text NOT NULL UNIQUE,"Amount" numeric(18,2) NOT NULL,"Currency" text NOT NULL,"PaidAt" timestamptz NOT NULL,"Status" text NOT NULL,"Number" text,"AccessKey" text,"ProviderId" text,"Attempts" integer NOT NULL,"NextAttemptAt" timestamptz,"LastError" text,"UpdatedAt" timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS structured_record_sessions ("UserId" uuid PRIMARY KEY REFERENCES users("Id") ON DELETE CASCADE,"State" text NOT NULL,"ExpiresAt" timestamptz NOT NULL);
INSERT INTO fiscal_documents ("Id","PaymentId","Amount","Currency","PaidAt","Status","Attempts","UpdatedAt")
SELECT gen_random_uuid(),"Id","Amount","Currency","ApprovedAt",'awaiting_configuration',0,now()
FROM billing_payments WHERE "Status"='approved' AND "Amount">0
ON CONFLICT ("PaymentId") DO NOTHING;
""");
}
