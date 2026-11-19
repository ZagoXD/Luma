using System.IdentityModel.Tokens.Jwt;
using Luma.Api.Data;
using Luma.Api.Models;
using Luma.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Luma.Tests;

public sealed class AccountSessionServiceTests
{
    [Fact]
    public async Task Access_is_short_lived_has_no_contact_data_and_refresh_is_hashed()
    {
        await using var db = Db();
        var clock = new Clock();
        var account = await Account(db);
        var tokens = await Service(db, clock).IssueAsync(account);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.Token);
        Assert.Equal("at+jwt", jwt.Header.Typ);
        Assert.DoesNotContain(jwt.Claims, c => c.Type is "email" or "phone");
        Assert.InRange(tokens.AccessExpiresAt - clock.GetUtcNow(), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(15));
        Assert.Equal(clock.GetUtcNow().AddDays(30), tokens.RefreshExpiresAt);
        Assert.Equal(AccountSecurity.HashToken(tokens.RefreshToken), (await db.AccountSessions.SingleAsync()).TokenHash);
        Assert.NotNull(await Service(db, clock).AuthenticateAsync(tokens.Token));
    }

    [Fact]
    public async Task Expired_access_can_refresh_but_absolute_refresh_expiry_requires_login()
    {
        await using var db = Db(); var clock = new Clock(); var service = Service(db, clock);
        var tokens = await service.IssueAsync(await Account(db));
        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Null(await service.AuthenticateAsync(tokens.Token));
        var renewed = await service.RefreshAsync(tokens.RefreshToken);
        Assert.NotNull(renewed);
        Assert.Equal(tokens.RefreshExpiresAt, renewed.RefreshExpiresAt);
        Assert.NotNull(await service.AuthenticateAsync(renewed.Token));
        clock.Advance(TimeSpan.FromDays(31));
        Assert.Null(await service.RefreshAsync(renewed.RefreshToken));
        Assert.Null(await service.AuthenticateAsync(renewed.Token));
    }

    [Fact]
    public async Task Rotation_handles_concurrent_retry_then_revokes_replayed_family()
    {
        await using var db = Db(); var clock = new Clock(); var service = Service(db, clock);
        var first = await service.IssueAsync(await Account(db));
        var next = await service.RefreshAsync(first.RefreshToken);
        var concurrent = await service.RefreshAsync(first.RefreshToken);
        Assert.NotNull(next); Assert.NotNull(concurrent);
        Assert.NotEqual(first.RefreshToken, next.RefreshToken);
        Assert.Equal(next.RefreshToken, concurrent.RefreshToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await service.RefreshAsync(first.RefreshToken));
        Assert.Null(await service.AuthenticateAsync(next.Token));
        Assert.Null(await service.RefreshAsync(next.RefreshToken));
    }

    [Fact]
    public async Task Forged_refresh_does_not_revoke_a_valid_session_and_logout_does()
    {
        await using var db = Db(); var clock = new Clock(); var service = Service(db, clock);
        var tokens = await service.IssueAsync(await Account(db));
        Assert.Null(await service.RefreshAsync(tokens.RefreshToken + "tampered"));
        Assert.NotNull(await service.AuthenticateAsync(tokens.Token));
        await service.RevokeAsync(tokens.RefreshToken);
        Assert.Null(await service.AuthenticateAsync(tokens.Token));
        Assert.Null(await service.RefreshAsync(tokens.RefreshToken));
    }

    [Fact]
    public async Task Logout_can_revoke_the_session_even_without_a_refresh_cookie()
    {
        await using var db = Db(); var clock = new Clock(); var service = Service(db, clock);
        var tokens = await service.IssueAsync(await Account(db));
        await service.RevokeAccessAsync(tokens.Token);
        Assert.Null(await service.AuthenticateAsync(tokens.Token));
        Assert.Null(await service.RefreshAsync(tokens.RefreshToken));
    }

    [Fact]
    public async Task Password_version_change_and_account_deletion_revoke_both_tokens()
    {
        await using var db = Db(); var clock = new Clock(); var service = Service(db, clock);
        var account = await Account(db);
        var old = await service.IssueAsync(account);
        account.SessionVersion++;
        await db.SaveChangesAsync();
        Assert.Null(await service.AuthenticateAsync(old.Token));
        Assert.Null(await service.RefreshAsync(old.RefreshToken));
        var current = await service.IssueAsync(account);
        account.DeletionRequestedAt = clock.GetUtcNow(); await db.SaveChangesAsync();
        Assert.Null(await service.AuthenticateAsync(current.Token));
        Assert.Null(await service.RefreshAsync(current.RefreshToken));
    }

    [Fact]
    public async Task Wrong_key_issuer_and_old_30_day_format_are_rejected()
    {
        await using var db = Db(); var clock = new Clock(); var account = await Account(db);
        var issued = await Service(db, clock).IssueAsync(account);
        Assert.Null(await Service(db, clock, "another-issuer").AuthenticateAsync(issued.Token));
        var legacy = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new("sub", account.Id.ToString())], expires: clock.GetUtcNow().AddDays(30).UtcDateTime,
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(new string('k',64))), "HS256")));
        Assert.Null(await Service(db, clock).AuthenticateAsync(legacy));
    }

    private static AccountSessionService Service(LumaDbContext db, Clock clock, string issuer = "luma-tests") => new(db,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Luma:JwtSigningKey"] = new string('k',64), ["Auth:Issuer"] = issuer, ["Auth:Audience"] = "luma-web"
        }).Build(), clock);
    private static LumaDbContext Db() { PrivacyRuntime.Reset(); return new(new DbContextOptionsBuilder<LumaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options); }
    private static async Task<AccountUser> Account(LumaDbContext db) {
        var account = new AccountUser { Email = "private@example.com", Cpf = "52998224725", PhoneNumber = "+5511999990001", PasswordHash = "hash" };
        db.Add(account); await db.SaveChangesAsync(); return account;
    }
    private sealed class Clock : TimeProvider {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan time) => now += time;
    }
}
