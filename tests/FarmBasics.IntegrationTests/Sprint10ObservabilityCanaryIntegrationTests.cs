using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FarmBasics.Api.Observability;
using FarmBasics.BuildingBlocks.Observability;
using FarmBasics.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace FarmBasics.IntegrationTests;

public sealed class Sprint10ObservabilityCanaryIntegrationTests
    : IClassFixture<PostgisDatabaseFixture>, IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private readonly PostgisDatabaseFixture _dbFixture;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public Sprint10ObservabilityCanaryIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "sprint10-test-key" };
    }

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

            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.RequireHttpsMetadata = false;
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = TestIssuer,
                        ValidateAudience = true,
                        ValidAudience = "farm-basics-api",
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = _signingKey,
                        ValidateLifetime = true,
                        NameClaimType = "sub",
                        ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
                    };
                });
            });
        });
    }

    private string CreateSignedToken(Guid subjectGuid)
    {
        SigningCredentials credentials = new(_signingKey, SecurityAlgorithms.RsaSha256);
        DateTime now = DateTime.UtcNow;

        SecurityTokenDescriptor descriptor = new()
        {
            Subject = new ClaimsIdentity([new Claim("sub", subjectGuid.ToString())]),
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddMinutes(30),
            IssuedAt = now,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subjectGuid.ToString(),
                ["iss"] = TestIssuer,
                ["aud"] = "farm-basics-api"
            }
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static async Task ProvisionViaAuthMeAsync(HttpClient client, string jwt)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, "/v1/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task WpA0CriticalInvariantCanariesReportCleanZeroThenSurfaceSyntheticDriftAndDenyNonAdmin()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid adminId = Guid.NewGuid();
        Guid farmerId = Guid.NewGuid();
        string adminJwt = CreateSignedToken(adminId);
        string farmerJwt = CreateSignedToken(farmerId);

        await ProvisionViaAuthMeAsync(client, adminJwt);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        // Non-admin caller denied 403 PERMISSION_DENIED on /v1/admin/observability/canaries
        using (HttpRequestMessage forbiddenReq = new(HttpMethod.Get, "/v1/admin/observability/canaries"))
        {
            forbiddenReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            HttpResponseMessage forbiddenResp = await client.SendAsync(forbiddenReq);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);
        }

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await using (NpgsqlCommand grantCmd = conn.CreateCommand())
        {
            grantCmd.CommandText = """
                INSERT INTO admin.admin_role_assignment (id, identity_id, admin_role_id, scope_type, granted_by_admin_id)
                SELECT gen_random_uuid(), @adminId, r.id, 'global', @adminId
                FROM admin.admin_role r
                WHERE r.code = 'AuditComplianceAdmin';
                """;
            grantCmd.Parameters.AddWithValue("adminId", adminId);
            await grantCmd.ExecuteNonQueryAsync();
        }

        // Clean slate before inserting synthetic alert
        await using (NpgsqlCommand clearCmd = conn.CreateCommand())
        {
            clearCmd.CommandText = "UPDATE accounts.reconciliation_alert SET resolved = TRUE WHERE resolved = FALSE;";
            await clearCmd.ExecuteNonQueryAsync();
        }

        using (HttpRequestMessage cleanReq = new(HttpMethod.Get, "/v1/admin/observability/canaries"))
        {
            cleanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage cleanResp = await client.SendAsync(cleanReq);
            Assert.Equal(HttpStatusCode.OK, cleanResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await cleanResp.Content.ReadAsStringAsync());
            JsonElement data = d.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("canaryRunSucceeded").GetBoolean());
            Assert.Equal(0, data.GetProperty("unbalancedJournalEntries").GetInt32());
            Assert.Equal(0, data.GetProperty("missingMonthlyPartitions").GetInt32());
            Assert.Equal(0, data.GetProperty("selfApprovedMasterDataRequests").GetInt32());
            Assert.Equal(0, data.GetProperty("unresolvedReconciliationAlerts").GetInt32());
            Assert.True(data.GetProperty("allInvariantsHealthy").GetBoolean());
        }

        string metricsClean = await client.GetStringAsync(new Uri("/metrics", UriKind.Relative));
        Assert.Contains("farmbasics_invariant_unbalanced_journal_entries 0", metricsClean, StringComparison.Ordinal);
        Assert.Contains("farmbasics_invariant_missing_monthly_partitions 0", metricsClean, StringComparison.Ordinal);
        Assert.Contains("farmbasics_invariant_self_approved_master_data 0", metricsClean, StringComparison.Ordinal);
        Assert.Contains("farmbasics_invariant_reconciliation_open_alerts 0", metricsClean, StringComparison.Ordinal);
        Assert.Contains("farmbasics_invariant_canary_last_run_success 1", metricsClean, StringComparison.Ordinal);

        // Insert a synthetic unresolved reconciliation alert and confirm canary detects it immediately
        Guid periodId = Guid.NewGuid();
        Guid alertId = Guid.NewGuid();
        await using (NpgsqlCommand seedAlert = conn.CreateCommand())
        {
            seedAlert.CommandText = """
                INSERT INTO accounts.accounting_period (id, farm_identity_id, start_date, end_date, status)
                VALUES (@periodId, @farmerId, @startDate, @endDate, 'open');

                INSERT INTO accounts.reconciliation_alert (
                    id, farm_identity_id, account_id, period_id,
                    cached_closing_balance, computed_closing_balance, drift_amount, severity, resolved
                )
                SELECT @alertId, @farmerId, a.id, @periodId, 500.00, 350.00, 150.00, 'CRITICAL', FALSE
                FROM accounts.account a
                LIMIT 1;
                """;
            seedAlert.Parameters.AddWithValue("periodId", periodId);
            seedAlert.Parameters.AddWithValue("farmerId", farmerId);
            seedAlert.Parameters.AddWithValue("startDate", new DateOnly(2026, 1, 1));
            seedAlert.Parameters.AddWithValue("endDate", new DateOnly(2026, 1, 31));
            seedAlert.Parameters.AddWithValue("alertId", alertId);
            await seedAlert.ExecuteNonQueryAsync();
        }

        try
        {
            using (HttpRequestMessage dirtyReq = new(HttpMethod.Get, "/v1/admin/observability/canaries"))
            {
                dirtyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
                HttpResponseMessage dirtyResp = await client.SendAsync(dirtyReq);
                Assert.Equal(HttpStatusCode.OK, dirtyResp.StatusCode);
                using JsonDocument d = JsonDocument.Parse(await dirtyResp.Content.ReadAsStringAsync());
                JsonElement data = d.RootElement.GetProperty("data");
                Assert.True(data.GetProperty("unresolvedReconciliationAlerts").GetInt32() >= 1);
                Assert.False(data.GetProperty("allInvariantsHealthy").GetBoolean());
            }

            string metricsBreached = await client.GetStringAsync(new Uri("/metrics", UriKind.Relative));
            Assert.Contains("farmbasics_invariant_reconciliation_open_alerts 1", metricsBreached, StringComparison.Ordinal);
        }
        finally
        {
            await using NpgsqlCommand cleanup = conn.CreateCommand();
            cleanup.CommandText = """
                DELETE FROM accounts.reconciliation_alert WHERE id = @alertId;
                DELETE FROM accounts.accounting_period WHERE id = @periodId;
                """;
            cleanup.Parameters.AddWithValue("alertId", alertId);
            cleanup.Parameters.AddWithValue("periodId", periodId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task WpA0InvariantCanaryResetsGaugeToZeroFailClosedWhenSweepFails()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        await ProvisionViaAuthMeAsync(client, CreateSignedToken(Guid.NewGuid()));

        await using NpgsqlDataSource validSource = NpgsqlDataSource.Create(_dbFixture.ConnectionString);
        InvariantCanaryReport healthy = await InvariantCanaryService.RunSweepAsync(
            validSource,
            NullLogger.Instance,
            CancellationToken.None);
        Assert.True(healthy.CanaryRunSucceeded);
        Assert.Equal(1d, PlatformMetrics.InvariantCanaryLastRunSuccess.Value);

        await using NpgsqlDataSource unreachableSource = NpgsqlDataSource.Create(
            "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=1;Command Timeout=1");
        await Assert.ThrowsAnyAsync<Exception>(() =>
            InvariantCanaryService.RunSweepAsync(
                unreachableSource,
                NullLogger.Instance,
                CancellationToken.None));
        Assert.Equal(0d, PlatformMetrics.InvariantCanaryLastRunSuccess.Value);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
