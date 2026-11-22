using FarmBasics.BuildingBlocks.Logging;
using Xunit;

namespace FarmBasics.UnitTests.Logging;

public sealed class SensitiveDataRedactionTests
{
    private sealed record SampleRegistrationDto(
        Guid IdentityId,
        string LgaCode,
        string Nin,
        string Otp,
        string Pin,
        string Password,
        string Bvn,
        string AccountNumber,
        NestedAuthPayload Auth);

    private sealed record NestedAuthPayload(string AccessToken, string ClientSecret, string SafeTag);

    [Fact]
    public void DestructuredObjectsAndNestedSecretsAreMaskedWhileSafeFieldsRemain()
    {
        const string rawNin = "98765432109";
        const string rawOtp = "482910";
        const string rawPin = "7391";
        const string rawPassword = "SuperSecretFarmerPassword!";
        const string rawBvn = "22233344455";
        const string rawAccount = "0123456789";
        const string rawAccessToken = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjMifQ.sigvalue123";
        const string rawSecret = "client-secret-value-xyz";
        Guid identityId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        InMemoryLogSink sink = new();
        using Serilog.Core.Logger logger = InMemoryLogSink.CreateTestLogger(sink);

        SampleRegistrationDto payload = new(
            identityId,
            "KN-KMC",
            rawNin,
            rawOtp,
            rawPin,
            rawPassword,
            rawBvn,
            rawAccount,
            new NestedAuthPayload(rawAccessToken, rawSecret, "agent-tablet-01"));

        logger.Information("Processing registration {@Payload}", payload);

        string output = sink.RenderAllText();
        Assert.Contains(identityId.ToString(), output, StringComparison.Ordinal);
        Assert.Contains("KN-KMC", output, StringComparison.Ordinal);
        Assert.Contains("agent-tablet-01", output, StringComparison.Ordinal);
        Assert.Contains(SensitiveFieldDenylist.RedactedMarker, output, StringComparison.Ordinal);

        Assert.DoesNotContain(rawNin, output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawOtp, output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawPin, output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawPassword, output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawBvn, output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawAccount, output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawAccessToken, output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawSecret, output, StringComparison.Ordinal);
    }

    [Fact]
    public void ScalarPropertiesAndInlineKeyValueOrBearerTokensAreMasked()
    {
        const string rawNin = "55511122233";
        const string rawOtp = "901255";
        const string rawPin = "6420";
        const string rawPassword = "AnotherPassword99!";
        const string bearerJwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ1c2VyLTEifQ.signature-bytes";

        InMemoryLogSink sink = new();
        using Serilog.Core.Logger logger = InMemoryLogSink.CreateTestLogger(sink);

        logger.Information(
            "Scalar fields Nin={Nin}, Otp={Otp}, Pin={Pin}, Password={Password}, Header={Header}, Text={Text}",
            rawNin,
            rawOtp,
            rawPin,
            rawPassword,
            $"Bearer {bearerJwt}",
            $"inline nin={rawNin} pin={rawPin} ok=true");

        string output = sink.RenderAllText();
        Assert.Contains("ok=true", output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawNin, output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawOtp, output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawPin, output, StringComparison.Ordinal);
        Assert.DoesNotContain(rawPassword, output, StringComparison.Ordinal);
        Assert.DoesNotContain(bearerJwt, output, StringComparison.Ordinal);
    }

    [Fact]
    public void DictionarySensitiveKeysAreMasked()
    {
        const string hmacSecret = "hmac-super-secret-2026";
        InMemoryLogSink sink = new();
        using Serilog.Core.Logger logger = InMemoryLogSink.CreateTestLogger(sink);

        Dictionary<string, string> metadata = new()
        {
            ["webhook_secret"] = hmacSecret,
            ["raw_nin"] = "11223344556",
            ["lgaName"] = "Kano Municipal"
        };

        logger.Information("Metadata {@Metadata}", metadata);

        string output = sink.RenderAllText();
        Assert.Contains("Kano Municipal", output, StringComparison.Ordinal);
        Assert.DoesNotContain(hmacSecret, output, StringComparison.Ordinal);
        Assert.DoesNotContain("11223344556", output, StringComparison.Ordinal);
    }
}
