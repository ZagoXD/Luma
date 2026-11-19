using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Luma.Api.Data;
using Luma.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Luma.Api.Services;

public sealed record AccountTokens(string Token, string RefreshToken, DateTimeOffset AccessExpiresAt, DateTimeOffset RefreshExpiresAt);

public sealed class AccountSessionService(LumaDbContext db, IConfiguration configuration, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private string SigningKey => configuration["Luma:JwtSigningKey"] ?? throw new InvalidOperationException("Chave JWT não configurada.");
    private string Issuer => configuration["Auth:Issuer"] ?? "luma-api";
    private string Audience => configuration["Auth:Audience"] ?? "luma-web";

    public static void ValidateConfiguration(IConfiguration configuration)
    {
        var key = configuration["Luma:JwtSigningKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32 || key.Contains("change-me", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Luma:JwtSigningKey precisa ser uma chave segura com pelo menos 32 bytes.");
    }

    public async Task<AccountTokens> IssueAsync(AccountUser account, CancellationToken ct = default)
    {
        if (account.DeletionRequestedAt is not null) throw new InvalidOperationException("Conta em exclusão.");
        var session = new AccountSession { AccountUserId = account.Id, AccountVersion = account.SessionVersion,
            CreatedAt = time.GetUtcNow(), ExpiresAt = time.GetUtcNow().AddDays(30) };
        session.TokenHash = AccountSecurity.HashToken(RefreshValue(session, 0));
        db.AccountSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return Tokens(account, session);
    }

    public async Task<AccountUser?> AuthenticateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return null;
        try
        {
            var now = time.GetUtcNow();
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var claims = handler.ValidateToken(token, new TokenValidationParameters {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ValidateIssuer = true, ValidIssuer = Issuer, ValidateAudience = true, ValidAudience = Audience,
                RequireSignedTokens = true, RequireExpirationTime = true, ValidateLifetime = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ValidTypes = ["at+jwt"], ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (notBefore, expires, _, _) => expires is not null && expires > now.UtcDateTime
                    && (notBefore is null || notBefore <= now.UtcDateTime)
            }, out _);
            if (!Guid.TryParse(claims.FindFirstValue("sub"), out var accountId)
                || !Guid.TryParse(claims.FindFirstValue("sid"), out var sessionId)
                || !int.TryParse(claims.FindFirstValue("ver"), out var version)) return null;
            var session = await db.AccountSessions.Include(s => s.AccountUser).SingleOrDefaultAsync(s => s.Id == sessionId
                && s.AccountUserId == accountId && s.RevokedAt == null && s.ExpiresAt > now, ct);
            return session?.AccountUser is { DeletionRequestedAt: null } account && account.SessionVersion == version
                && session.AccountVersion == version ? account : null;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or FormatException) { return null; }
    }

    public async Task<AccountTokens?> RefreshAsync(string? token, CancellationToken ct = default)
    {
        if (!Parse(token, out var id, out var generation)) return null;
        await using var transaction = db.Database.IsNpgsql() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (transaction is not null) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"auth-session:" + id}, 0))", ct);
        var session = await db.AccountSessions.Include(s => s.AccountUser).SingleOrDefaultAsync(s => s.Id == id, ct);
        var now = time.GetUtcNow();
        if (session?.AccountUser is not { DeletionRequestedAt: null } account || session.RevokedAt is not null
            || session.ExpiresAt <= now || session.AccountVersion != account.SessionVersion || !ValidProof(token!, session, generation)) return null;
        if (generation == session.RefreshGeneration)
        {
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(AccountSecurity.HashToken(token!)), Encoding.ASCII.GetBytes(session.TokenHash))) return null;
            session.RefreshGeneration++;
            session.PreviousValidUntil = now.AddSeconds(15);
            session.TokenHash = AccountSecurity.HashToken(RefreshValue(session, session.RefreshGeneration));
        }
        else if (generation != session.RefreshGeneration - 1 || session.PreviousValidUntil is null || session.PreviousValidUntil < now)
        {
            // A genuine older credential outside the concurrency window signals replay of this session family.
            session.RevokedAt = now;
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return null;
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Tokens(account, session);
    }

    public async Task RevokeAsync(string? token, CancellationToken ct = default)
    {
        if (!Parse(token, out var id, out var generation)) return;
        var session = await db.AccountSessions.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session is null || !ValidProof(token!, session, generation)) return;
        session.RevokedAt ??= time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAccessAsync(string? token, CancellationToken ct = default)
    {
        if (await AuthenticateAsync(token, ct) is null) return;
        var id = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token!).Claims.Single(c => c.Type == "sid").Value);
        var session = await db.AccountSessions.SingleAsync(s => s.Id == id, ct);
        session.RevokedAt ??= time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private AccountTokens Tokens(AccountUser account, AccountSession session)
    {
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(10);
        if (expires > session.ExpiresAt) expires = session.ExpiresAt;
        var jwt = new JwtSecurityToken(Issuer, Audience, [new("sub", account.Id.ToString()), new("sid", session.Id.ToString()),
            new("ver", account.SessionVersion.ToString(CultureInfo.InvariantCulture)), new("jti", Guid.NewGuid().ToString("N")),
            new("iat", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)],
            now.UtcDateTime, expires.UtcDateTime, new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
        jwt.Header["typ"] = "at+jwt";
        return new(new JwtSecurityTokenHandler().WriteToken(jwt), RefreshValue(session, session.RefreshGeneration), expires, session.ExpiresAt);
    }

    private string RefreshValue(AccountSession session, long generation)
    {
        var prefix = session.Id.ToString("N") + "." + generation.ToString(CultureInfo.InvariantCulture);
        var proof = HMACSHA256.HashData(Encoding.UTF8.GetBytes(SigningKey), Encoding.UTF8.GetBytes("luma-refresh-v1:" + prefix + ":" + session.AccountVersion));
        return prefix + "." + Base64UrlEncoder.Encode(proof);
    }

    private bool ValidProof(string token, AccountSession session, long generation)
    {
        var expected = RefreshValue(session, generation);
        return expected.Length == token.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(token));
    }

    private static bool Parse(string? token, out Guid id, out long generation)
    {
        id = default; generation = default;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) return false;
        var parts = token.Split('.');
        return parts.Length == 3 && Guid.TryParseExact(parts[0], "N", out id)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out generation) && generation >= 0 && parts[2].Length == 43;
    }
}
