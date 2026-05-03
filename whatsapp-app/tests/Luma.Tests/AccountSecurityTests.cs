using Luma.Api.Services;

namespace Luma.Tests;

public sealed class AccountSecurityTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("45815168890")]
    public void Cpf_validation_accepts_valid_values(string cpf)
    {
        Assert.True(AccountInputNormalizer.IsValidCpf(cpf));
    }

    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("12345678900")]
    [InlineData("45815168891")]
    public void Cpf_validation_rejects_invalid_values(string cpf)
    {
        Assert.False(AccountInputNormalizer.IsValidCpf(cpf));
    }

    [Theory]
    [InlineData("16992330309", "+5516992330309")]
    [InlineData("(16) 99233-0309", "+5516992330309")]
    [InlineData("+55 16 99233-0309", "+5516992330309")]
    [InlineData("1633334444", "+551633334444")]
    public void Phone_normalization_accepts_brazilian_phone_with_or_without_extra_nine(string phone, string expected)
    {
        Assert.Equal(expected, AccountInputNormalizer.NormalizeBrazilPhone(phone));
    }

    [Theory]
    [InlineData("+16992330309", "+5516992330309")]
    [InlineData("+1133334444", "+551133334444")]
    public void Phone_normalization_repairs_legacy_local_e164_without_country_code(string phone, string expected)
    {
        Assert.Equal(expected, AccountInputNormalizer.NormalizeBrazilPhone(phone));
    }

    [Theory]
    [InlineData("10992330309")]
    [InlineData("169923303")]
    [InlineData("16123456789")]
    [InlineData("16999999999")]
    public void Phone_normalization_rejects_invalid_values(string phone)
    {
        Assert.Null(AccountInputNormalizer.NormalizeBrazilPhone(phone));
    }

    [Fact]
    public void Account_data_consent_is_required_for_registration()
    {
        Assert.Null(AccountConsentPolicy.ValidateDataConsent(true));
        Assert.Contains("autorização", AccountConsentPolicy.ValidateDataConsent(false));
    }

    [Fact]
    public void Jwt_roundtrip_returns_account_id()
    {
        var accountId = Guid.NewGuid();
        var signingKey = "test-signing-key-with-more-than-32-chars";

        var token = AccountSecurity.CreateJwt(accountId, "teste@example.com", "+5516992330309", signingKey, DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal(accountId, AccountSecurity.ValidateJwt(token, signingKey));
    }

    [Fact]
    public void Jwt_validation_rejects_expired_or_wrongly_signed_token()
    {
        var accountId = Guid.NewGuid();
        var signingKey = "test-signing-key-with-more-than-32-chars";
        var token = AccountSecurity.CreateJwt(accountId, "teste@example.com", "+5516992330309", signingKey, DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.Null(AccountSecurity.ValidateJwt(token, signingKey));
        Assert.Null(AccountSecurity.ValidateJwt(token, "another-signing-key-with-more-than-32-chars"));
    }
}
