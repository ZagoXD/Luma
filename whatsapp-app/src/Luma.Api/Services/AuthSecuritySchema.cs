using Luma.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Luma.Api.Services;

public static class AuthSecuritySchema
{
    public static Task EnsureAsync(LumaDbContext db) => db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE account_users ADD COLUMN IF NOT EXISTS "SessionVersion" integer NOT NULL DEFAULT 0;
        ALTER TABLE account_sessions ADD COLUMN IF NOT EXISTS "AccountVersion" integer NOT NULL DEFAULT 0;
        ALTER TABLE account_sessions ADD COLUMN IF NOT EXISTS "RefreshGeneration" bigint NOT NULL DEFAULT 0;
        ALTER TABLE account_sessions ADD COLUMN IF NOT EXISTS "PreviousValidUntil" timestamptz;
        ALTER TABLE account_sessions ADD COLUMN IF NOT EXISTS "RevokedAt" timestamptz;
        ALTER TABLE password_reset_tokens ADD COLUMN IF NOT EXISTS "AccountVersion" integer NOT NULL DEFAULT 0;
        CREATE TABLE IF NOT EXISTS auth_rate_limits (
            "Key" varchar(128) PRIMARY KEY, "Attempts" integer NOT NULL, "ExpiresAt" timestamptz NOT NULL);
        CREATE INDEX IF NOT EXISTS "IX_auth_rate_limits_ExpiresAt" ON auth_rate_limits ("ExpiresAt");
        """);
}
