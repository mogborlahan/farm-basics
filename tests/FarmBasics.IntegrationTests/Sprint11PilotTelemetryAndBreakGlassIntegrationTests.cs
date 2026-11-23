using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FarmBasics.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace FarmBasics.IntegrationTests;

public sealed class Sprint11PilotTelemetryAndBreakGlassIntegrationTests
    : IClassFixture<PostgisDatabaseFixture>, IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private static readonly string[] ForbiddenPiiKeys =
    [
        "farmerId",
        "identityId",
        "nin",
        "ninHash",
        "phone",
        "fullName",
        "geometry",
        "polygon",
        "coordinates"
    ];

    private readonly PostgisDatabaseFixture _dbFixture;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public Sprint11PilotTelemetryAndBreakGlassIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "sprint11-test-key" };
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

    private static async Task GrantRoleAsync(NpgsqlConnection conn, Guid identityId, string roleCode)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO admin.admin_role_assignment (id, identity_id, admin_role_id, scope_type, granted_by_admin_id)
            SELECT gen_random_uuid(), @identityId, r.id, 'global', @identityId
            FROM admin.admin_role r
            WHERE r.code = @roleCode;
            """;
        cmd.Parameters.AddWithValue("identityId", identityId);
        cmd.Parameters.AddWithValue("roleCode", roleCode);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task WpB0PilotCohortTelemetryAggregatesZeroPiiAndEmitsPrometheusCounters()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid fieldOpsAdminId = Guid.NewGuid();
        Guid farmerId = Guid.NewGuid();
        string adminJwt = CreateSignedToken(fieldOpsAdminId);
        string farmerJwt = CreateSignedToken(farmerId);

        await ProvisionViaAuthMeAsync(client, adminJwt);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantRoleAsync(conn, fieldOpsAdminId, "FieldOperationsAdmin");

        // Farmer denied access to admin pilot telemetry endpoint
        using (HttpRequestMessage deniedReq = new(HttpMethod.Get, "/v1/admin/observability/pilot-cohort"))
        {
            deniedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(deniedReq)).StatusCode);
        }

        // 1. Trigger invalid boundary walk (< 3 distinct points -> 422 INSUFFICIENT_POINTS)
        Guid invalidSessionId;
        using (HttpRequestMessage startInvalid = new(HttpMethod.Post, "/v1/production-units/boundary-sessions"))
        {
            startInvalid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            HttpResponseMessage resp = await client.SendAsync(startInvalid);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            invalidSessionId = doc.RootElement.GetProperty("data").GetProperty("sessionId").GetGuid();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow.AddMinutes(-30);
        using (HttpRequestMessage batchInvalid = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{invalidSessionId}/points/batch"))
        {
            batchInvalid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            batchInvalid.Content = JsonContent.Create(new
            {
                points = new object[]
                {
                    new { clientGuid = Guid.NewGuid(), sequenceNumber = 1, lat = 7.6000, lng = 3.9100, accuracyM = 4.0, recordedAt = now },
                    new { clientGuid = Guid.NewGuid(), sequenceNumber = 2, lat = 7.6005, lng = 3.9105, accuracyM = 4.0, recordedAt = now.AddMinutes(1) }
                }
            });
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(batchInvalid)).StatusCode);
        }

        using (HttpRequestMessage compInvalid = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{invalidSessionId}/complete"))
        {
            compInvalid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.SendAsync(compInvalid)).StatusCode);
        }

        // 2. Complete & confirm valid boundary walk
        Guid validSessionId;
        using (HttpRequestMessage startValid = new(HttpMethod.Post, "/v1/production-units/boundary-sessions"))
        {
            startValid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            HttpResponseMessage resp = await client.SendAsync(startValid);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            validSessionId = doc.RootElement.GetProperty("data").GetProperty("sessionId").GetGuid();
        }

        using (HttpRequestMessage batchValid = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{validSessionId}/points/batch"))
        {
            batchValid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            batchValid.Content = JsonContent.Create(new
            {
                points = new object[]
                {
                    new { clientGuid = Guid.NewGuid(), sequenceNumber = 1, lat = 7.6000, lng = 3.9100, accuracyM = 4.0, recordedAt = now },
                    new { clientGuid = Guid.NewGuid(), sequenceNumber = 2, lat = 7.6000, lng = 3.9110, accuracyM = 4.0, recordedAt = now.AddMinutes(1) },
                    new { clientGuid = Guid.NewGuid(), sequenceNumber = 3, lat = 7.6010, lng = 3.9110, accuracyM = 4.0, recordedAt = now.AddMinutes(2) },
                    new { clientGuid = Guid.NewGuid(), sequenceNumber = 4, lat = 7.6010, lng = 3.9100, accuracyM = 4.0, recordedAt = now.AddMinutes(3) }
                }
            });
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(batchValid)).StatusCode);
        }

        using (HttpRequestMessage compValid = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{validSessionId}/complete"))
        {
            compValid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(compValid)).StatusCode);
        }

        Guid unitId;
        using (HttpRequestMessage confValid = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{validSessionId}/confirm"))
        {
            confValid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            confValid.Content = JsonContent.Create(new
            {
                name = "Sprint 11 Pilot Plot",
                systemType = "open_field",
                growingMediumClass = "soil_based"
            });
            HttpResponseMessage confResp = await client.SendAsync(confValid);
            Assert.Equal(HttpStatusCode.Created, confResp.StatusCode);
            using JsonDocument cDoc = JsonDocument.Parse(await confResp.Content.ReadAsStringAsync());
            unitId = cDoc.RootElement.GetProperty("data").GetProperty("productionUnitId").GetGuid();
        }

        // 3. Start crop cycle, advance to pre_planting, log PRE-01 operation
        Guid maizeCropId;
        await using (NpgsqlCommand cropCmd = conn.CreateCommand())
        {
            cropCmd.CommandText = "SELECT id FROM field.crop_profile WHERE common_name = 'Maize' LIMIT 1;";
            maizeCropId = (Guid)(await cropCmd.ExecuteScalarAsync())!;
        }

        Guid cycleId;
        using (HttpRequestMessage createCyc = new(HttpMethod.Post, "/v1/crop-cycles"))
        {
            createCyc.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            createCyc.Content = JsonContent.Create(new
            {
                productionUnitId = unitId,
                cropProfileId = maizeCropId,
                plantingDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
            HttpResponseMessage resp = await client.SendAsync(createCyc);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            cycleId = doc.RootElement.GetProperty("data").GetProperty("cycleId").GetGuid();
        }

        using (HttpRequestMessage advReq = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/advance-stage"))
        {
            advReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            advReq.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(advReq)).StatusCode);
        }

        using (HttpRequestMessage logReq = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/tasks/PRE-01/log"))
        {
            logReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            logReq.Content = JsonContent.Create(new { data = new { note = "cleared" } });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(logReq)).StatusCode);
        }

        // 4. Exercise offline sync endpoint
        using (HttpRequestMessage syncReq = new(HttpMethod.Post, "/v1/sync/spike-records"))
        {
            syncReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            string body = JsonSerializer.Serialize(new
            {
                clientGuid = Guid.NewGuid(),
                entityType = "operation_log",
                payloadJson = "{\"task\":\"pilot_sync_probe\"}",
                clientRecordedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
            syncReq.Content = new StringContent(body, Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(syncReq)).StatusCode);
        }

        // 5. Verify zero-PII pilot cohort aggregates
        using (HttpRequestMessage cohortReq = new(HttpMethod.Get, "/v1/admin/observability/pilot-cohort"))
        {
            cohortReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage cohortResp = await client.SendAsync(cohortReq);
            Assert.Equal(HttpStatusCode.OK, cohortResp.StatusCode);

            using JsonDocument doc = JsonDocument.Parse(await cohortResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.False(data.GetProperty("containsRawPii").GetBoolean());
            Assert.True(data.GetProperty("boundarySessionsTotal").GetInt32() >= 2);
            Assert.True(data.GetProperty("boundaryConfirmedCount").GetInt32() >= 1);
            Assert.True(data.GetProperty("totalOperationLogs").GetInt32() >= 1);
            Assert.True(data.GetProperty("totalSyncRecords").GetInt32() >= 1);
            Assert.True(data.GetProperty("activeFarmersLoggingLast14Days").GetInt32() >= 1);

            foreach (JsonProperty prop in data.EnumerateObject())
            {
                Assert.DoesNotContain(prop.Name, ForbiddenPiiKeys, StringComparer.OrdinalIgnoreCase);
            }
        }

        // 6. Verify Prometheus pilot counters exported on /metrics
        string metrics = await client.GetStringAsync(new Uri("/metrics", UriKind.Relative));
        Assert.Contains("farmbasics_pilot_boundary_walk_outcomes_total{outcome=\"invalid\"}", metrics, StringComparison.Ordinal);
        Assert.Contains("farmbasics_pilot_boundary_walk_outcomes_total{outcome=\"confirmed\"}", metrics, StringComparison.Ordinal);
        Assert.Contains("farmbasics_pilot_operation_logs_total{status=\"ok\"}", metrics, StringComparison.Ordinal);
        Assert.Contains("farmbasics_pilot_sync_outcomes_total{outcome=\"succeeded\"}", metrics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WpB2BreakGlassWorkflowEnforcesSecondSuperAdminReviewAndDbConstraint()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid adminAId = Guid.NewGuid();
        Guid adminBId = Guid.NewGuid();
        Guid farmerId = Guid.NewGuid();
        string adminAJwt = CreateSignedToken(adminAId);
        string adminBJwt = CreateSignedToken(adminBId);
        string farmerJwt = CreateSignedToken(farmerId);

        await ProvisionViaAuthMeAsync(client, adminAJwt);
        await ProvisionViaAuthMeAsync(client, adminBJwt);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantRoleAsync(conn, adminAId, "SuperAdmin");
        await GrantRoleAsync(conn, adminBId, "SuperAdmin");

        // Farmer denied access to break-glass invoke & list
        using (HttpRequestMessage deniedInvoke = new(HttpMethod.Post, "/v1/admin/break-glass"))
        {
            deniedInvoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            deniedInvoke.Content = JsonContent.Create(new
            {
                actionDescription = "Restart Keycloak container during outage",
                justification = "P1 incident INC-2026-1123 with Keycloak unhealthy"
            });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(deniedInvoke)).StatusCode);
        }

        // Short description rejected 400 VALIDATION_ERROR
        using (HttpRequestMessage shortReq = new(HttpMethod.Post, "/v1/admin/break-glass"))
        {
            shortReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAJwt);
            shortReq.Content = JsonContent.Create(new
            {
                actionDescription = "short",
                justification = "short"
            });
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(shortReq)).StatusCode);
        }

        // AdminA records retroactive break-glass action
        Guid actionId;
        DateTimeOffset invokedAt;
        DateTimeOffset reviewDueAt;
        using (HttpRequestMessage invokeReq = new(HttpMethod.Post, "/v1/admin/break-glass"))
        {
            invokeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAJwt);
            invokeReq.Content = JsonContent.Create(new
            {
                actionDescription = "Type 1 SSH restart of Keycloak during P1 outage",
                justification = "Doc 04A §11.4 retroactive break-glass log for INC-1102",
                retroactive = true
            });
            HttpResponseMessage invokeResp = await client.SendAsync(invokeReq);
            Assert.Equal(HttpStatusCode.Created, invokeResp.StatusCode);

            using JsonDocument doc = JsonDocument.Parse(await invokeResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            actionId = data.GetProperty("breakGlassActionId").GetGuid();
            Assert.Equal("pending_review", data.GetProperty("status").GetString());
            Assert.True(data.GetProperty("retroactive").GetBoolean());
            invokedAt = data.GetProperty("invokedAt").GetDateTimeOffset();
            reviewDueAt = data.GetProperty("reviewDueAt").GetDateTimeOffset();
            Assert.True(reviewDueAt - invokedAt >= TimeSpan.FromHours(47.9));
        }

        // AdminA cannot self-review -> 403 SELF_APPROVAL_BLOCKED
        using (HttpRequestMessage selfReview = new(HttpMethod.Post, $"/v1/admin/break-glass/{actionId}/review"))
        {
            selfReview.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAJwt);
            selfReview.Content = JsonContent.Create(new { notes = "Attempting self approval" });
            HttpResponseMessage selfResp = await client.SendAsync(selfReview);
            Assert.Equal(HttpStatusCode.Forbidden, selfResp.StatusCode);
            using JsonDocument errDoc = JsonDocument.Parse(await selfResp.Content.ReadAsStringAsync());
            Assert.Equal("SELF_APPROVAL_BLOCKED", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // PostgreSQL CHECK constraint blocks self-review even on direct SQL update (SQLSTATE 23514)
        await using (NpgsqlCommand sqlSelfCmd = conn.CreateCommand())
        {
            sqlSelfCmd.CommandText = """
                UPDATE admin.break_glass_action
                SET status = 'reviewed', reviewed_by_admin_id = @adminId, reviewed_at = now()
                WHERE id = @id;
                """;
            sqlSelfCmd.Parameters.AddWithValue("adminId", adminAId);
            sqlSelfCmd.Parameters.AddWithValue("id", actionId);
            PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => sqlSelfCmd.ExecuteNonQueryAsync());
            Assert.Equal("23514", ex.SqlState);
        }

        // AdminB lists pending reviews
        using (HttpRequestMessage listReq = new(HttpMethod.Get, "/v1/admin/break-glass"))
        {
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminBJwt);
            HttpResponseMessage listResp = await client.SendAsync(listReq);
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            using JsonDocument listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
            JsonElement item = Assert.Single(
                listDoc.RootElement.GetProperty("data").EnumerateArray(),
                e => e.GetProperty("breakGlassActionId").GetGuid() == actionId);
            Assert.Equal("pending_review", item.GetProperty("status").GetString());
        }

        // AdminB approves/reviews the break-glass action -> 200 OK
        using (HttpRequestMessage reviewReq = new(HttpMethod.Post, $"/v1/admin/break-glass/{actionId}/review"))
        {
            reviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminBJwt);
            reviewReq.Content = JsonContent.Create(new { notes = "Verified SSH session log & confirmed key rotation" });
            HttpResponseMessage reviewResp = await client.SendAsync(reviewReq);
            Assert.Equal(HttpStatusCode.OK, reviewResp.StatusCode);
            using JsonDocument revDoc = JsonDocument.Parse(await reviewResp.Content.ReadAsStringAsync());
            JsonElement data = revDoc.RootElement.GetProperty("data");
            Assert.Equal("reviewed", data.GetProperty("status").GetString());
            Assert.Equal(adminAId, data.GetProperty("invokedByAdminId").GetGuid());
            Assert.Equal(adminBId, data.GetProperty("reviewedByAdminId").GetGuid());
        }

        // Re-review returns 409 CONFLICT
        using (HttpRequestMessage replayReq = new(HttpMethod.Post, $"/v1/admin/break-glass/{actionId}/review"))
        {
            replayReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminBJwt);
            replayReq.Content = JsonContent.Create(new { notes = "Duplicate review" });
            Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(replayReq)).StatusCode);
        }

        // Both immutable audit records present in audit.audit_log_entry
        await using NpgsqlCommand auditCmd = conn.CreateCommand();
        auditCmd.CommandText = """
            SELECT COUNT(*)::int
            FROM audit.audit_log_entry
            WHERE entity_type = 'break_glass_action'
              AND entity_id = @id
              AND event_type IN ('admin.break_glass_invoked', 'admin.break_glass_reviewed');
            """;
        auditCmd.Parameters.AddWithValue("id", actionId);
        int auditCount = (int)(await auditCmd.ExecuteScalarAsync())!;
        Assert.Equal(2, auditCount);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
