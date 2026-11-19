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
    public void Password_hash_is_salted_and_verifies_only_the_correct_password()
    {
        var first = AccountSecurity.HashPassword("correct-password");
        Assert.NotEqual(first, AccountSecurity.HashPassword("correct-password"));
        Assert.True(AccountSecurity.VerifyPassword("correct-password", first));
        Assert.False(AccountSecurity.VerifyPassword("incorrect-password", first));
    }

    [Fact]
    public void Invalid_password_storage_fails_closed_without_unbounded_derivation()
    {
        Assert.False(AccountSecurity.VerifyPassword("password", "pbkdf2-sha256.100000.invalid.invalid"));
        Assert.False(AccountSecurity.VerifyPassword("password", "pbkdf2-sha256.2147483647.AA==.AA=="));
        Assert.False(AccountSecurity.VerifyPassword("password", "pbkdf2-sha256.100000.."));
    }
}
