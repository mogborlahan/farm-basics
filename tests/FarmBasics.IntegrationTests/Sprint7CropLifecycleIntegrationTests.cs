using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
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

public sealed class Sprint7CropLifecycleIntegrationTests
    : IClassFixture<PostgisDatabaseFixture>, IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private readonly PostgisDatabaseFixture _dbFixture;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public Sprint7CropLifecycleIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "sprint7-test-key" };
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

    private static async Task GrantSuperAdminAsync(NpgsqlConnection conn, Guid identityId)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO admin.admin_role_assignment (id, identity_id, admin_role_id, scope_type, granted_by_admin_id)
            SELECT gen_random_uuid(), @identityId, r.id, 'global', @identityId
            FROM admin.admin_role r
            WHERE r.code = 'SuperAdmin';
            """;
        cmd.Parameters.AddWithValue("identityId", identityId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ProvisionViaAuthMeAsync(HttpClient client, string jwt)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, "/v1/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
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
                new { clientGuid = Guid.NewGuid(), sequenceNumber = 1, lat = 7.6000, lng = 3.9100, accuracyM = 4.0, recordedAt = now },
                new { clientGuid = Guid.NewGuid(), sequenceNumber = 2, lat = 7.6000, lng = 3.9110, accuracyM = 4.0, recordedAt = now.AddMinutes(1) },
                new { clientGuid = Guid.NewGuid(), sequenceNumber = 3, lat = 7.6010, lng = 3.9110, accuracyM = 4.0, recordedAt = now.AddMinutes(2) },
                new { clientGuid = Guid.NewGuid(), sequenceNumber = 4, lat = 7.6010, lng = 3.9100, accuracyM = 4.0, recordedAt = now.AddMinutes(3) }
            }
        });
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(batchReq)).StatusCode);

        using HttpRequestMessage compReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/complete");
        compReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(compReq)).StatusCode);

        using HttpRequestMessage confReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/confirm");
        confReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
        confReq.Content = JsonContent.Create(new
        {
            name,
            systemType = "open_field",
            growingMediumClass = "soil_based"
        });
        HttpResponseMessage confResp = await client.SendAsync(confReq);
        Assert.Equal(HttpStatusCode.Created, confResp.StatusCode);

        using JsonDocument cDoc = JsonDocument.Parse(await confResp.Content.ReadAsStringAsync());
        return cDoc.RootElement.GetProperty("data").GetProperty("productionUnitId").GetGuid();
    }

    [Fact]
    public async Task Wp70CropProfileSeededAndGovernedWithoutSelfApproval()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid admin1Id = Guid.NewGuid();
        Guid admin2Id = Guid.NewGuid();
        string admin1Jwt = CreateSignedToken(admin1Id);
        string admin2Jwt = CreateSignedToken(admin2Id);

        await ProvisionViaAuthMeAsync(client, admin1Jwt);
        await ProvisionViaAuthMeAsync(client, admin2Jwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantSuperAdminAsync(conn, admin1Id);
        await GrantSuperAdminAsync(conn, admin2Id);

        // 1. Verify all 6 Nigerian pilot crops seeded with source_citation
        HttpResponseMessage catalogResp = await client.GetAsync(new Uri("/v1/crop-profiles", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, catalogResp.StatusCode);
        using (JsonDocument doc = JsonDocument.Parse(await catalogResp.Content.ReadAsStringAsync()))
        {
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.True(data.GetArrayLength() >= 6);
            Assert.Contains(data.EnumerateArray(), c => c.GetProperty("commonName").GetString() == "Maize");
            Assert.Contains(data.EnumerateArray(), c => c.GetProperty("commonName").GetString() == "Cassava");
            Assert.Contains(data.EnumerateArray(), c => c.GetProperty("commonName").GetString() == "Rice (FARO 44)");
        }

        // 2. Admin1 proposes Sorghum via MasterDataChangeRequest (no self-approval)
        string uniqueCropName = $"Sorghum (SAMSORG-{Guid.NewGuid().ToString("N")[..4]})";
        Guid crId;
        using (HttpRequestMessage proposeReq = new(HttpMethod.Post, "/v1/admin/master-data/change-requests"))
        {
            proposeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin1Jwt);
            proposeReq.Content = JsonContent.Create(new
            {
                tableName = "field.crop_profile",
                changeType = "insert",
                diff = new
                {
                    commonName = uniqueCropName,
                    botanicalName = "Sorghum bicolor",
                    category = "cereal",
                    typicalCycleDaysMin = 100,
                    typicalCycleDaysMax = 125,
                    plantingMethod = "dibble",
                    seedDepthCmMin = 2.5m,
                    seedDepthCmMax = 4.0m,
                    rowSpacingCmMin = 60.0m,
                    rowSpacingCmMax = 75.0m,
                    optimalSoilPhMin = 5.5m,
                    optimalSoilPhMax = 7.5m,
                    harvestMoistureContentPct = 12.5m,
                    expectedYieldPerHaKg = 2500.0m,
                    unitOfSale = "bag_100kg",
                    kgPerUnit = 100.0m,
                    sourceCitation = "IAR Zaria Sorghum Extension Manual"
                }
            });
            HttpResponseMessage propResp = await client.SendAsync(proposeReq);
            Assert.Equal(HttpStatusCode.Created, propResp.StatusCode);
            using JsonDocument propDoc = JsonDocument.Parse(await propResp.Content.ReadAsStringAsync());
            crId = propDoc.RootElement.GetProperty("data").GetProperty("changeRequestId").GetGuid();
        }

        // Self-approval fails 403 SELF_APPROVAL_BLOCKED
        using (HttpRequestMessage selfApp = new(HttpMethod.Post, $"/v1/admin/master-data/change-requests/{crId}/approve"))
        {
            selfApp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin1Jwt);
            selfApp.Content = JsonContent.Create(new { reviewNotes = "Self check" });
            HttpResponseMessage selfResp = await client.SendAsync(selfApp);
            Assert.Equal(HttpStatusCode.Forbidden, selfResp.StatusCode);
            using JsonDocument errDoc = JsonDocument.Parse(await selfResp.Content.ReadAsStringAsync());
            Assert.Equal("SELF_APPROVAL_BLOCKED", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // Second admin approves -> inserts CropProfile into live catalog
        using (HttpRequestMessage secondApp = new(HttpMethod.Post, $"/v1/admin/master-data/change-requests/{crId}/approve"))
        {
            secondApp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin2Jwt);
            secondApp.Content = JsonContent.Create(new { reviewNotes = "Verified against IAR Zaria guide" });
            HttpResponseMessage appResp = await client.SendAsync(secondApp);
            Assert.Equal(HttpStatusCode.OK, appResp.StatusCode);
            using JsonDocument appDoc = JsonDocument.Parse(await appResp.Content.ReadAsStringAsync());
            JsonElement data = appDoc.RootElement.GetProperty("data");
            Assert.Equal("applied", data.GetProperty("status").GetString());
            Assert.True(data.GetProperty("appliedRecordId").GetGuid() != Guid.Empty);
        }

        HttpResponseMessage filteredResp = await client.GetAsync(
            new Uri($"/v1/crop-profiles?search={Uri.EscapeDataString(uniqueCropName)}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, filteredResp.StatusCode);
        using (JsonDocument fDoc = JsonDocument.Parse(await filteredResp.Content.ReadAsStringAsync()))
        {
            JsonElement arr = fDoc.RootElement.GetProperty("data");
            Assert.Equal(1, arr.GetArrayLength());
            Assert.Equal(admin2Id, arr[0].GetProperty("reviewedByAdminId").GetGuid());
        }
    }

    [Fact]
    public async Task Wp71CropCycleOpenFieldConcurrencyRaceAndFullForwardLifecycle()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid adminId = Guid.NewGuid();
        Guid farmerId = Guid.NewGuid();
        string adminJwt = CreateSignedToken(adminId);
        string farmerJwt = CreateSignedToken(farmerId);

        await ProvisionViaAuthMeAsync(client, adminJwt);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantSuperAdminAsync(conn, adminId);

        Guid maizeId;
        await using (NpgsqlCommand q = conn.CreateCommand())
        {
            q.CommandText = "SELECT id FROM field.crop_profile WHERE common_name = 'Maize' LIMIT 1;";
            maizeId = (Guid)(await q.ExecuteScalarAsync())!;
        }

        // 1. 50-iteration genuine parallel race against uq_one_active_cycle_per_open_field (BR-A-07)
        RaceSummary race = await ParallelRaceHarness.RunAsync(
            iterations: 50,
            concurrency: 2,
            async iteration =>
            {
                Guid iterFarmerId = Guid.NewGuid();
                string iterFarmerJwt = CreateSignedToken(iterFarmerId);
                Guid racedUnitId = Guid.NewGuid();

                await using NpgsqlCommand seedCmd = conn.CreateCommand();
                seedCmd.CommandText = """
                    INSERT INTO identity.identity (id, first_name, last_name, phone_primary, status)
                    VALUES (@fid, 'Race', @lname, '+2348011112222', 'active');
                    INSERT INTO field.production_unit (
                        id, farm_identity_id, name, system_type, capture_method, growing_medium_class, boundary
                    )
                    VALUES (
                        @uid, @fid, @uname, 'open_field', 'gps_walked_boundary', 'soil_based',
                        ST_GeomFromText('POLYGON((3.9100 7.6000, 3.9110 7.6000, 3.9110 7.6010, 3.9100 7.6010, 3.9100 7.6000))', 4326)::geography
                    );
                    """;
                seedCmd.Parameters.AddWithValue("fid", iterFarmerId);
                seedCmd.Parameters.AddWithValue("lname", $"Farmer{iteration}");
                seedCmd.Parameters.AddWithValue("uid", racedUnitId);
                seedCmd.Parameters.AddWithValue("uname", $"RacePlot-{iteration}");
                await seedCmd.ExecuteNonQueryAsync();

                return async _ =>
                {
                    using HttpRequestMessage req = new(HttpMethod.Post, "/v1/crop-cycles");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", iterFarmerJwt);
                    req.Content = JsonContent.Create(new
                    {
                        productionUnitId = racedUnitId,
                        cropProfileId = maizeId
                    });
                    HttpResponseMessage resp = await client.SendAsync(req);
                    if (resp.StatusCode == HttpStatusCode.Created)
                    {
                        return true;
                    }

                    Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
                    using JsonDocument errDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    Assert.Equal("ACTIVE_CYCLE_EXISTS", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
                    return false;
                };
            });

        Assert.Equal(50, race.TotalWinners);
        Assert.Equal(50, race.TotalConflicts);
        Assert.All(race.WinnersPerIteration, w => Assert.Equal(1, w));

        // 2. Contrast BR-E-06: non-open-field CEA unit allows concurrent active cycles
        await using (NpgsqlCommand getLga = conn.CreateCommand())
        {
            getLga.CommandText = "SELECT id FROM identity.lga LIMIT 1;";
            object? lgaScalar = await getLga.ExecuteScalarAsync();
            if (lgaScalar is not Guid)
            {
                Guid sid = Guid.NewGuid();
                Guid lid = Guid.NewGuid();
                string sfx = sid.ToString("N")[..3];
                await using NpgsqlCommand insLga = conn.CreateCommand();
                insLga.CommandText = """
                    INSERT INTO identity.state (id, name, code) VALUES (@sid, @sn, @sc);
                    INSERT INTO identity.lga (id, state_id, name, code) VALUES (@lid, @sid, @ln, @lc);
                    """;
                insLga.Parameters.AddWithValue("sid", sid);
                insLga.Parameters.AddWithValue("sn", $"S7-{sfx}");
                insLga.Parameters.AddWithValue("sc", $"7{sfx[..2]}");
                insLga.Parameters.AddWithValue("lid", lid);
                insLga.Parameters.AddWithValue("ln", $"L7-{sfx}");
                insLga.Parameters.AddWithValue("lc", $"M{sfx}");
                await insLga.ExecuteNonQueryAsync();
                lgaScalar = lid;
            }
            Guid lgaId = (Guid)lgaScalar;

            using HttpRequestMessage enableSoilless = new(HttpMethod.Put, "/v1/admin/capabilities/cea.soilless");
            enableSoilless.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            enableSoilless.Content = JsonContent.Create(new { enabled = true, scopeType = "global" });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(enableSoilless)).StatusCode);

            using HttpRequestMessage ceaUnitReq = new(HttpMethod.Post, "/v1/production-units");
            ceaUnitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            ceaUnitReq.Content = JsonContent.Create(new
            {
                name = "NFT Concurrent House",
                systemType = "hydroponic_nft",
                growingMediumClass = "soilless",
                captureMethod = "manual_footprint_entry",
                footprintSqm = 100m,
                lgaId
            });
            HttpResponseMessage ceaUnitResp = await client.SendAsync(ceaUnitReq);
            Assert.Equal(HttpStatusCode.Created, ceaUnitResp.StatusCode);
            using JsonDocument cuDoc = JsonDocument.Parse(await ceaUnitResp.Content.ReadAsStringAsync());
            Guid ceaUnitId = cuDoc.RootElement.GetProperty("data").GetProperty("productionUnitId").GetGuid();

            for (int c = 0; c < 2; c++)
            {
                using HttpRequestMessage cycReq = new(HttpMethod.Post, "/v1/crop-cycles");
                cycReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
                cycReq.Content = JsonContent.Create(new { productionUnitId = ceaUnitId, cropProfileId = maizeId });
                Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(cycReq)).StatusCode);
            }

            await using NpgsqlCommand cleanCap = conn.CreateCommand();
            cleanCap.CommandText = "DELETE FROM platform.capability_module_override WHERE module_code = 'cea.soilless';";
            await cleanCap.ExecuteNonQueryAsync();
        }

        // 3. Full forward lifecycle: planning -> pre_planting -> planting -> crop_care -> harvest -> post_harvest -> closed
        Guid lifecycleUnitId = await CreateWalkedOpenFieldUnitAsync(client, farmerJwt, "Full Lifecycle Field");
        DateOnly maturePlantingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-100);
        Guid cycleId;
        using (HttpRequestMessage createCyc = new(HttpMethod.Post, "/v1/crop-cycles"))
        {
            createCyc.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            createCyc.Content = JsonContent.Create(new
            {
                productionUnitId = lifecycleUnitId,
                cropProfileId = maizeId,
                plantingDate = maturePlantingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
            HttpResponseMessage resp = await client.SendAsync(createCyc);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            cycleId = d.RootElement.GetProperty("data").GetProperty("cycleId").GetGuid();
        }

        // Skip attempt planning -> planting rejected 422 INVALID_STAGE_TRANSITION
        using (HttpRequestMessage skipReq = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/advance-stage"))
        {
            skipReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            skipReq.Content = JsonContent.Create(new { targetStage = "planting" });
            HttpResponseMessage skipResp = await client.SendAsync(skipReq);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, skipResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await skipResp.Content.ReadAsStringAsync());
            Assert.Equal("INVALID_STAGE_TRANSITION", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        async Task AdvanceOkAsync()
        {
            using HttpRequestMessage adv = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/advance-stage");
            adv.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            adv.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(adv)).StatusCode);
        }

        async Task LogTaskOkAsync(string taskDefId, object? payload = null)
        {
            using HttpRequestMessage logReq = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/tasks/{taskDefId}/log");
            logReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            logReq.Content = JsonContent.Create(new { data = payload });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(logReq)).StatusCode);
        }

        // planning -> pre_planting
        await AdvanceOkAsync();

        // Missing PRE tasks blocks pre_planting -> planting
        using (HttpRequestMessage blkReq = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/advance-stage"))
        {
            blkReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            blkReq.Content = JsonContent.Create(new { });
            HttpResponseMessage blkResp = await client.SendAsync(blkReq);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blkResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await blkResp.Content.ReadAsStringAsync());
            Assert.Equal("STAGE_GATE_BLOCKED", d.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        await LogTaskOkAsync("PRE-01");
        await LogTaskOkAsync("PRE-06", new { soilPh = 6.2 });
        await AdvanceOkAsync(); // -> planting

        await LogTaskOkAsync("PLT-01", new { seedDepthCm = 4.0 });
        await LogTaskOkAsync("PLT-02", new { rowSpacingCm = 80.0 });
        await AdvanceOkAsync(); // -> crop_care

        await LogTaskOkAsync("CC-01");
        await LogTaskOkAsync("CC-05");
        await LogTaskOkAsync("CC-22");
        await AdvanceOkAsync(); // -> harvest (plantingDate was 100d ago >= 90d min)

        // harvest -> post_harvest blocked until >=1 harvest batch recorded
        await LogTaskOkAsync("HRV-17", new { harvestMoisturePct = 13.0 });
        using (HttpRequestMessage noBatch = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/advance-stage"))
        {
            noBatch.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            noBatch.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.SendAsync(noBatch)).StatusCode);
        }

        Guid batchId;
        using (HttpRequestMessage hbReq = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/harvest-batches"))
        {
            hbReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            hbReq.Content = JsonContent.Create(new { quantityKg = 3200.0m, grade = "A", moisturePct = 13.0m });
            HttpResponseMessage hbResp = await client.SendAsync(hbReq);
            Assert.Equal(HttpStatusCode.Created, hbResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await hbResp.Content.ReadAsStringAsync());
            batchId = d.RootElement.GetProperty("data").GetProperty("harvestBatchId").GetGuid();
        }
        await AdvanceOkAsync(); // -> post_harvest

        await LogTaskOkAsync("PHV-01");
        // post_harvest -> closed blocked while batch disposition is still 'open'
        using (HttpRequestMessage openBatchBlk = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/advance-stage"))
        {
            openBatchBlk.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            openBatchBlk.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.SendAsync(openBatchBlk)).StatusCode);
        }

        using (HttpRequestMessage dispReq = new(HttpMethod.Patch, $"/v1/harvest-batches/{batchId}/disposition"))
        {
            dispReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            dispReq.Content = JsonContent.Create(new { disposition = "sold" });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(dispReq)).StatusCode);
        }
        await AdvanceOkAsync(); // -> closed

        // Rollback & post-closed abandon both refused
        using (HttpRequestMessage rollbackReq = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/advance-stage"))
        {
            rollbackReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            rollbackReq.Content = JsonContent.Create(new { targetStage = "crop_care" });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.SendAsync(rollbackReq)).StatusCode);
        }

        using (HttpRequestMessage abandonClosedReq = new(HttpMethod.Post, $"/v1/crop-cycles/{cycleId}/abandon"))
        {
            abandonClosedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.SendAsync(abandonClosedReq)).StatusCode);
        }

        // Fresh cycle can start after terminal 'closed' and be abandoned cleanly
        using (HttpRequestMessage nextCycReq = new(HttpMethod.Post, "/v1/crop-cycles"))
        {
            nextCycReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            nextCycReq.Content = JsonContent.Create(new { productionUnitId = lifecycleUnitId, cropProfileId = maizeId });
            HttpResponseMessage resp = await client.SendAsync(nextCycReq);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Guid secondCycleId = d.RootElement.GetProperty("data").GetProperty("cycleId").GetGuid();

            using HttpRequestMessage abReq = new(HttpMethod.Post, $"/v1/crop-cycles/{secondCycleId}/abandon");
            abReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(abReq)).StatusCode);
        }
    }

    [Fact]
    public async Task Wp72OperationLogValidationOverrideImmutabilityAndCapabilityExclusion()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid adminId = Guid.NewGuid();
        Guid farmerId = Guid.NewGuid();
        string adminJwt = CreateSignedToken(adminId);
        string farmerJwt = CreateSignedToken(farmerId);

        await ProvisionViaAuthMeAsync(client, adminJwt);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantSuperAdminAsync(conn, adminId);

        Guid maizeId;
        Guid riceId;
        await using (NpgsqlCommand q = conn.CreateCommand())
        {
            q.CommandText = "SELECT id FROM field.crop_profile WHERE common_name = 'Maize' LIMIT 1;";
            maizeId = (Guid)(await q.ExecuteScalarAsync())!;
        }
        await using (NpgsqlCommand q2 = conn.CreateCommand())
        {
            q2.CommandText = "SELECT id FROM field.crop_profile WHERE common_name = 'Rice (FARO 44)' LIMIT 1;";
            riceId = (Guid)(await q2.ExecuteScalarAsync())!;
        }

        Guid riceUnitId = await CreateWalkedOpenFieldUnitAsync(client, farmerJwt, "Rice Plot");
        Guid riceCycleId;
        using (HttpRequestMessage cycReq = new(HttpMethod.Post, "/v1/crop-cycles"))
        {
            cycReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            cycReq.Content = JsonContent.Create(new { productionUnitId = riceUnitId, cropProfileId = riceId });
            using JsonDocument d = JsonDocument.Parse(await (await client.SendAsync(cycReq)).Content.ReadAsStringAsync());
            riceCycleId = d.RootElement.GetProperty("data").GetProperty("cycleId").GetGuid();
        }

        // Advance Rice cycle to pre_planting: PRE-14 (transplant nursery) MUST appear because Rice planting_method == transplant
        using (HttpRequestMessage advRice = new(HttpMethod.Post, $"/v1/crop-cycles/{riceCycleId}/advance-stage"))
        {
            advRice.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            advRice.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(advRice)).StatusCode);
        }

        using (HttpRequestMessage riceTasksReq = new(HttpMethod.Get, $"/v1/crop-cycles/{riceCycleId}/tasks"))
        {
            riceTasksReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            using JsonDocument d = JsonDocument.Parse(await (await client.SendAsync(riceTasksReq)).Content.ReadAsStringAsync());
            JsonElement tasks = d.RootElement.GetProperty("data").GetProperty("tasks");
            Assert.Contains(tasks.EnumerateArray(), t => t.GetProperty("taskDefinitionId").GetString() == "PRE-14");
        }

        // Maize (dibble): PRE-14 omitted, and core.irrigation disabled omits CC-06 cleanly
        Guid maizeUnitId = await CreateWalkedOpenFieldUnitAsync(client, farmerJwt, "Maize Override Plot");
        Guid maizeCycleId;
        using (HttpRequestMessage cycReq = new(HttpMethod.Post, "/v1/crop-cycles"))
        {
            cycReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            cycReq.Content = JsonContent.Create(new { productionUnitId = maizeUnitId, cropProfileId = maizeId });
            using JsonDocument d = JsonDocument.Parse(await (await client.SendAsync(cycReq)).Content.ReadAsStringAsync());
            maizeCycleId = d.RootElement.GetProperty("data").GetProperty("cycleId").GetGuid();
        }

        using (HttpRequestMessage advPre = new(HttpMethod.Post, $"/v1/crop-cycles/{maizeCycleId}/advance-stage"))
        {
            advPre.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            advPre.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(advPre)).StatusCode);
        }

        using (HttpRequestMessage preTasks = new(HttpMethod.Get, $"/v1/crop-cycles/{maizeCycleId}/tasks"))
        {
            preTasks.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            using JsonDocument d = JsonDocument.Parse(await (await client.SendAsync(preTasks)).Content.ReadAsStringAsync());
            JsonElement tasks = d.RootElement.GetProperty("data").GetProperty("tasks");
            Assert.DoesNotContain(tasks.EnumerateArray(), t => t.GetProperty("taskDefinitionId").GetString() == "PRE-14");
        }

        // Complete PRE-01 and log FLAGGED PRE-06 with historical 3-day-old physical loggedAt
        using (HttpRequestMessage logPre01 = new(HttpMethod.Post, $"/v1/crop-cycles/{maizeCycleId}/tasks/PRE-01/log"))
        {
            logPre01.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            logPre01.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(logPre01)).StatusCode);
        }

        DateTimeOffset realPhysicalTime = new(2026, 11, 19, 6, 30, 0, TimeSpan.Zero);
        Guid flagLogId;
        using (HttpRequestMessage logFlag = new(HttpMethod.Post, $"/v1/crop-cycles/{maizeCycleId}/tasks/PRE-06/log"))
        {
            logFlag.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            logFlag.Content = JsonContent.Create(new
            {
                data = new { soilPh = 3.8m },
                loggedAt = realPhysicalTime
            });
            HttpResponseMessage resp = await client.SendAsync(logFlag);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            JsonElement data = d.RootElement.GetProperty("data");
            Assert.Equal("flag", data.GetProperty("status").GetString());
            flagLogId = data.GetProperty("operationLogId").GetGuid();
        }

        // Unresolved flag blocks pre_planting -> planting
        using (HttpRequestMessage blocked = new(HttpMethod.Post, $"/v1/crop-cycles/{maizeCycleId}/advance-stage"))
        {
            blocked.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            blocked.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.SendAsync(blocked)).StatusCode);
        }

        // Override unblocks stage transition while preserving status == 'flag' + historical loggedAt (BR-A-09)
        using (HttpRequestMessage ovReq = new(HttpMethod.Post, $"/v1/operation-logs/{flagLogId}/override"))
        {
            ovReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            ovReq.Content = JsonContent.Create(new { reason = "Applying lime before sowing" });
            HttpResponseMessage ovResp = await client.SendAsync(ovReq);
            Assert.Equal(HttpStatusCode.OK, ovResp.StatusCode);
            using JsonDocument d = JsonDocument.Parse(await ovResp.Content.ReadAsStringAsync());
            Assert.Equal("flag", d.RootElement.GetProperty("data").GetProperty("status").GetString());
        }

        await using (NpgsqlCommand dbVerify = conn.CreateCommand())
        {
            dbVerify.CommandText = "SELECT status::text, override_reason, logged_at FROM field.operation_log WHERE id = @id;";
            dbVerify.Parameters.AddWithValue("id", flagLogId);
            await using NpgsqlDataReader rr = await dbVerify.ExecuteReaderAsync();
            Assert.True(await rr.ReadAsync());
            Assert.Equal("flag", rr.GetString(0));
            Assert.Equal("Applying lime before sowing", rr.GetString(1));
            Assert.Equal(realPhysicalTime, rr.GetFieldValue<DateTimeOffset>(2).ToUniversalTime());
        }

        // DB immutability triggers refuse altering status (23514) or deleting row (42501)
        await using (NpgsqlCommand mutateStatus = conn.CreateCommand())
        {
            mutateStatus.CommandText = "UPDATE field.operation_log SET status = 'ok' WHERE id = @id;";
            mutateStatus.Parameters.AddWithValue("id", flagLogId);
            PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => mutateStatus.ExecuteNonQueryAsync());
            Assert.Equal("23514", ex.SqlState);
        }
        await using (NpgsqlCommand delLog = conn.CreateCommand())
        {
            delLog.CommandText = "DELETE FROM field.operation_log WHERE id = @id;";
            delLog.Parameters.AddWithValue("id", flagLogId);
            PostgresException delEx = await Assert.ThrowsAsync<PostgresException>(() => delLog.ExecuteNonQueryAsync());
            Assert.Equal("42501", delEx.SqlState);
        }

        // Now advance cleanly through planting -> crop_care and verify core.irrigation CC-06 gating
        using (HttpRequestMessage advPlanting = new(HttpMethod.Post, $"/v1/crop-cycles/{maizeCycleId}/advance-stage"))
        {
            advPlanting.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            advPlanting.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(advPlanting)).StatusCode);
        }

        using (HttpRequestMessage p1 = new(HttpMethod.Post, $"/v1/crop-cycles/{maizeCycleId}/tasks/PLT-01/log"))
        {
            p1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            p1.Content = JsonContent.Create(new { data = new { seedDepthCm = 5.3 } }); // warning band
            using JsonDocument d = JsonDocument.Parse(await (await client.SendAsync(p1)).Content.ReadAsStringAsync());
            Assert.Equal("warning", d.RootElement.GetProperty("data").GetProperty("status").GetString());
        }
        using (HttpRequestMessage p2 = new(HttpMethod.Post, $"/v1/crop-cycles/{maizeCycleId}/tasks/PLT-02/log"))
        {
            p2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            p2.Content = JsonContent.Create(new { data = new { rowSpacingCm = 80.0 } });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(p2)).StatusCode);
        }
        using (HttpRequestMessage advCare = new(HttpMethod.Post, $"/v1/crop-cycles/{maizeCycleId}/advance-stage"))
        {
            advCare.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            advCare.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(advCare)).StatusCode);
        }

        // CC-06 excluded while core.irrigation disabled; logging returns 403 CAPABILITY_DISABLED
        using (HttpRequestMessage careTasksOff = new(HttpMethod.Get, $"/v1/crop-cycles/{maizeCycleId}/tasks"))
        {
            careTasksOff.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            using JsonDocument d = JsonDocument.Parse(await (await client.SendAsync(careTasksOff)).Content.ReadAsStringAsync());
            Assert.DoesNotContain(d.RootElement.GetProperty("data").GetProperty("tasks").EnumerateArray(), t => t.GetProperty("taskDefinitionId").GetString() == "CC-06");
        }
        using (HttpRequestMessage cc06Off = new(HttpMethod.Post, $"/v1/crop-cycles/{maizeCycleId}/tasks/CC-06/log"))
        {
            cc06Off.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            cc06Off.Content = JsonContent.Create(new { });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(cc06Off)).StatusCode);
        }

        // Enable core.irrigation -> CC-06 appears in task list
        using (HttpRequestMessage enableIrr = new(HttpMethod.Put, "/v1/admin/capabilities/core.irrigation"))
        {
            enableIrr.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            enableIrr.Content = JsonContent.Create(new { enabled = true, scopeType = "global" });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(enableIrr)).StatusCode);
        }
        using (HttpRequestMessage careTasksOn = new(HttpMethod.Get, $"/v1/crop-cycles/{maizeCycleId}/tasks"))
        {
            careTasksOn.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            using JsonDocument d = JsonDocument.Parse(await (await client.SendAsync(careTasksOn)).Content.ReadAsStringAsync());
            Assert.Contains(d.RootElement.GetProperty("data").GetProperty("tasks").EnumerateArray(), t => t.GetProperty("taskDefinitionId").GetString() == "CC-06");
        }

        await using (NpgsqlCommand cleanIrr = conn.CreateCommand())
        {
            cleanIrr.CommandText = "DELETE FROM platform.capability_module_override WHERE module_code = 'core.irrigation';";
            await cleanIrr.ExecuteNonQueryAsync();
        }
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
