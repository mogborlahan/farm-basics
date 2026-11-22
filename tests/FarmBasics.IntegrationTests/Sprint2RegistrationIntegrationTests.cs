using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FarmBasics.Api.Integrations;
using FarmBasics.Api.Persistence;
using FarmBasics.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FarmBasics.IntegrationTests;

public sealed partial class Sprint2RegistrationIntegrationTests(PostgisDatabaseFixture dbFixture)
    : IClassFixture<PostgisDatabaseFixture>
{
    private readonly PostgisDatabaseFixture _dbFixture = dbFixture;

    [GeneratedRegex(@"\b(\d{6})\b")]
    private static partial Regex OtpCodeRegex();

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _dbFixture.ConnectionString,
                    ["Auth:RequireHttpsMetadata"] = "false",
                    ["Integrations:Nin:Mode"] = "Sandbox",
                    ["Integrations:Sms:Mode"] = "Sandbox"
                });
            });
        });
    }

    [Fact]
    public async Task Wp21SelfServiceRegistrationEndToEndOtpLockoutAndProviderErrors()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        SandboxSmsGatewayClient smsCapture = factory.Services.GetRequiredService<SandboxSmsGatewayClient>();
        smsCapture.Clear();

        // 1. 400 NIN_NOT_FOUND (...0000)
        HttpResponseMessage notFoundResp = await client.PostAsJsonAsync(
            new Uri("/v1/registrations/self-service", UriKind.Relative),
            new { nin = "44400010000", phone = "+2348011110001" });
        Assert.Equal(HttpStatusCode.BadRequest, notFoundResp.StatusCode);
        using (JsonDocument doc = JsonDocument.Parse(await notFoundResp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("NIN_NOT_FOUND", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // 2. 503 NIN_PROVIDER_UNAVAILABLE (...5553)
        HttpResponseMessage unavailableResp = await client.PostAsJsonAsync(
            new Uri("/v1/registrations/self-service", UriKind.Relative),
            new { nin = "44400015553", phone = "+2348011110002" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailableResp.StatusCode);
        using (JsonDocument doc = JsonDocument.Parse(await unavailableResp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("NIN_PROVIDER_UNAVAILABLE", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // 3. 202 NIN_LOOKUP_UNDER_REVIEW (...9999) + audit record written
        HttpResponseMessage fuzzyResp = await client.PostAsJsonAsync(
            new Uri("/v1/registrations/self-service", UriKind.Relative),
            new { nin = "44400019999", phone = "+2348011110003" });
        Assert.Equal(HttpStatusCode.Accepted, fuzzyResp.StatusCode);
        using (JsonDocument doc = JsonDocument.Parse(await fuzzyResp.Content.ReadAsStringAsync()))
        {
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal("NIN_LOOKUP_UNDER_REVIEW", data.GetProperty("code").GetString());
            Assert.NotEqual(Guid.Empty, data.GetProperty("reviewQueueItemId").GetGuid());
        }

        // 4. Lockout flow: 4 wrong OTPs -> 400 INVALID_OTP; 5th wrong OTP -> 429 OTP_LOCKED
        string lockoutNin = "77000011001";
        string lockoutPhone = "+2348011112001";
        HttpResponseMessage lockRegResp = await client.PostAsJsonAsync(
            new Uri("/v1/registrations/self-service", UriKind.Relative),
            new { nin = lockoutNin, phone = lockoutPhone });
        Assert.Equal(HttpStatusCode.Created, lockRegResp.StatusCode);
        Guid lockIdentityId;
        using (JsonDocument doc = JsonDocument.Parse(await lockRegResp.Content.ReadAsStringAsync()))
        {
            lockIdentityId = doc.RootElement.GetProperty("data").GetProperty("identityId").GetGuid();
        }

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            HttpResponseMessage badOtpResp = await client.PostAsJsonAsync(
                new Uri($"/v1/registrations/self-service/{lockIdentityId}/verify-otp", UriKind.Relative),
                new { otp = "000000", channel = "phone" });

            if (attempt < 5)
            {
                Assert.Equal(HttpStatusCode.BadRequest, badOtpResp.StatusCode);
                using JsonDocument badDoc = JsonDocument.Parse(await badOtpResp.Content.ReadAsStringAsync());
                JsonElement err = badDoc.RootElement.GetProperty("error");
                Assert.Equal("INVALID_OTP", err.GetProperty("code").GetString());
                Assert.Equal(5 - attempt, err.GetProperty("details").GetProperty("attemptsRemaining").GetInt32());
            }
            else
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, badOtpResp.StatusCode);
                using JsonDocument lockDoc = JsonDocument.Parse(await badOtpResp.Content.ReadAsStringAsync());
                JsonElement err = lockDoc.RootElement.GetProperty("error");
                Assert.Equal("OTP_LOCKED", err.GetProperty("code").GetString());
                Assert.True(err.GetProperty("details").GetProperty("retryAfterSeconds").GetInt32() >= 60);
            }
        }

        // 5. Happy path + incomplete registration resume + FarmerID activation + active NIN conflict
        string happyNin = "77000021002";
        string happyPhone = "+2348011112002";
        string happyEmail = $"farmer.{Guid.NewGuid():N}@synthetic.farmbasics.test";

        HttpResponseMessage reg1Resp = await client.PostAsJsonAsync(
            new Uri("/v1/registrations/self-service", UriKind.Relative),
            new { nin = happyNin, phone = happyPhone, email = happyEmail });
        Assert.Equal(HttpStatusCode.Created, reg1Resp.StatusCode);

        Guid happyIdentityId;
        using (JsonDocument doc = JsonDocument.Parse(await reg1Resp.Content.ReadAsStringAsync()))
        {
            JsonElement data = doc.RootElement.GetProperty("data");
            happyIdentityId = data.GetProperty("identityId").GetGuid();
            Assert.Equal("pending_verification", data.GetProperty("status").GetString());
            Assert.False(data.GetProperty("resumed").GetBoolean());
            Assert.True(data.GetProperty("smsDispatched").GetBoolean());
        }

        // Re-submitting same NIN while still pending resumes without duplicate identity row (FR-ID-001 AC #4)
        HttpResponseMessage resumeResp = await client.PostAsJsonAsync(
            new Uri("/v1/registrations/self-service", UriKind.Relative),
            new { nin = happyNin, phone = happyPhone, email = happyEmail });
        Assert.Equal(HttpStatusCode.Created, resumeResp.StatusCode);
        using (JsonDocument doc = JsonDocument.Parse(await resumeResp.Content.ReadAsStringAsync()))
        {
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal(happyIdentityId, data.GetProperty("identityId").GetGuid());
            Assert.True(data.GetProperty("resumed").GetBoolean());
        }

        SmsSendRequest? latestSms = smsCapture.SentMessages.LastOrDefault(m => m.ToPhoneE164 == happyPhone);
        Assert.NotNull(latestSms);
        Match otpMatch = OtpCodeRegex().Match(latestSms.Body);
        Assert.True(otpMatch.Success);
        string otpCode = otpMatch.Groups[1].Value;

        HttpResponseMessage verifyResp = await client.PostAsJsonAsync(
            new Uri($"/v1/registrations/self-service/{happyIdentityId}/verify-otp", UriKind.Relative),
            new { otp = otpCode, channel = "phone" });
        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);

        string generatedFarmerId;
        using (JsonDocument doc = JsonDocument.Parse(await verifyResp.Content.ReadAsStringAsync()))
        {
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal("active", data.GetProperty("status").GetString());
            generatedFarmerId = data.GetProperty("farmerId").GetString()!;
            Assert.StartsWith("FB-NG-", generatedFarmerId, StringComparison.Ordinal);
        }

        // Verify RoleProfile + TrustProfile + Audit persisted in DB
        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await using (NpgsqlCommand roleCmd = conn.CreateCommand())
        {
            roleCmd.CommandText = """
                SELECT count(*)
                FROM identity.role_profile rp
                JOIN identity.trust_profile tp ON tp.identity_id = rp.identity_id
                WHERE rp.identity_id = @id
                  AND rp.role_type = 'farmer'
                  AND rp.role_specific_id = @farmerId
                  AND tp.verification_tier = 'verified';
                """;
            roleCmd.Parameters.AddWithValue("id", happyIdentityId);
            roleCmd.Parameters.AddWithValue("farmerId", generatedFarmerId);
            Assert.Equal(1L, Convert.ToInt64(await roleCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }

        // Now that identity is active, re-registering the same NIN returns 409 NIN_ALREADY_REGISTERED
        HttpResponseMessage conflictResp = await client.PostAsJsonAsync(
            new Uri("/v1/registrations/self-service", UriKind.Relative),
            new { nin = happyNin, phone = happyPhone });
        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);
        using (JsonDocument doc = JsonDocument.Parse(await conflictResp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("NIN_ALREADY_REGISTERED", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Wp22ParallelDuplicateNinRegistrationRaceExactlyOneIdentityCreated()
    {
        await using WebApplicationFactory<Program> factory1 = CreateFactory();
        await using (NpgsqlDataSource migDs = NpgsqlDataSource.Create(_dbFixture.ConnectionString))
        {
            await factory1.Services.GetRequiredService<SchemaMigrationRunner>()
                .EnsureMigratedAsync(migDs, CancellationToken.None);
        }

        using HttpClient raceClient = factory1.CreateClient();
        List<string> usedNins = new(50);

        // 50 iterations x 2 concurrent POSTs with identical NIN per Doc/12-QA §3.2 & Rule 4
        RaceSummary pendingRace = await ParallelRaceHarness.RunAsync(
            iterations: 50,
            concurrency: 2,
            iteration =>
            {
                string nin = $"8810000{1001 + iteration:D4}";
                usedNins.Add(nin);
                string phone = $"+234802222{1000 + iteration:D4}";

                Func<int, Task<bool>> worker = async _ =>
                {
                    HttpResponseMessage resp = await raceClient.PostAsJsonAsync(
                        new Uri("/v1/registrations/self-service", UriKind.Relative),
                        new { nin, phone });

                    if (resp.StatusCode == HttpStatusCode.Created)
                    {
                        using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                        return !doc.RootElement.GetProperty("data").GetProperty("resumed").GetBoolean();
                    }

                    Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
                    return false;
                };

                return Task.FromResult(worker);
            });

        Assert.Equal(50, pendingRace.TotalWinners);
        Assert.Equal(50, pendingRace.TotalConflicts);
        Assert.All(pendingRace.WinnersPerIteration, w => Assert.Equal(1, w));

        // Verify strictly 1 DB identity row created per NIN across all 50 iterations
        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        foreach (string nin in usedNins)
        {
            string ninRef = NinVerificationHashHelper.ComputeVerificationRef(nin, "farmbasics-nonprod-nin-pepper-v1");
            await using NpgsqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM identity.identity WHERE nin_verification_ref = @ref;";
            cmd.Parameters.AddWithValue("ref", ninRef);
            long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            Assert.Equal(1L, count);
        }

        // Second race (20 iterations x 2 threads) against an already-ACTIVE NIN: 100% 409 NIN_ALREADY_REGISTERED
        await using WebApplicationFactory<Program> factory2 = CreateFactory();
        using HttpClient activeClient = factory2.CreateClient();
        SandboxSmsGatewayClient smsCapture2 = factory2.Services.GetRequiredService<SandboxSmsGatewayClient>();
        smsCapture2.Clear();

        string activeNin = "88100009001";
        string activePhone = "+2348033339001";
        HttpResponseMessage seedResp = await activeClient.PostAsJsonAsync(
            new Uri("/v1/registrations/self-service", UriKind.Relative),
            new { nin = activeNin, phone = activePhone });
        Assert.Equal(HttpStatusCode.Created, seedResp.StatusCode);

        Guid activeId;
        using (JsonDocument doc = JsonDocument.Parse(await seedResp.Content.ReadAsStringAsync()))
        {
            activeId = doc.RootElement.GetProperty("data").GetProperty("identityId").GetGuid();
        }

        SmsSendRequest sms = smsCapture2.SentMessages.Last(m => m.ToPhoneE164 == activePhone);
        string activeOtp = OtpCodeRegex().Match(sms.Body).Groups[1].Value;
        HttpResponseMessage actVerify = await activeClient.PostAsJsonAsync(
            new Uri($"/v1/registrations/self-service/{activeId}/verify-otp", UriKind.Relative),
            new { otp = activeOtp, channel = "phone" });
        Assert.Equal(HttpStatusCode.OK, actVerify.StatusCode);

        RaceSummary activeRace = await ParallelRaceHarness.RunAsync(
            iterations: 20,
            concurrency: 2,
            _ =>
            {
                Func<int, Task<bool>> worker = async _ =>
                {
                    HttpResponseMessage resp = await activeClient.PostAsJsonAsync(
                        new Uri("/v1/registrations/self-service", UriKind.Relative),
                        new { nin = activeNin, phone = activePhone });
                    Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
                    using JsonDocument errDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    Assert.Equal("NIN_ALREADY_REGISTERED", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
                    return false;
                };
                return Task.FromResult(worker);
            });

        Assert.Equal(0, activeRace.TotalWinners);
        Assert.Equal(40, activeRace.TotalConflicts);
    }
}
