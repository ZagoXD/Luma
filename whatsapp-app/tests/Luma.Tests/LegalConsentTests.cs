using Luma.Api.Services;

namespace Luma.Tests;

public sealed class LegalConsentTests
{
    [Fact]
    public void MissingOrStaleAcceptanceIsRejected()
    {
        Assert.NotNull(LegalConsent.Validate(false, LegalConsent.Version));
        Assert.NotNull(LegalConsent.Validate(true, "old"));
        Assert.Null(LegalConsent.Validate(true, LegalConsent.Version));
    }
}
