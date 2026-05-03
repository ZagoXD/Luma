using Luma.Api.Services;

namespace Luma.Tests;

public sealed class PrivacyServicesTests
{
    [Fact]
    public void Field_encryption_roundtrip_does_not_store_plaintext()
    {
        var options = TestOptions();
        PrivacyRuntime.Configure(options);
        try
        {
            var encrypted = PrivacyRuntime.Protect("Julia", "account.full_name");
            var decrypted = PrivacyRuntime.Unprotect(encrypted, "account.full_name");

            Assert.NotEqual("Julia", encrypted);
            Assert.Contains("A256GCM", encrypted);
            Assert.Equal("Julia", decrypted);
        }
        finally
        {
            PrivacyRuntime.Reset();
        }
    }

    [Fact]
    public void Field_encryption_rejects_wrong_purpose()
    {
        PrivacyRuntime.Configure(TestOptions());
        try
        {
            var encrypted = PrivacyRuntime.Protect("45815168890", "account.cpf");

            Assert.Throws<InvalidOperationException>(() => PrivacyRuntime.Unprotect(encrypted, "account.email"));
        }
        finally
        {
            PrivacyRuntime.Reset();
        }
    }

    [Theory]
    [InlineData("Teste@Example.com", "teste@example.com", "account.email")]
    [InlineData("+55 16 99233-0309", "+5516992330309", "account.phone")]
    public void Lookup_hash_is_stable_for_normalized_values(string first, string second, string purpose)
    {
        PrivacyRuntime.Configure(TestOptions());
        try
        {
            var firstHash = PrivacyRuntime.LookupHash(first, purpose);
            var secondHash = PrivacyRuntime.LookupHash(second, purpose);

            Assert.Equal(firstHash, secondHash);
            Assert.DoesNotContain(first, firstHash);
        }
        finally
        {
            PrivacyRuntime.Reset();
        }
    }

    private static PrivacyOptions TestOptions()
    {
        return new PrivacyOptions
        {
            EncryptionEnabled = true,
            EncryptionKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
            LookupPepper = Convert.ToBase64String(Enumerable.Range(33, 32).Select(i => (byte)i).ToArray()),
            ActiveKeyId = "test-key"
        };
    }
}
