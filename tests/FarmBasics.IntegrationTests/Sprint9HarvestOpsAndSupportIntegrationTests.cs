using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FarmBasics.Api.Integrations;
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

public sealed partial class Sprint9HarvestOpsAndSupportIntegrationTests
    : IClassFixture<PostgisDatabaseFixture>, IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private readonly PostgisDatabaseFixture _dbFixture;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public Sprint9HarvestOpsAndSupportIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "sprint9-test-key" };
    }

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
                    ["Integrations:Sms:Mode"] = "Sandbox",
                    ["Integrations:Voice:Mode"] = "Sandbox"
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

    private static async Task GrantRoleAsync(
        NpgsqlConnection conn,
        Guid targetIdentityId,
        string roleCode,
        string scopeType,
        Guid? scopeRefId,
        Guid grantedByAdminId)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO admin.admin_role_assignment (id, identity_id, admin_role_id, scope_type, scope_ref_id, granted_by_admin_id)
            SELECT gen_random_uuid(), @identityId, r.id, @scopeType::scope_type, @scopeRefId, @grantedBy
            FROM admin.admin_role r
            WHERE r.code = @roleCode;
            """;
        cmd.Parameters.AddWithValue("identityId", targetIdentityId);
        cmd.Parameters.AddWithValue("roleCode", roleCode);
        cmd.Parameters.AddWithValue("scopeType", scopeType);
        cmd.Parameters.AddWithValue("scopeRefId", (object?)scopeRefId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("grantedBy", grantedByAdminId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> RegisterVerifiedFarmerAsync(
        HttpClient client,
        SandboxSmsGatewayClient smsCapture,
        string nin,
        string phone,
        Guid? stateId = null,
        Guid? lgaId = null)
    {
        HttpResponseMessage regResp = await client.PostAsJsonAsync(
            new Uri("/v1/registrations/self-service", UriKind.Relative),
            new { nin, phone, stateId, lgaId });
        Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);

        Guid identityId;
        using (JsonDocument doc = JsonDocument.Parse(await regResp.Content.ReadAsStringAsync()))
        {
            identityId = doc.RootElement.GetProperty("data").GetProperty("identityId").GetGuid();
        }

        SmsSendRequest? sms = smsCapture.SentMessages.LastOrDefault(m => m.ToPhoneE164 == phone);
        Assert.NotNull(sms);
        Match otpMatch = OtpCodeRegex().Match(sms.Body);
        Assert.True(otpMatch.Success);

        HttpResponseMessage verifyResp = await client.PostAsJsonAsync(
            new Uri($"/v1/registrations/self-service/{identityId}/verify-otp", UriKind.Relative),
            new { otp = otpMatch.Groups[1].Value, channel = "phone" });
        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
        return identityId;
    }

    private static async Task<Guid> CreateWalkedOpenFieldUnitAsync(HttpClient client, string farmerJwt, string name)
    {
        using HttpRequestMessage startReq = new(HttpMethod.Post, "/v1/production-units/boundary-sessions");
        startReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
        HttpResponseMessage startResp = await client.SendAsync(startReq);
        Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);

        Guid sessionId;
        using (JsonDocument doc = JsonDocument.Parse(await startResp.Content.ReadAsStringAsync()))
        {
            sessionId = doc.RootElement.GetProperty("data").GetProperty("sessionId").GetGuid();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow.AddMinutes(-20);
        using HttpRequestMessage batchReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/points/batch");
        batchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
        batchReq.Content = JsonContent.Create(new
        {
            points = new object[]
            {
                new { clientGuid = Guid.NewGuid(), sequenceNumber = 1, lat = 7.7000, lng = 3.9200, accuracyM = 4.0, recordedAt = now },
                new { clientGuid = Guid.NewGuid(), sequenceNumber = 2, lat = 7.7000, lng = 3.9210, accuracyM = 4.0, recordedAt = now.AddMinutes(1) },
                new { clientGuid = Guid.NewGuid(), sequenceNumber = 3, lat = 7.7010, lng = 3.9210, accuracyM = 4.0, recordedAt = now.AddMinutes(2) },
                new { clientGuid = Guid.NewGuid(), sequenceNumber = 4, lat = 7.7010, lng = 3.9200, accuracyM = 4.0, recordedAt = now.AddMinutes(3) }
            }
        });
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(batchReq)).StatusCode);

        using HttpRequestMessage compReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/complete");
        compReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(compReq)).StatusCode);

        using HttpRequestMessage confReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/confirm");
        confReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
        confReq.Content = JsonContent.Create(new { name, systemType = "open_field", growingMediumClass = "soil_based" });
        HttpResponseMessage confResp = await client.SendAsync(confReq);
        Assert.Equal(HttpStatusCode.Created, confResp.StatusCode);

        using JsonDocument confDoc = JsonDocument.Parse(await confResp.Content.ReadAsStringAsync());
        return confDoc.RootElement.GetProperty("data").GetProperty("productionUnitId").GetGuid();
    }

    private async Task<(Guid OrgId, Guid TabletId, Guid SessionId)> SetupVerifiedOrgAgentAndSessionAsync(
        HttpClient client,
        Guid adminId,
        string adminJwt,
        string orgAdminJwt,
        Guid agentId,
        string agentJwt,
        string deviceSerial,
        string deviceFingerprint)
    {
        await ProvisionViaAuthMeAsync(client, adminJwt);
        await ProvisionViaAuthMeAsync(client, orgAdminJwt);
        await ProvisionViaAuthMeAsync(client, agentJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantRoleAsync(conn, adminId, "SuperAdmin", "global", null, adminId);

        Guid orgId;
        using (HttpRequestMessage createOrg = new(HttpMethod.Post, "/v1/organizations"))
        {
            createOrg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminJwt);
            createOrg.Content = JsonContent.Create(new { name = $"Ops Co-op {Guid.NewGuid():N}", type = "cooperative" });
            HttpResponseMessage resp = await client.SendAsync(createOrg);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            orgId = doc.RootElement.GetProperty("data").GetProperty("organizationId").GetGuid();
        }

        using (HttpRequestMessage verifyOrg = new(HttpMethod.Post, $"/v1/admin/organizations/{orgId}/verify"))
        {
            verifyOrg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(verifyOrg)).StatusCode);
        }

        using (HttpRequestMessage addAgent = new(HttpMethod.Post, $"/v1/organizations/{orgId}/agents"))
        {
            addAgent.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminJwt);
            addAgent.Content = JsonContent.Create(new { agentIdentityId = agentId });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(addAgent)).StatusCode);
        }

        Guid tabletId;
        using (HttpRequestMessage bindTablet = new(HttpMethod.Post, "/v1/tablets/bind"))
        {
            bindTablet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminJwt);
            bindTablet.Content = JsonContent.Create(new
            {
                deviceSerial,
                deviceFingerprint,
                organizationId = orgId,
                assignedAgentId = agentId
            });
            HttpResponseMessage resp = await client.SendAsync(bindTablet);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            tabletId = doc.RootElement.GetProperty("data").GetProperty("tabletId").GetGuid();
        }

        Guid sessionId;
        using (HttpRequestMessage startSess = new(HttpMethod.Post, "/v1/agents/sessions"))
        {
            startSess.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            startSess.Content = JsonContent.Create(new { tabletId, deviceFingerprint, lifetimeHours = 72 });
            HttpResponseMessage resp = await client.SendAsync(startSess);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            sessionId = doc.RootElement.GetProperty("data").GetProperty("sessionId").GetGuid();
        }

        return (orgId, tabletId, sessionId);
    }

    private static async Task<Guid> GetCropProfileIdAsync(NpgsqlConnection conn, string commonName)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM field.crop_profile WHERE common_name = @name LIMIT 1;";
        cmd.Parameters.AddWithValue("name", commonName);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task AdvanceStageAsync(HttpClient client, string jwt, Guid cycleId)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/advance-stage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req.Content = JsonContent.Create(new { });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
    }

    private static async Task LogTaskAsync(HttpClient client, string jwt, Guid cycleId, string taskDefId, object? data = null)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/tasks/{taskDefId}/log");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        req.Content = JsonContent.Create(new { data });
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Wp90ContinuousHarvestBatchesListDispositionAuditAndClosedCycleLock()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid farmerId = Guid.NewGuid();
        string farmerJwt = CreateSignedToken(farmerId);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        Guid maizeId = await GetCropProfileIdAsync(conn, "Maize");

        Guid unitId = await CreateWalkedOpenFieldUnitAsync(client, farmerJwt, "Staggered Harvest Plot");
        DateOnly maturePlantingDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-100));

        Guid cycleId;
        using (HttpRequestMessage startCyc = new(HttpMethod.Post, "/v1/crop-cycles"))
        {
            startCyc.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            startCyc.Content = JsonContent.Create(new
            {
                productionUnitId = unitId,
                cropProfileId = maizeId,
                plantingDate = maturePlantingDate
            });
            HttpResponseMessage resp = await client.SendAsync(startCyc);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            cycleId = d.RootElement.GetProperty("data").GetProperty("cycleId").GetGuid();
        }

        // Progress to harvest stage
        await AdvanceStageAsync(client, farmerJwt, cycleId); // pre_planting
        await LogTaskAsync(client, farmerJwt, cycleId, "PRE-01");
        await LogTaskAsync(client, farmerJwt, cycleId, "PRE-06", new { soilPh = 6.2m });
        await AdvanceStageAsync(client, farmerJwt, cycleId); // planting
        await LogTaskAsync(client, farmerJwt, cycleId, "PLT-01", new { seedDepthCm = 4.0m });
        await LogTaskAsync(client, farmerJwt, cycleId, "PLT-02", new { rowSpacingCm = 80.0m });
        await AdvanceStageAsync(client, farmerJwt, cycleId); // crop_care
        await LogTaskAsync(client, farmerJwt, cycleId, "CC-01");
        await LogTaskAsync(client, farmerJwt, cycleId, "CC-05");
        await LogTaskAsync(client, farmerJwt, cycleId, "CC-22");
        await AdvanceStageAsync(client, farmerJwt, cycleId); // harvest
        await LogTaskAsync(client, farmerJwt, cycleId, "HRV-17", new { harvestMoisturePct = 13.5m });

        // Log 3 staggered harvest batches (FR-GM-002 multi-batch reuse)
        decimal[] batchWeights = [120.50m, 85.00m, 94.50m];
        List<Guid> batchIds = [];
        for (int idx = 0; idx < batchWeights.Length; idx++)
        {
            using HttpRequestMessage hbReq = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/harvest-batches");
            hbReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            hbReq.Content = JsonContent.Create(new
            {
                quantityKg = batchWeights[idx],
                grade = "Grade A",
                moisturePct = 13.5m,
                harvestDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3 + idx))
            });
            HttpResponseMessage hbResp = await client.SendAsync(hbReq);
            Assert.Equal(HttpStatusCode.Created, hbResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await hbResp.Content.ReadAsStringAsync());
            batchIds.Add(d.RootElement.GetProperty("data").GetProperty("harvestBatchId").GetGuid());
        }

        // GET /v1/crop-cycles/{cycleId}/harvest-batches lists all 3 batches + totals
        using (HttpRequestMessage listReq = new(HttpMethod.Get, $"/v1/crop-cycles/{cycleId}/harvest-batches"))
        {
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            HttpResponseMessage listResp = await client.SendAsync(listReq);
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
            JsonElement data = d.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("supportsContinuousHarvest").GetBoolean());
            Assert.Equal(300.00m, data.GetProperty("totalHarvestedKg").GetDecimal());
            Assert.Equal(3, data.GetProperty("openBatchCount").GetInt32());
            Assert.Equal(3, data.GetProperty("batches").GetArrayLength());
            Assert.NotEqual(JsonValueKind.Null, data.GetProperty("yieldVariancePct").ValueKind);
        }

        string[] dispositions = ["sold", "stored", "processed"];
        for (int idx = 0; idx < batchIds.Count; idx++)
        {
            using HttpRequestMessage dispReq = new(HttpMethod.Patch, $"/v1/harvest-batches/{batchIds[idx]}/disposition");
            dispReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            dispReq.Content = JsonContent.Create(new { disposition = dispositions[idx] });
            HttpResponseMessage dispResp = await client.SendAsync(dispReq);
            Assert.Equal(HttpStatusCode.OK, dispResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await dispResp.Content.ReadAsStringAsync());
            JsonElement data = d.RootElement.GetProperty("data");
            Assert.Equal("open", data.GetProperty("previousDisposition").GetString());
            Assert.Equal(dispositions[idx], data.GetProperty("disposition").GetString());
        }

        // Advance harvest -> post_harvest -> closed
        await AdvanceStageAsync(client, farmerJwt, cycleId);
        await LogTaskAsync(client, farmerJwt, cycleId, "PHV-01");
        await AdvanceStageAsync(client, farmerJwt, cycleId);

        // Closed cycle rejects further disposition changes with 409 CYCLE_NOT_OPEN
        using (HttpRequestMessage lockedReq = new(HttpMethod.Patch, $"/v1/harvest-batches/{batchIds[0]}/disposition"))
        {
            lockedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            lockedReq.Content = JsonContent.Create(new { disposition = "lost" });
            HttpResponseMessage lockedResp = await client.SendAsync(lockedReq);
            Assert.Equal(HttpStatusCode.Conflict, lockedResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await lockedResp.Content.ReadAsStringAsync());
            Assert.Equal("CYCLE_NOT_OPEN", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Wp91FieldOpsRosterSmallSampleSuppressionTabletsAndOrgScopeIsolation()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid adminId = Guid.NewGuid();
        Guid orgAdminAId = Guid.NewGuid();
        Guid orgAdminBId = Guid.NewGuid();
        Guid agentLowId = Guid.NewGuid();
        Guid agentHighId = Guid.NewGuid();
        Guid agentBId = Guid.NewGuid();
        Guid opsFarmerId = Guid.NewGuid();

        string adminJwt = CreateSignedToken(adminId);
        string orgAdminAJwt = CreateSignedToken(orgAdminAId);
        string orgAdminBJwt = CreateSignedToken(orgAdminBId);
        string agentLowJwt = CreateSignedToken(agentLowId);
        string agentHighJwt = CreateSignedToken(agentHighId);
        string agentBJwt = CreateSignedToken(agentBId);
        string farmerJwt = CreateSignedToken(opsFarmerId);

        await ProvisionViaAuthMeAsync(client, orgAdminBJwt);
        await ProvisionViaAuthMeAsync(client, agentHighJwt);
        await ProvisionViaAuthMeAsync(client, agentBJwt);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        (Guid orgAId, Guid tabletAId, _) = await SetupVerifiedOrgAgentAndSessionAsync(
            client,
            adminId,
            adminJwt,
            orgAdminAJwt,
            agentLowId,
            agentLowJwt,
            deviceSerial: $"TAB-A-{Guid.NewGuid():N}"[..16],
            deviceFingerprint: $"FP-A-{Guid.NewGuid():N}");

        using (HttpRequestMessage addHigh = new(HttpMethod.Post, $"/v1/organizations/{orgAId}/agents"))
        {
            addHigh.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminAJwt);
            addHigh.Content = JsonContent.Create(new { agentIdentityId = agentHighId });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(addHigh)).StatusCode);
        }

        // Create second org B with its own tablet so we verify OrgAdmin A cannot see Org B
        Guid orgBId;
        using (HttpRequestMessage createB = new(HttpMethod.Post, "/v1/organizations"))
        {
            createB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminBJwt);
            createB.Content = JsonContent.Create(new { name = $"Org B {Guid.NewGuid():N}", type = "ngo" });
            HttpResponseMessage resp = await client.SendAsync(createB);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            orgBId = d.RootElement.GetProperty("data").GetProperty("organizationId").GetGuid();
        }
        using (HttpRequestMessage verB = new(HttpMethod.Post, $"/v1/admin/organizations/{orgBId}/verify"))
        {
            verB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(verB)).StatusCode);
        }
        using (HttpRequestMessage addB = new(HttpMethod.Post, $"/v1/organizations/{orgBId}/agents"))
        {
            addB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminBJwt);
            addB.Content = JsonContent.Create(new { agentIdentityId = agentBId });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(addB)).StatusCode);
        }
        Guid tabletBId;
        using (HttpRequestMessage bindB = new(HttpMethod.Post, "/v1/tablets/bind"))
        {
            bindB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminBJwt);
            bindB.Content = JsonContent.Create(new
            {
                deviceSerial = $"TAB-B-{Guid.NewGuid():N}"[..16],
                deviceFingerprint = $"FP-B-{Guid.NewGuid():N}",
                organizationId = orgBId,
                assignedAgentId = agentBId
            });
            HttpResponseMessage resp = await client.SendAsync(bindB);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            tabletBId = d.RootElement.GetProperty("data").GetProperty("tabletId").GetGuid();
        }

        // Seed 3 logs (small sample) for agentLowId and 10 logs (3 flagged) for agentHighId
        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantRoleAsync(conn, orgAdminAId, "FieldOperationsAdmin", "organization", orgAId, adminId);

        Guid unitId = await CreateWalkedOpenFieldUnitAsync(client, farmerJwt, "FO Quality Plot");
        Guid maizeId = await GetCropProfileIdAsync(conn, "Maize");
        Guid cycleId;
        using (HttpRequestMessage cycReq = new(HttpMethod.Post, "/v1/crop-cycles"))
        {
            cycReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            cycReq.Content = JsonContent.Create(new { productionUnitId = unitId, cropProfileId = maizeId });
            using JsonDocument d = JsonDocument.Parse(await (await client.SendAsync(cycReq)).Content.ReadAsStringAsync());
            cycleId = d.RootElement.GetProperty("data").GetProperty("cycleId").GetGuid();
        }

        await using (NpgsqlCommand seedLogs = conn.CreateCommand())
        {
            seedLogs.CommandText = """
                INSERT INTO field.operation_log (
                    crop_cycle_id, task_definition_id, logged_by_identity_id, acting_agent_id, status
                )
                SELECT @cycleId, 'CC-05', @agentLow, @agentLow, CASE WHEN gs = 1 THEN 'flag'::task_status ELSE 'ok'::task_status END
                FROM generate_series(1, 3) AS gs
                UNION ALL
                SELECT @cycleId, 'CC-05', @agentHigh, @agentHigh, CASE WHEN gs <= 3 THEN 'flag'::task_status ELSE 'ok'::task_status END
                FROM generate_series(1, 10) AS gs;
                """;
            seedLogs.Parameters.AddWithValue("cycleId", cycleId);
            seedLogs.Parameters.AddWithValue("agentLow", agentLowId);
            seedLogs.Parameters.AddWithValue("agentHigh", agentHighId);
            await seedLogs.ExecuteNonQueryAsync();
        }

        // 1. Org-scoped admin queries Org A roster -> small sample suppressed, >=10 elevated computed
        using (HttpRequestMessage foReq = new(HttpMethod.Get, $"/v1/admin/field-ops/agents/{orgAId}"))
        {
            foReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminAJwt);
            HttpResponseMessage foResp = await client.SendAsync(foReq);
            Assert.Equal(HttpStatusCode.OK, foResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await foResp.Content.ReadAsStringAsync());
            JsonElement data = d.RootElement.GetProperty("data");
            Assert.Equal("organization", data.GetProperty("effectiveScopeType").GetString());
            JsonElement agents = data.GetProperty("agents");
            Assert.Equal(2, agents.GetArrayLength());

            JsonElement lowRow = agents.EnumerateArray().First(a => a.GetProperty("agentId").GetGuid() == agentLowId);
            Assert.False(lowRow.GetProperty("isLogSampleSufficient").GetBoolean());
            Assert.Equal(JsonValueKind.Null, lowRow.GetProperty("flagRate").ValueKind);
            Assert.Equal("insufficient_sample", lowRow.GetProperty("signalStatus").GetString());

            JsonElement highRow = agents.EnumerateArray().First(a => a.GetProperty("agentId").GetGuid() == agentHighId);
            Assert.True(highRow.GetProperty("isLogSampleSufficient").GetBoolean());
            Assert.Equal(0.3000m, highRow.GetProperty("flagRate").GetDecimal());
            Assert.Equal("elevated", highRow.GetProperty("signalStatus").GetString());
        }

        // 2. Org-scoped admin tablet fleet sees Org A tablet only (never Org B tablet)
        using (HttpRequestMessage tabReq = new(HttpMethod.Get, "/v1/admin/field-ops/tablets"))
        {
            tabReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminAJwt);
            HttpResponseMessage tabResp = await client.SendAsync(tabReq);
            Assert.Equal(HttpStatusCode.OK, tabResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await tabResp.Content.ReadAsStringAsync());
            JsonElement tablets = d.RootElement.GetProperty("data").GetProperty("tablets");
            Assert.Contains(tablets.EnumerateArray(), t => t.GetProperty("tabletId").GetGuid() == tabletAId);
            Assert.DoesNotContain(tablets.EnumerateArray(), t => t.GetProperty("tabletId").GetGuid() == tabletBId);
        }

        // 3. Cross-org request rejected with 403 SCOPE_VIOLATION (FR-FO-003)
        using (HttpRequestMessage crossOrgReq = new(HttpMethod.Get, $"/v1/admin/field-ops/agents/{orgBId}"))
        {
            crossOrgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminAJwt);
            HttpResponseMessage crossResp = await client.SendAsync(crossOrgReq);
            Assert.Equal(HttpStatusCode.Forbidden, crossResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await crossResp.Content.ReadAsStringAsync());
            Assert.Equal("SCOPE_VIOLATION", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Wp92AnalyticsBaselineEnforcesRegionScopeAndSeparatesMeasuredFromDeclaredArea()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        SandboxSmsGatewayClient smsCapture = factory.Services.GetRequiredService<SandboxSmsGatewayClient>();
        smsCapture.Clear();

        Guid superAdminId = Guid.NewGuid();
        Guid regionAdminAId = Guid.NewGuid();
        string superAdminJwt = CreateSignedToken(superAdminId);
        string regionAdminAJwt = CreateSignedToken(regionAdminAId);
        await ProvisionViaAuthMeAsync(client, superAdminJwt);
        await ProvisionViaAuthMeAsync(client, regionAdminAJwt);

        // Seed two isolated states + LGAs so multi-sprint DB rows never bleed into State Alpha / Beta assertions
        Guid stateAlphaId = Guid.NewGuid();
        Guid lgaAlphaId = Guid.NewGuid();
        Guid stateBetaId = Guid.NewGuid();
        Guid lgaBetaId = Guid.NewGuid();
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantRoleAsync(conn, superAdminId, "SuperAdmin", "global", null, superAdminId);
        await GrantRoleAsync(conn, regionAdminAId, "FieldOperationsAdmin", "region", stateAlphaId, superAdminId);

        await using (NpgsqlCommand geoSeed = conn.CreateCommand())
        {
            geoSeed.CommandText = """
                INSERT INTO identity.state (id, name, code) VALUES
                    (@saId, @saName, @saCode),
                    (@sbId, @sbName, @sbCode);
                INSERT INTO identity.lga (id, state_id, name, code) VALUES
                    (@laId, @saId, @laName, @laCode),
                    (@lbId, @sbId, @lbName, @lbCode);
                """;
            geoSeed.Parameters.AddWithValue("saId", stateAlphaId);
            geoSeed.Parameters.AddWithValue("saName", $"S9 Alpha {suffix}");
            geoSeed.Parameters.AddWithValue("saCode", $"A{suffix[..3]}");
            geoSeed.Parameters.AddWithValue("sbId", stateBetaId);
            geoSeed.Parameters.AddWithValue("sbName", $"S9 Beta {suffix}");
            geoSeed.Parameters.AddWithValue("sbCode", $"B{suffix[..3]}");
            geoSeed.Parameters.AddWithValue("laId", lgaAlphaId);
            geoSeed.Parameters.AddWithValue("laName", $"LGA Alpha {suffix}");
            geoSeed.Parameters.AddWithValue("laCode", $"LA{suffix[..4]}");
            geoSeed.Parameters.AddWithValue("lbId", lgaBetaId);
            geoSeed.Parameters.AddWithValue("lbName", $"LGA Beta {suffix}");
            geoSeed.Parameters.AddWithValue("lbCode", $"LB{suffix[..4]}");
            await geoSeed.ExecuteNonQueryAsync();
        }

        string uniqueTail = RandomNumberGenerator.GetInt32(10000, 99999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Guid farmerAlphaId = await RegisterVerifiedFarmerAsync(client, smsCapture, $"770921{uniqueTail}", "+2348092100001", stateAlphaId, lgaAlphaId);
        Guid farmerBetaId = await RegisterVerifiedFarmerAsync(client, smsCapture, $"770922{uniqueTail}", "+2348092200002", stateBetaId, lgaBetaId);

        string farmerAlphaJwt = CreateSignedToken(farmerAlphaId);
        string farmerBetaJwt = CreateSignedToken(farmerBetaId);

        Guid unitAlphaWalkedId = await CreateWalkedOpenFieldUnitAsync(client, farmerAlphaJwt, "Alpha Walked Plot");
        Guid unitBetaWalkedId = await CreateWalkedOpenFieldUnitAsync(client, farmerBetaJwt, "Beta Walked Plot");

        // Add declared (unwalked manual_footprint_entry) unit of 2.5000 ha in State Alpha
        await using (NpgsqlCommand declUnit = conn.CreateCommand())
        {
            declUnit.CommandText = """
                INSERT INTO field.production_unit (
                    id, farm_identity_id, name, system_type, capture_method, growing_medium_class,
                    lga_id, area_square_meters, area_basis
                )
                VALUES (
                    gen_random_uuid(), @farmerId, 'Alpha Declared Plot', 'open_field', 'manual_footprint_entry', 'soil_based',
                    @lgaId, 25000.00, 'declared'
                );
                """;
            declUnit.Parameters.AddWithValue("farmerId", farmerAlphaId);
            declUnit.Parameters.AddWithValue("lgaId", lgaAlphaId);
            await declUnit.ExecuteNonQueryAsync();
        }

        Guid maizeId = await GetCropProfileIdAsync(conn, "Maize");
        Guid alphaCycleId;
        using (HttpRequestMessage caReq = new(HttpMethod.Post, "/v1/crop-cycles"))
        {
            caReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerAlphaJwt);
            caReq.Content = JsonContent.Create(new { productionUnitId = unitAlphaWalkedId, cropProfileId = maizeId });
            using JsonDocument d = JsonDocument.Parse(await (await client.SendAsync(caReq)).Content.ReadAsStringAsync());
            alphaCycleId = d.RootElement.GetProperty("data").GetProperty("cycleId").GetGuid();
        }

        Guid betaCycleId;
        using (HttpRequestMessage cbReq = new(HttpMethod.Post, "/v1/crop-cycles"))
        {
            cbReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerBetaJwt);
            cbReq.Content = JsonContent.Create(new { productionUnitId = unitBetaWalkedId, cropProfileId = maizeId });
            using JsonDocument d = JsonDocument.Parse(await (await client.SendAsync(cbReq)).Content.ReadAsStringAsync());
            betaCycleId = d.RootElement.GetProperty("data").GetProperty("cycleId").GetGuid();
        }

        using (HttpRequestMessage hba = new(HttpMethod.Post, $"/v1/crop-cycles/{alphaCycleId}/harvest-batches"))
        {
            hba.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerAlphaJwt);
            hba.Content = JsonContent.Create(new { quantityKg = 420.00m });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(hba)).StatusCode);
        }

        using (HttpRequestMessage hbb = new(HttpMethod.Post, $"/v1/crop-cycles/{betaCycleId}/harvest-batches"))
        {
            hbb.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerBetaJwt);
            hbb.Content = JsonContent.Create(new { quantityKg = 999.00m });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(hbb)).StatusCode);
        }

        // Region-scoped admin reads /v1/admin/analytics/baseline -> sees ONLY State Alpha
        using (HttpRequestMessage regReq = new(HttpMethod.Get, "/v1/admin/analytics/baseline"))
        {
            regReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", regionAdminAJwt);
            HttpResponseMessage regResp = await client.SendAsync(regReq);
            Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await regResp.Content.ReadAsStringAsync());
            JsonElement data = d.RootElement.GetProperty("data");

            Assert.Equal("region", data.GetProperty("effectiveScopeType").GetString());
            Assert.True(data.GetProperty("computedLive").GetBoolean());
            Assert.Equal(1, data.GetProperty("totalFarmers").GetInt32());
            Assert.Equal(1, data.GetProperty("verifiedFarmers").GetInt32());
            Assert.Equal(2, data.GetProperty("productionUnitsCount").GetInt32());
            Assert.InRange(data.GetProperty("measuredAreaHectares").GetDecimal(), 1.0m, 1.5m);
            Assert.Equal(2.5000m, data.GetProperty("declaredAreaHectares").GetDecimal());
            Assert.Equal(1, data.GetProperty("activeCropCycles").GetInt32());
            Assert.Equal(420.00m, data.GetProperty("totalHarvestedKg").GetDecimal());
        }

        // Cross-region request by Region Admin A for State Beta or LGA Beta -> 403 SCOPE_VIOLATION
        using (HttpRequestMessage outStateReq = new(HttpMethod.Get, $"/v1/admin/analytics/baseline?stateId={stateBetaId}"))
        {
            outStateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", regionAdminAJwt);
            HttpResponseMessage outStateResp = await client.SendAsync(outStateReq);
            Assert.Equal(HttpStatusCode.Forbidden, outStateResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await outStateResp.Content.ReadAsStringAsync());
            Assert.Equal("SCOPE_VIOLATION", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        using (HttpRequestMessage outLgaReq = new(HttpMethod.Get, $"/v1/admin/analytics/baseline?lgaId={lgaBetaId}"))
        {
            outLgaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", regionAdminAJwt);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(outLgaReq)).StatusCode);
        }

        // Global SuperAdmin querying State Beta sees State Beta's 999.00 kg cleanly
        using (HttpRequestMessage globalBetaReq = new(HttpMethod.Get, $"/v1/admin/analytics/baseline?stateId={stateBetaId}"))
        {
            globalBetaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminJwt);
            HttpResponseMessage globalResp = await client.SendAsync(globalBetaReq);
            Assert.Equal(HttpStatusCode.OK, globalResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await globalResp.Content.ReadAsStringAsync());
            JsonElement data = d.RootElement.GetProperty("data");
            Assert.Equal("global", data.GetProperty("effectiveScopeType").GetString());
            Assert.Equal(999.00m, data.GetProperty("totalHarvestedKg").GetDecimal());
        }
    }

    [Fact]
    public async Task Wp93SupportReadViewMandatoryCaseRefAndDecisionRecordedFobEscalation()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        SandboxSmsGatewayClient smsCapture = factory.Services.GetRequiredService<SandboxSmsGatewayClient>();
        smsCapture.Clear();

        Guid adminId = Guid.NewGuid();
        Guid orgAdminId = Guid.NewGuid();
        Guid agentId = Guid.NewGuid();
        Guid supportAgentId = Guid.NewGuid();
        Guid trustAdminId = Guid.NewGuid();

        string adminJwt = CreateSignedToken(adminId);
        string orgAdminJwt = CreateSignedToken(orgAdminId);
        string agentJwt = CreateSignedToken(agentId);
        string supportJwt = CreateSignedToken(supportAgentId);
        string trustAdminJwt = CreateSignedToken(trustAdminId);

        await ProvisionViaAuthMeAsync(client, supportJwt);
        await ProvisionViaAuthMeAsync(client, trustAdminJwt);

        (_, _, Guid sessionId) = await SetupVerifiedOrgAgentAndSessionAsync(
            client,
            adminId,
            adminJwt,
            orgAdminJwt,
            agentId,
            agentJwt,
            deviceSerial: $"TAB-S93-{Guid.NewGuid():N}"[..16],
            deviceFingerprint: $"FP-S93-{Guid.NewGuid():N}");

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantRoleAsync(conn, supportAgentId, "CustomerSupportAgent", "global", null, adminId);
        await GrantRoleAsync(conn, trustAdminId, "IdentityTrustAdmin", "global", null, adminId);

        string uniqueTail = RandomNumberGenerator.GetInt32(10000, 99999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Guid farmerId = await RegisterVerifiedFarmerAsync(client, smsCapture, $"770931{uniqueTail}", "+2348093100001");

        // Issue a physical fob to the farmer
        string secretFobSerial = $"FOB-SECRET-{Guid.NewGuid():N}"[..20];
        Guid fobId;
        using (HttpRequestMessage issueFob = new(HttpMethod.Post, "/v1/fobs/issue"))
        {
            issueFob.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            issueFob.Content = JsonContent.Create(new { identityId = farmerId, fobSerial = secretFobSerial, agentSessionId = sessionId });
            HttpResponseMessage resp = await client.SendAsync(issueFob);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            fobId = d.RootElement.GetProperty("data").GetProperty("fobId").GetGuid();
        }

        // 1. Missing X-Support-Case-Ref rejected with 400 SUPPORT_CASE_REF_REQUIRED
        using (HttpRequestMessage missingCaseReq = new(HttpMethod.Get, $"/v1/admin/support/identities/{farmerId}"))
        {
            missingCaseReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supportJwt);
            HttpResponseMessage missingResp = await client.SendAsync(missingCaseReq);
            Assert.Equal(HttpStatusCode.BadRequest, missingResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await missingResp.Content.ReadAsStringAsync());
            Assert.Equal("SUPPORT_CASE_REF_REQUIRED", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // 2. With X-Support-Case-Ref: status-only read view returned, sensitive fields omitted, lookup audited
        string caseRef = $"CASE-2026-{uniqueTail}";
        using (HttpRequestMessage readReq = new(HttpMethod.Get, $"/v1/admin/support/identities/{farmerId}"))
        {
            readReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supportJwt);
            readReq.Headers.Add("X-Support-Case-Ref", caseRef);
            HttpResponseMessage readResp = await client.SendAsync(readReq);
            Assert.Equal(HttpStatusCode.OK, readResp.StatusCode);

            string rawJson = await readResp.Content.ReadAsStringAsync();
            Assert.DoesNotContain(secretFobSerial, rawJson, StringComparison.Ordinal);
            Assert.DoesNotContain("fobSerial", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ninVerificationRef", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("phonePrimary", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("balance", rawJson, StringComparison.OrdinalIgnoreCase);

            using JsonDocument d = JsonDocument.Parse(rawJson);
            JsonElement data = d.RootElement.GetProperty("data");
            Assert.Equal("active", data.GetProperty("status").GetString());
            Assert.Equal(caseRef, data.GetProperty("supportCaseRef").GetString());
            JsonElement fobItem = Assert.Single(data.GetProperty("fobs").EnumerateArray());
            Assert.Equal(fobId, fobItem.GetProperty("fobId").GetGuid());
            Assert.Equal("active", fobItem.GetProperty("status").GetString());
        }

        // Verify support_case_ref persisted in audit.audit_log_entry payload
        await using (NpgsqlCommand lookupAuditCmd = conn.CreateCommand())
        {
            lookupAuditCmd.CommandText = """
                SELECT payload->>'supportCaseRef'
                FROM audit.audit_log_entry
                WHERE domain = 'support'
                  AND event_type = 'support.identity_lookup'
                  AND entity_id = @farmerId
                ORDER BY created_at DESC
                LIMIT 1;
                """;
            lookupAuditCmd.Parameters.AddWithValue("farmerId", farmerId);
            Assert.Equal(caseRef, await lookupAuditCmd.ExecuteScalarAsync() as string);
        }

        // 3. P3 safe posture: CustomerSupportAgent CANNOT directly call POST /v1/fobs/{fobId}/report-lost -> 403
        using (HttpRequestMessage directRevoke = new(HttpMethod.Post, $"/v1/fobs/{fobId}/report-lost"))
        {
            directRevoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supportJwt);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(directRevoke)).StatusCode);
        }

        // 4. Support escalates via POST /v1/admin/support/fobs/{fobId}/revoke-request -> 202 urgent IdentityTrustAdmin review
        Guid reviewQueueItemId;
        using (HttpRequestMessage escReq = new(HttpMethod.Post, $"/v1/admin/support/fobs/{fobId}/revoke-request"))
        {
            escReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supportJwt);
            escReq.Content = JsonContent.Create(new
            {
                supportCaseRef = caseRef,
                reason = "Farmer called helpline reporting lost NFC fob in market."
            });
            HttpResponseMessage escResp = await client.SendAsync(escReq);
            Assert.Equal(HttpStatusCode.Accepted, escResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await escResp.Content.ReadAsStringAsync());
            JsonElement data = d.RootElement.GetProperty("data");
            Assert.Equal("P3", data.GetProperty("decisionRef").GetString());
            Assert.Equal("IdentityTrustAdmin", data.GetProperty("routedToRole").GetString());
            Assert.Equal(caseRef, data.GetProperty("supportCaseRef").GetString());
            reviewQueueItemId = data.GetProperty("reviewQueueItemId").GetGuid();
        }

        await using (NpgsqlCommand rqCmd = conn.CreateCommand())
        {
            rqCmd.CommandText = """
                SELECT rc.code, rqi.priority::text
                FROM review.review_queue_item rqi
                JOIN review.review_category rc ON rc.id = rqi.category_id
                WHERE rqi.id = @id;
                """;
            rqCmd.Parameters.AddWithValue("id", reviewQueueItemId);
            await using NpgsqlDataReader r = await rqCmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("SUPPORT_FOB_REVOCATION_ESCALATION", r.GetString(0));
            Assert.Equal("urgent", r.GetString(1));
        }

        // 5. IdentityTrustAdmin executes revocation passing X-Support-Case-Ref -> permanent revocation + case ref audited
        using (HttpRequestMessage trustRevoke = new(HttpMethod.Post, $"/v1/fobs/{fobId}/report-lost"))
        {
            trustRevoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", trustAdminJwt);
            trustRevoke.Headers.Add("X-Support-Case-Ref", caseRef);
            HttpResponseMessage resp = await client.SendAsync(trustRevoke);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        await using (NpgsqlCommand revAuditCmd = conn.CreateCommand())
        {
            revAuditCmd.CommandText = """
                SELECT payload->>'supportCaseRef'
                FROM audit.audit_log_entry
                WHERE event_type = 'fob.revoked'
                  AND entity_id = @fobId
                ORDER BY created_at DESC
                LIMIT 1;
                """;
            revAuditCmd.Parameters.AddWithValue("fobId", fobId);
            Assert.Equal(caseRef, await revAuditCmd.ExecuteScalarAsync() as string);
        }
    }

    [Fact]
    public async Task Wp94InPersonAgentVerifiedPhoneChangeRejectsSelfServiceAndUpdatesVerifiedPhone()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        SandboxSmsGatewayClient smsCapture = factory.Services.GetRequiredService<SandboxSmsGatewayClient>();
        smsCapture.Clear();

        Guid adminId = Guid.NewGuid();
        Guid orgAdminId = Guid.NewGuid();
        Guid agentId = Guid.NewGuid();
        string adminJwt = CreateSignedToken(adminId);
        string orgAdminJwt = CreateSignedToken(orgAdminId);
        string agentJwt = CreateSignedToken(agentId);

        (_, _, Guid sessionId) = await SetupVerifiedOrgAgentAndSessionAsync(
            client,
            adminId,
            adminJwt,
            orgAdminJwt,
            agentId,
            agentJwt,
            deviceSerial: $"TAB-S94-{Guid.NewGuid():N}"[..16],
            deviceFingerprint: $"FP-S94-{Guid.NewGuid():N}");

        string uniqueTail = RandomNumberGenerator.GetInt32(10000, 99999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string oldPhone = $"+23480941{uniqueTail}";
        string newPhoneE164 = $"+23480949{uniqueTail}";
        string newPhoneLocal = $"080949{uniqueTail}";

        Guid farmerId = await RegisterVerifiedFarmerAsync(client, smsCapture, $"770941{uniqueTail}", oldPhone);
        string farmerJwt = CreateSignedToken(farmerId);

        // 1. Self-service/remote farmer call refused with 403 AGENT_SESSION_REQUIRED (P6)
        using (HttpRequestMessage remoteAttempt = new(HttpMethod.Post, $"/v1/identities/{farmerId}/phone-change/initiate"))
        {
            remoteAttempt.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            remoteAttempt.Content = JsonContent.Create(new { newPhone = newPhoneLocal });
            HttpResponseMessage remoteResp = await client.SendAsync(remoteAttempt);
            Assert.Equal(HttpStatusCode.Forbidden, remoteResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await remoteResp.Content.ReadAsStringAsync());
            Assert.Equal("AGENT_SESSION_REQUIRED", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // 2. Active agent on bound tablet initiates phone change + dispatches SMS OTP to new phone
        smsCapture.Clear();
        Guid phoneChangeReqId;
        using (HttpRequestMessage initReq = new(HttpMethod.Post, $"/v1/identities/{farmerId}/phone-change/initiate"))
        {
            initReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            initReq.Headers.Add("X-Agent-Session-Id", sessionId.ToString());
            initReq.Content = JsonContent.Create(new { newPhone = newPhoneLocal });
            HttpResponseMessage initResp = await client.SendAsync(initReq);
            Assert.Equal(HttpStatusCode.Created, initResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await initResp.Content.ReadAsStringAsync());
            JsonElement data = d.RootElement.GetProperty("data");
            Assert.Equal("pending_otp", data.GetProperty("status").GetString());
            Assert.True(data.GetProperty("smsDispatched").GetBoolean());
            phoneChangeReqId = data.GetProperty("phoneChangeRequestId").GetGuid();
        }

        SmsSendRequest? sms = smsCapture.SentMessages.LastOrDefault(m => m.ToPhoneE164 == newPhoneE164);
        Assert.NotNull(sms);
        Match otpMatch = OtpCodeRegex().Match(sms.Body);
        Assert.True(otpMatch.Success);
        string otpCode = otpMatch.Groups[1].Value;

        // 3. Incorrect OTP rejected cleanly
        using (HttpRequestMessage badConfirm = new(HttpMethod.Post, $"/v1/identities/{farmerId}/phone-change/{phoneChangeReqId}/confirm"))
        {
            badConfirm.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            badConfirm.Headers.Add("X-Agent-Session-Id", sessionId.ToString());
            badConfirm.Content = JsonContent.Create(new { otp = otpCode == "000000" ? "111111" : "000000" });
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(badConfirm)).StatusCode);
        }

        // 4. Correct OTP completes in-person phone recovery and audits agent/tablet provenance
        using (HttpRequestMessage okConfirm = new(HttpMethod.Post, $"/v1/identities/{farmerId}/phone-change/{phoneChangeReqId}/confirm"))
        {
            okConfirm.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            okConfirm.Headers.Add("X-Agent-Session-Id", sessionId.ToString());
            okConfirm.Content = JsonContent.Create(new { otp = otpCode });
            HttpResponseMessage okResp = await client.SendAsync(okConfirm);
            Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await okResp.Content.ReadAsStringAsync());
            JsonElement data = d.RootElement.GetProperty("data");
            Assert.Equal("completed", data.GetProperty("status").GetString());
            Assert.Equal(newPhoneE164, data.GetProperty("newPhone").GetString());
        }

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();

        await using (NpgsqlCommand idCheck = conn.CreateCommand())
        {
            idCheck.CommandText = "SELECT phone_primary FROM identity.identity WHERE id = @id;";
            idCheck.Parameters.AddWithValue("id", farmerId);
            Assert.Equal(newPhoneE164, await idCheck.ExecuteScalarAsync() as string);
        }

        await using (NpgsqlCommand auditCheck = conn.CreateCommand())
        {
            auditCheck.CommandText = """
                SELECT payload->>'oldPhoneLast4', payload->>'newPhoneLast4'
                FROM audit.audit_log_entry
                WHERE event_type = 'identity.phone_changed_in_person'
                  AND entity_id = @farmerId
                ORDER BY created_at DESC
                LIMIT 1;
                """;
            auditCheck.Parameters.AddWithValue("farmerId", farmerId);
            await using NpgsqlDataReader ar = await auditCheck.ExecuteReaderAsync();
            Assert.True(await ar.ReadAsync());
            Assert.Equal(oldPhone[^4..], ar.GetString(0));
            Assert.Equal(newPhoneE164[^4..], ar.GetString(1));
        }
    }

    [Fact]
    public async Task Wp93AndWp94ScopeIsolationAndSameAgentHandshakeEnforced()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        SandboxSmsGatewayClient smsCapture = factory.Services.GetRequiredService<SandboxSmsGatewayClient>();
        smsCapture.Clear();

        Guid adminId = Guid.NewGuid();
        Guid orgAdminId = Guid.NewGuid();
        Guid agent1Id = Guid.NewGuid();
        Guid agent2Id = Guid.NewGuid();
        Guid supportAlphaId = Guid.NewGuid();
        Guid trustAlphaId = Guid.NewGuid();

        string adminJwt = CreateSignedToken(adminId);
        string orgAdminJwt = CreateSignedToken(orgAdminId);
        string agent1Jwt = CreateSignedToken(agent1Id);
        string agent2Jwt = CreateSignedToken(agent2Id);
        string supportAlphaJwt = CreateSignedToken(supportAlphaId);
        string trustAlphaJwt = CreateSignedToken(trustAlphaId);

        await ProvisionViaAuthMeAsync(client, agent2Jwt);
        await ProvisionViaAuthMeAsync(client, supportAlphaJwt);
        await ProvisionViaAuthMeAsync(client, trustAlphaJwt);

        (Guid orgId, _, Guid session1Id) = await SetupVerifiedOrgAgentAndSessionAsync(
            client,
            adminId,
            adminJwt,
            orgAdminJwt,
            agent1Id,
            agent1Jwt,
            deviceSerial: $"TAB-A1-{Guid.NewGuid():N}"[..16],
            deviceFingerprint: $"FP-A1-{Guid.NewGuid():N}");

        // Onboard second active agent + tablet session in same verified org
        using (HttpRequestMessage addAg2 = new(HttpMethod.Post, $"/v1/organizations/{orgId}/agents"))
        {
            addAg2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminJwt);
            addAg2.Content = JsonContent.Create(new { agentIdentityId = agent2Id });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(addAg2)).StatusCode);
        }

        string fp2 = $"FP-A2-{Guid.NewGuid():N}";
        Guid tablet2Id;
        using (HttpRequestMessage bindTab2 = new(HttpMethod.Post, "/v1/tablets/bind"))
        {
            bindTab2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminJwt);
            bindTab2.Content = JsonContent.Create(new
            {
                deviceSerial = $"TAB-A2-{Guid.NewGuid():N}"[..16],
                deviceFingerprint = fp2,
                organizationId = orgId,
                assignedAgentId = agent2Id
            });
            HttpResponseMessage resp = await client.SendAsync(bindTab2);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            tablet2Id = d.RootElement.GetProperty("data").GetProperty("tabletId").GetGuid();
        }

        Guid session2Id;
        using (HttpRequestMessage startSess2 = new(HttpMethod.Post, "/v1/agents/sessions"))
        {
            startSess2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent2Jwt);
            startSess2.Content = JsonContent.Create(new { tabletId = tablet2Id, deviceFingerprint = fp2, lifetimeHours = 72 });
            HttpResponseMessage resp = await client.SendAsync(startSess2);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            session2Id = d.RootElement.GetProperty("data").GetProperty("sessionId").GetGuid();
        }

        Guid stateAlphaId = Guid.NewGuid();
        Guid lgaAlphaId = Guid.NewGuid();
        Guid stateBetaId = Guid.NewGuid();
        Guid lgaBetaId = Guid.NewGuid();
        string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await using (NpgsqlCommand geoSeed = conn.CreateCommand())
        {
            geoSeed.CommandText = """
                INSERT INTO identity.state (id, name, code) VALUES
                    (@saId, @saName, @saCode),
                    (@sbId, @sbName, @sbCode);
                INSERT INTO identity.lga (id, state_id, name, code) VALUES
                    (@laId, @saId, @laName, @laCode),
                    (@lbId, @sbId, @lbName, @lbCode);
                """;
            geoSeed.Parameters.AddWithValue("saId", stateAlphaId);
            geoSeed.Parameters.AddWithValue("saName", $"S9R Alpha {suffix}");
            geoSeed.Parameters.AddWithValue("saCode", $"R{suffix[..3]}");
            geoSeed.Parameters.AddWithValue("sbId", stateBetaId);
            geoSeed.Parameters.AddWithValue("sbName", $"S9R Beta {suffix}");
            geoSeed.Parameters.AddWithValue("sbCode", $"Q{suffix[..3]}");
            geoSeed.Parameters.AddWithValue("laId", lgaAlphaId);
            geoSeed.Parameters.AddWithValue("laName", $"LGA RAlpha {suffix}");
            geoSeed.Parameters.AddWithValue("laCode", $"RA{suffix[..4]}");
            geoSeed.Parameters.AddWithValue("lbId", lgaBetaId);
            geoSeed.Parameters.AddWithValue("lbName", $"LGA RBeta {suffix}");
            geoSeed.Parameters.AddWithValue("lbCode", $"RB{suffix[..4]}");
            await geoSeed.ExecuteNonQueryAsync();
        }

        await GrantRoleAsync(conn, supportAlphaId, "CustomerSupportAgent", "region", stateAlphaId, adminId);
        await GrantRoleAsync(conn, trustAlphaId, "IdentityTrustAdmin", "region", stateAlphaId, adminId);

        string uniqueTail = RandomNumberGenerator.GetInt32(10000, 99999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Guid farmerAlphaId = await RegisterVerifiedFarmerAsync(client, smsCapture, $"770951{uniqueTail}", $"+23480951{uniqueTail}", stateAlphaId, lgaAlphaId);
        Guid farmerBetaId = await RegisterVerifiedFarmerAsync(client, smsCapture, $"770952{uniqueTail}", $"+23480952{uniqueTail}", stateBetaId, lgaBetaId);

        Guid fobAlphaId;
        using (HttpRequestMessage issueA = new(HttpMethod.Post, "/v1/fobs/issue"))
        {
            issueA.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent1Jwt);
            issueA.Content = JsonContent.Create(new { identityId = farmerAlphaId, fobSerial = $"FOB-RA-{Guid.NewGuid():N}"[..20], agentSessionId = session1Id });
            HttpResponseMessage resp = await client.SendAsync(issueA);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            fobAlphaId = d.RootElement.GetProperty("data").GetProperty("fobId").GetGuid();
        }

        Guid fobBetaId;
        using (HttpRequestMessage issueB = new(HttpMethod.Post, "/v1/fobs/issue"))
        {
            issueB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent1Jwt);
            issueB.Content = JsonContent.Create(new { identityId = farmerBetaId, fobSerial = $"FOB-RB-{Guid.NewGuid():N}"[..20], agentSessionId = session1Id });
            HttpResponseMessage resp = await client.SendAsync(issueB);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            fobBetaId = d.RootElement.GetProperty("data").GetProperty("fobId").GetGuid();
        }

        string caseRef = $"CASE-REG-{uniqueTail}";

        // 1. CustomerSupportAgent (State Alpha): in-scope Alpha allowed (200/202); out-of-scope Beta denied (403 SCOPE_VIOLATION)
        using (HttpRequestMessage alphaRead = new(HttpMethod.Get, $"/v1/admin/support/identities/{farmerAlphaId}"))
        {
            alphaRead.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supportAlphaJwt);
            alphaRead.Headers.Add("X-Support-Case-Ref", caseRef);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(alphaRead)).StatusCode);
        }

        using (HttpRequestMessage betaRead = new(HttpMethod.Get, $"/v1/admin/support/identities/{farmerBetaId}"))
        {
            betaRead.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supportAlphaJwt);
            betaRead.Headers.Add("X-Support-Case-Ref", caseRef);
            HttpResponseMessage resp = await client.SendAsync(betaRead);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("SCOPE_VIOLATION", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        using (HttpRequestMessage alphaEsc = new(HttpMethod.Post, $"/v1/admin/support/fobs/{fobAlphaId}/revoke-request"))
        {
            alphaEsc.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supportAlphaJwt);
            alphaEsc.Content = JsonContent.Create(new { supportCaseRef = caseRef, reason = "Lost NFC fob reported inside assigned State Alpha region." });
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(alphaEsc)).StatusCode);
        }

        using (HttpRequestMessage betaEsc = new(HttpMethod.Post, $"/v1/admin/support/fobs/{fobBetaId}/revoke-request"))
        {
            betaEsc.Headers.Authorization = new AuthenticationHeaderValue("Bearer", supportAlphaJwt);
            betaEsc.Content = JsonContent.Create(new { supportCaseRef = caseRef, reason = "Attempting cross-region fob escalation outside State Alpha." });
            HttpResponseMessage resp = await client.SendAsync(betaEsc);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("SCOPE_VIOLATION", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // 2. IdentityTrustAdmin (State Alpha): out-of-scope Beta report-lost refused (403 SCOPE_VIOLATION); in-scope Alpha succeeds (200 OK)
        using (HttpRequestMessage trustBetaRevoke = new(HttpMethod.Post, $"/v1/fobs/{fobBetaId}/report-lost"))
        {
            trustBetaRevoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", trustAlphaJwt);
            trustBetaRevoke.Headers.Add("X-Support-Case-Ref", caseRef);
            HttpResponseMessage resp = await client.SendAsync(trustBetaRevoke);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("SCOPE_VIOLATION", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        using (HttpRequestMessage trustAlphaRevoke = new(HttpMethod.Post, $"/v1/fobs/{fobAlphaId}/report-lost"))
        {
            trustAlphaRevoke.Headers.Authorization = new AuthenticationHeaderValue("Bearer", trustAlphaJwt);
            trustAlphaRevoke.Headers.Add("X-Support-Case-Ref", caseRef);
            HttpResponseMessage resp = await client.SendAsync(trustAlphaRevoke);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("revoked", d.RootElement.GetProperty("data").GetProperty("status").GetString());
        }

        // 3. PhoneChangeEndpoints: second agent attempting to confirm agent1's phone-change request receives 403 PERMISSION_DENIED
        smsCapture.Clear();
        string recoveredPhoneE164 = $"+23480958{uniqueTail}";
        string recoveredPhoneLocal = $"080958{uniqueTail}";
        Guid pcReqId;
        using (HttpRequestMessage initPc = new(HttpMethod.Post, $"/v1/identities/{farmerAlphaId}/phone-change/initiate"))
        {
            initPc.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent1Jwt);
            initPc.Headers.Add("X-Agent-Session-Id", session1Id.ToString());
            initPc.Content = JsonContent.Create(new { newPhone = recoveredPhoneLocal });
            HttpResponseMessage resp = await client.SendAsync(initPc);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            pcReqId = d.RootElement.GetProperty("data").GetProperty("phoneChangeRequestId").GetGuid();
        }

        SmsSendRequest? pcSms = smsCapture.SentMessages.LastOrDefault(m => m.ToPhoneE164 == recoveredPhoneE164);
        Assert.NotNull(pcSms);
        Match pcMatch = OtpCodeRegex().Match(pcSms.Body);
        Assert.True(pcMatch.Success);
        string pcOtp = pcMatch.Groups[1].Value;

        using (HttpRequestMessage otherAgentConfirm = new(HttpMethod.Post, $"/v1/identities/{farmerAlphaId}/phone-change/{pcReqId}/confirm"))
        {
            otherAgentConfirm.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent2Jwt);
            otherAgentConfirm.Headers.Add("X-Agent-Session-Id", session2Id.ToString());
            otherAgentConfirm.Content = JsonContent.Create(new { otp = pcOtp });
            HttpResponseMessage resp = await client.SendAsync(otherAgentConfirm);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("PERMISSION_DENIED", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        using (HttpRequestMessage origAgentConfirm = new(HttpMethod.Post, $"/v1/identities/{farmerAlphaId}/phone-change/{pcReqId}/confirm"))
        {
            origAgentConfirm.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent1Jwt);
            origAgentConfirm.Headers.Add("X-Agent-Session-Id", session1Id.ToString());
            origAgentConfirm.Content = JsonContent.Create(new { otp = pcOtp });
            HttpResponseMessage resp = await client.SendAsync(origAgentConfirm);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("completed", d.RootElement.GetProperty("data").GetProperty("status").GetString());
        }
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
