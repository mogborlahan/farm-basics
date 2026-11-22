using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FarmBasics.Api.ProductionUnits;
using FarmBasics.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace FarmBasics.IntegrationTests;

public sealed class Sprint5ProductionUnitIntegrationTests
    : IClassFixture<PostgisDatabaseFixture>, IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private readonly PostgisDatabaseFixture _dbFixture;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public Sprint5ProductionUnitIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "sprint5-test-key" };
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

    private static async Task<Guid> EnsureLgaAsync(NpgsqlConnection conn)
    {
        await using NpgsqlCommand selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT id FROM identity.lga LIMIT 1;";
        if (await selectCmd.ExecuteScalarAsync() is Guid existingLgaId)
        {
            return existingLgaId;
        }

        Guid stateId = Guid.NewGuid();
        Guid lgaId = Guid.NewGuid();
        string suffix = stateId.ToString("N")[..3];

        await using NpgsqlCommand seedCmd = conn.CreateCommand();
        seedCmd.CommandText = """
            INSERT INTO identity.state (id, name, code)
            VALUES (@stateId, @stateName, @stateCode);
            INSERT INTO identity.lga (id, state_id, name, code)
            VALUES (@lgaId, @stateId, @lgaName, @lgaCode);
            """;
        seedCmd.Parameters.AddWithValue("stateId", stateId);
        seedCmd.Parameters.AddWithValue("stateName", $"TestState-{suffix}");
        seedCmd.Parameters.AddWithValue("stateCode", $"T{suffix[..2]}");
        seedCmd.Parameters.AddWithValue("lgaId", lgaId);
        seedCmd.Parameters.AddWithValue("lgaName", $"TestLga-{suffix}");
        seedCmd.Parameters.AddWithValue("lgaCode", $"L{suffix}");
        await seedCmd.ExecuteNonQueryAsync();

        return lgaId;
    }

    [Fact]
    public async Task Wp50SchemaConstraintsAndDeriveTrigger()
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
        Guid lgaId = await EnsureLgaAsync(conn);

        // 1. Verify all 16 system_type + 11 growing_medium enum values per 02D-ENG §1
        List<string> sysEnums = [];
        await using (NpgsqlCommand sysCmd = conn.CreateCommand())
        {
            sysCmd.CommandText = """
                SELECT e.enumlabel
                FROM pg_enum e
                JOIN pg_type t ON t.oid = e.enumtypid
                WHERE t.typname = 'system_type'
                ORDER BY e.enumsortorder;
                """;
            await using NpgsqlDataReader r = await sysCmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                sysEnums.Add(r.GetString(0));
            }
        }
        Assert.Equal(16, sysEnums.Count);
        Assert.Contains("dutch_bucket", sysEnums);
        Assert.Contains("hydroponic_fodder", sysEnums);
        Assert.Contains("fogponic", sysEnums);

        List<string> medEnums = [];
        await using (NpgsqlCommand medCmd = conn.CreateCommand())
        {
            medCmd.CommandText = """
                SELECT e.enumlabel
                FROM pg_enum e
                JOIN pg_type t ON t.oid = e.enumtypid
                WHERE t.typname = 'growing_medium'
                ORDER BY e.enumsortorder;
                """;
            await using NpgsqlDataReader r = await medCmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                medEnums.Add(r.GetString(0));
            }
        }
        Assert.Equal(11, medEnums.Count);
        Assert.Contains("rice_husk", medEnums);
        Assert.Contains("wood_sawdust", medEnums);

        // 2. Verify cea.walked_units / cea.soilless / cea.solution_advice are OFF by default (403 CAPABILITY_DISABLED)
        using (HttpRequestMessage blockedReq = new(HttpMethod.Post, "/v1/production-units"))
        {
            blockedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            blockedReq.Content = JsonContent.Create(new
            {
                name = "Blocked Pin Greenhouse",
                systemType = "greenhouse",
                growingMediumClass = "soil_based",
                captureMethod = "gps_pin_drop_with_footprint",
                pin = new { lat = 7.4, lng = 3.9, accuracyM = 4.0 },
                footprintSqm = 120.0m,
                lgaId
            });
            HttpResponseMessage blockedResp = await client.SendAsync(blockedReq);
            Assert.Equal(HttpStatusCode.Forbidden, blockedResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await blockedResp.Content.ReadAsStringAsync());
            Assert.Equal("CAPABILITY_DISABLED", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // Enable cea.soilless for constraint tests
        using (HttpRequestMessage enableCmd = new(HttpMethod.Put, "/v1/admin/capabilities/cea.soilless"))
        {
            enableCmd.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            enableCmd.Content = JsonContent.Create(new { enabled = true, scopeType = "global" });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(enableCmd)).StatusCode);
        }

        // 3. chk_capture_shape & chk_medium_class enforcement via API + direct SQL
        using (HttpRequestMessage badShapeReq = new(HttpMethod.Post, "/v1/production-units"))
        {
            badShapeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            badShapeReq.Content = JsonContent.Create(new
            {
                name = "Address Missing LGA",
                systemType = "container_farm",
                growingMediumClass = "soil_based",
                captureMethod = "address_only",
                pin = new { lat = 7.4, lng = 3.9 },
                address = "14 Farm Road, Ibadan"
            });
            HttpResponseMessage badShapeResp = await client.SendAsync(badShapeReq);
            Assert.Equal(HttpStatusCode.BadRequest, badShapeResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await badShapeResp.Content.ReadAsStringAsync());
            Assert.Equal("CAPTURE_SHAPE_INVALID", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        using (HttpRequestMessage badMedReq = new(HttpMethod.Post, "/v1/production-units"))
        {
            badMedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            badMedReq.Content = JsonContent.Create(new
            {
                name = "Impossible Soilless OpenField",
                systemType = "open_field",
                growingMediumClass = "soilless",
                captureMethod = "manual_footprint_entry",
                footprintSqm = 500.0m,
                lgaId
            });
            HttpResponseMessage badMedResp = await client.SendAsync(badMedReq);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, badMedResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await badMedResp.Content.ReadAsStringAsync());
            Assert.Equal("MEDIUM_CLASS_INCOMPATIBLE", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // Direct PostgreSQL check: walked unit without boundary fails chk_capture_shape (SQLSTATE 23514)
        await using (NpgsqlCommand dbShapeFail = conn.CreateCommand())
        {
            dbShapeFail.CommandText = """
                INSERT INTO field.production_unit (
                    farm_identity_id, name, system_type, capture_method, growing_medium_class,
                    area_square_meters, area_hectares, area_acres, area_basis
                )
                VALUES (
                    @farmerId, 'Null Boundary Walked', 'open_field', 'gps_walked_boundary', 'soil_based',
                    1000, 0.1, 0.2471, 'computed_geodesic'
                );
                """;
            dbShapeFail.Parameters.AddWithValue("farmerId", farmerId);
            PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => dbShapeFail.ExecuteNonQueryAsync());
            Assert.Equal("23514", ex.SqlState);
        }

        // Direct PostgreSQL check: trg_pu_derive overwrites client area on walked boundary insert
        Guid derivedUnitId = Guid.NewGuid();
        decimal dbAreaSqm;
        string dbBasis;
        await using (NpgsqlCommand deriveCmd = conn.CreateCommand())
        {
            deriveCmd.CommandText = """
                INSERT INTO field.production_unit (
                    id, farm_identity_id, name, system_type, capture_method, growing_medium_class,
                    boundary, area_square_meters, area_basis
                )
                VALUES (
                    @id, @farmerId, 'Derive Trigger Plot', 'open_field', 'gps_walked_boundary', 'soil_based',
                    ST_GeomFromText('POLYGON((3.9000 7.4000, 3.90090 7.4000, 3.90090 7.40090, 3.9000 7.40090, 3.9000 7.4000))', 4326)::geography,
                    7777777.00, 'declared'
                )
                RETURNING area_square_meters, area_basis::text;
                """;
            deriveCmd.Parameters.AddWithValue("id", derivedUnitId);
            deriveCmd.Parameters.AddWithValue("farmerId", farmerId);
            await using NpgsqlDataReader reader = await deriveCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            dbAreaSqm = reader.GetDecimal(0);
            dbBasis = reader.GetString(1);
        }
        Assert.Equal("computed_geodesic", dbBasis);
        Assert.InRange(dbAreaSqm, 9400m, 10600m);

        // Clean up global cea.soilless override so later tests observe clean default
        await using (NpgsqlCommand cleanupOverride = conn.CreateCommand())
        {
            cleanupOverride.CommandText = "DELETE FROM platform.capability_module_override WHERE module_code = 'cea.soilless';";
            await cleanupOverride.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Wp51And52WalkedBoundaryBatchIdempotencyAndServerPipeline()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid farmerId = Guid.NewGuid();
        Guid otherFarmerId = Guid.NewGuid();
        string farmerJwt = CreateSignedToken(farmerId);
        string otherJwt = CreateSignedToken(otherFarmerId);

        await ProvisionViaAuthMeAsync(client, farmerJwt);
        await ProvisionViaAuthMeAsync(client, otherJwt);

        // 1. Start session
        using HttpRequestMessage startReq = new(HttpMethod.Post, "/v1/production-units/boundary-sessions");
        startReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
        HttpResponseMessage startResp = await client.SendAsync(startReq);
        Assert.Equal(HttpStatusCode.Created, startResp.StatusCode);

        Guid sessionId;
        using (JsonDocument doc = JsonDocument.Parse(await startResp.Content.ReadAsStringAsync()))
        {
            sessionId = doc.RootElement.GetProperty("data").GetProperty("sessionId").GetGuid();
        }

        // 2. Batch points: 4 corners (~200m x 100m = ~2 ha) + 1 low accuracy + 1 teleport outlier spike
        DateTimeOffset day0 = new(2026, 11, 18, 7, 10, 0, TimeSpan.Zero);
        Guid corner1Guid = Guid.NewGuid();
        Guid outlierGuid = Guid.NewGuid();
        Guid lowAccGuid = Guid.NewGuid();

        object[] pointsPayload =
        [
            new { clientGuid = corner1Guid, sequenceNumber = 1, lat = 7.40000, lng = 3.90000, accuracyM = 4.2, recordedAt = day0 },
            new { clientGuid = Guid.NewGuid(), sequenceNumber = 2, lat = 7.40000, lng = 3.90180, accuracyM = 4.0, recordedAt = day0.AddMinutes(3) },
            new { clientGuid = outlierGuid, sequenceNumber = 3, lat = 7.40900, lng = 3.90900, accuracyM = 6.0, recordedAt = day0.AddMinutes(3).AddSeconds(2) },
            new { clientGuid = Guid.NewGuid(), sequenceNumber = 4, lat = 7.40090, lng = 3.90180, accuracyM = 4.1, recordedAt = day0.AddMinutes(5) },
            new { clientGuid = Guid.NewGuid(), sequenceNumber = 5, lat = 7.40090, lng = 3.90000, accuracyM = 3.9, recordedAt = day0.AddMinutes(8) },
            new { clientGuid = lowAccGuid, sequenceNumber = 6, lat = 7.45000, lng = 3.95000, accuracyM = 32.0, recordedAt = day0.AddMinutes(9) }
        ];

        using (HttpRequestMessage batchReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/points/batch"))
        {
            batchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            batchReq.Content = JsonContent.Create(new { points = pointsPayload });
            HttpResponseMessage batchResp = await client.SendAsync(batchReq);
            Assert.Equal(HttpStatusCode.Accepted, batchResp.StatusCode);

            using JsonDocument doc = JsonDocument.Parse(await batchResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal(5, data.GetProperty("accepted").GetInt32());
            Assert.Equal(1, data.GetProperty("rejectedLowAccuracy").GetInt32());
            Assert.Equal(0, data.GetProperty("duplicatesIgnored").GetInt32());
        }

        // Duplicate batch replay is idempotent
        using (HttpRequestMessage replayReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/points/batch"))
        {
            replayReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            replayReq.Content = JsonContent.Create(new { points = pointsPayload });
            HttpResponseMessage replayResp = await client.SendAsync(replayReq);
            Assert.Equal(HttpStatusCode.Accepted, replayResp.StatusCode);

            using JsonDocument doc = JsonDocument.Parse(await replayResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal(0, data.GetProperty("accepted").GetInt32());
            Assert.Equal(6, data.GetProperty("duplicatesIgnored").GetInt32());
        }

        // 3. Complete session: outlier removed, geodesic area computed, original 4-day timestamp preserved
        using (HttpRequestMessage completeReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/complete"))
        {
            completeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            HttpResponseMessage completeResp = await client.SendAsync(completeReq);
            Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode);

            using JsonDocument doc = JsonDocument.Parse(await completeResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal("valid", data.GetProperty("status").GetString());
            Assert.Equal(1, data.GetProperty("outliersRemoved").GetInt32());
            decimal areaSqm = data.GetProperty("areaSquareMeters").GetDecimal();
            Assert.InRange(areaSqm, 18500m, 21200m);
        }

        // Verify raw points (all 6 including outlier & low-accuracy) remain retained and non-deletable
        await using (NpgsqlConnection conn = new(_dbFixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using NpgsqlCommand checkRawCmd = conn.CreateCommand();
            checkRawCmd.CommandText = "SELECT recorded_at FROM field.tracking_point WHERE session_id = @sid AND client_guid = @guid;";
            checkRawCmd.Parameters.AddWithValue("sid", sessionId);
            checkRawCmd.Parameters.AddWithValue("guid", corner1Guid);
            await using (NpgsqlDataReader rawReader = await checkRawCmd.ExecuteReaderAsync())
            {
                Assert.True(await rawReader.ReadAsync());
                DateTimeOffset storedAt = rawReader.GetFieldValue<DateTimeOffset>(0);
                Assert.Equal(day0, storedAt.ToUniversalTime());
            }

            await using NpgsqlCommand delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM field.tracking_point WHERE session_id = @sid;";
            delCmd.Parameters.AddWithValue("sid", sessionId);
            PostgresException delEx = await Assert.ThrowsAsync<PostgresException>(() => delCmd.ExecuteNonQueryAsync());
            Assert.Equal("42501", delEx.SqlState);
        }

        // 4. Confirming walked non-open_field fails while cea.walked_units is disabled
        using (HttpRequestMessage blockedCeaReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/confirm"))
        {
            blockedCeaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            blockedCeaReq.Content = JsonContent.Create(new
            {
                name = "Walked Greenhouse",
                systemType = "greenhouse",
                growingMediumClass = "soil_based"
            });
            HttpResponseMessage blockedCeaResp = await client.SendAsync(blockedCeaReq);
            Assert.Equal(HttpStatusCode.Forbidden, blockedCeaResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await blockedCeaResp.Content.ReadAsStringAsync());
            Assert.Equal("CAPABILITY_DISABLED", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // 5. Confirm open_field passing bogus ClientAreaSqm -> overwritten by server geodesic trigger
        Guid unitId;
        using (HttpRequestMessage confirmReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/confirm"))
        {
            confirmReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            confirmReq.Content = JsonContent.Create(new
            {
                name = "Ibadan Maize Plot A",
                systemType = "open_field",
                growingMediumClass = "soil_based",
                clientAreaSqm = 888888.0m
            });
            HttpResponseMessage confirmResp = await client.SendAsync(confirmReq);
            Assert.Equal(HttpStatusCode.Created, confirmResp.StatusCode);

            using JsonDocument doc = JsonDocument.Parse(await confirmResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            unitId = data.GetProperty("productionUnitId").GetGuid();
            decimal areaSqm = data.GetProperty("areaSquareMeters").GetDecimal();
            Assert.NotEqual(888888.0m, areaSqm);
            Assert.InRange(areaSqm, 18500m, 21200m);
            Assert.Equal("computed_geodesic", data.GetProperty("areaBasis").GetString());
        }

        // 6. PATCH rejects boundary edit (400 BOUNDARY_IMMUTABLE), allows agronomic descriptors, enforces farmer scope
        using (HttpRequestMessage badPatch = new(HttpMethod.Patch, $"/v1/production-units/{unitId}"))
        {
            badPatch.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            badPatch.Content = JsonContent.Create(new { name = "Tamper", areaBasis = "declared" });
            HttpResponseMessage badPatchResp = await client.SendAsync(badPatch);
            Assert.Equal(HttpStatusCode.BadRequest, badPatchResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await badPatchResp.Content.ReadAsStringAsync());
            Assert.Equal("BOUNDARY_IMMUTABLE", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        using (HttpRequestMessage goodPatch = new(HttpMethod.Patch, $"/v1/production-units/{unitId}"))
        {
            goodPatch.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            goodPatch.Content = JsonContent.Create(new
            {
                name = "Ibadan Maize Plot Verified",
                soilType = "sandy_loam",
                drainageClass = "good",
                slopePercent = 2.5m
            });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(goodPatch)).StatusCode);
        }

        using (HttpRequestMessage otherFarmerGet = new(HttpMethod.Get, $"/v1/production-units/{unitId}"))
        {
            otherFarmerGet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherJwt);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(otherFarmerGet)).StatusCode);
        }
    }

    [Fact]
    public async Task Wp54AdversarialGeometrySuiteLargeHighLatSelfIntersectionAndTiny()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid farmerId = Guid.NewGuid();
        string farmerJwt = CreateSignedToken(farmerId);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        async Task<(HttpStatusCode Status, JsonElement Data)> RunWalkCompleteAsync((double Lat, double Lng)[] coords)
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

            DateTimeOffset t0 = DateTimeOffset.UtcNow.AddMinutes(-30);
            object[] pts = coords.Select((c, idx) => (object)new
            {
                clientGuid = Guid.NewGuid(),
                sequenceNumber = idx + 1,
                lat = c.Lat,
                lng = c.Lng,
                accuracyM = 3.5,
                recordedAt = t0.AddMinutes(idx * 5)
            }).ToArray();

            using HttpRequestMessage batchReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/points/batch");
            batchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            batchReq.Content = JsonContent.Create(new { points = pts });
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(batchReq)).StatusCode);

            using HttpRequestMessage completeReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sessionId}/complete");
            completeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            HttpResponseMessage completeResp = await client.SendAsync(completeReq);
            JsonDocument outDoc = JsonDocument.Parse(await completeResp.Content.ReadAsStringAsync());
            JsonElement data = outDoc.RootElement.TryGetProperty("data", out JsonElement d)
                ? d.Clone()
                : outDoc.RootElement.Clone();
            outDoc.Dispose();
            return (completeResp.StatusCode, data);
        }

        // 1. Large polygon (~10km x 10km in Kano, ~12°N) within +-5% of Haversine reference area
        (double Lat, double Lng)[] largeCoords =
        [
            (12.00, 8.50),
            (12.00, 8.59),
            (12.09, 8.59),
            (12.09, 8.50)
        ];
        double largeWidthM = BoundaryGeometryService.HaversineMeters(12.045, 8.50, 12.045, 8.59);
        double largeHeightM = BoundaryGeometryService.HaversineMeters(12.00, 8.545, 12.09, 8.545);
        double largeRefArea = largeWidthM * largeHeightM;

        (HttpStatusCode largeStatus, JsonElement largeData) = await RunWalkCompleteAsync(largeCoords);
        Assert.Equal(HttpStatusCode.OK, largeStatus);
        Assert.Equal("valid", largeData.GetProperty("status").GetString());
        double largeServerArea = (double)largeData.GetProperty("areaSquareMeters").GetDecimal();
        Assert.InRange(largeServerArea, largeRefArea * 0.95, largeRefArea * 1.05);

        // 2. High-latitude polygon (~60°N) within +-5% of geodesic area (naive planar would err ~2x)
        (double Lat, double Lng)[] highLatCoords =
        [
            (60.00, 10.00),
            (60.00, 10.02),
            (60.01, 10.02),
            (60.01, 10.00)
        ];
        double hlWidthM = BoundaryGeometryService.HaversineMeters(60.005, 10.00, 60.005, 10.02);
        double hlHeightM = BoundaryGeometryService.HaversineMeters(60.00, 10.01, 60.01, 10.01);
        double hlRefArea = hlWidthM * hlHeightM;

        (HttpStatusCode hlStatus, JsonElement hlData) = await RunWalkCompleteAsync(highLatCoords);
        Assert.Equal(HttpStatusCode.OK, hlStatus);
        Assert.Equal("valid", hlData.GetProperty("status").GetString());
        double hlServerArea = (double)hlData.GetProperty("areaSquareMeters").GetDecimal();
        Assert.InRange(hlServerArea, hlRefArea * 0.95, hlRefArea * 1.05);

        // 3. Minor self-intersection (< 3% pinched corner) auto-repairs to valid
        (double Lat, double Lng)[] minorPinchCoords =
        [
            (7.40000, 3.90000),
            (7.40000, 3.90200),
            (7.40200, 3.90200),
            (7.40195, 3.90010),
            (7.40192, 3.90030),
            (7.40210, 3.90020),
            (7.40190, 3.90000)
        ];
        (HttpStatusCode pinchStatus, JsonElement pinchData) = await RunWalkCompleteAsync(minorPinchCoords);
        Assert.Equal(HttpStatusCode.OK, pinchStatus);
        Assert.Equal("valid", pinchData.GetProperty("status").GetString());
        Assert.True(pinchData.GetProperty("repaired").GetBoolean());
        Assert.True(pinchData.GetProperty("repairDeltaPct").GetDecimal() < 3.0m);

        // 4. Major figure-eight self-intersection (>= 3%) rejected to review
        (double Lat, double Lng)[] figureEightCoords =
        [
            (7.4000, 3.9000),
            (7.4000, 3.9020),
            (7.4020, 3.9000),
            (7.4020, 3.9020)
        ];
        (HttpStatusCode figStatus, JsonElement figData) = await RunWalkCompleteAsync(figureEightCoords);
        Assert.Equal(HttpStatusCode.OK, figStatus);
        Assert.Equal("review", figData.GetProperty("status").GetString());
        Assert.Equal("major_self_intersection", figData.GetProperty("issue").GetString());

        // 5. Near-zero-area polygon (< 10 sqm) rejected to review
        (double Lat, double Lng)[] tinyCoords =
        [
            (7.400000, 3.900000),
            (7.400000, 3.900009),
            (7.400009, 3.900009),
            (7.400009, 3.900000)
        ];
        (HttpStatusCode tinyStatus, JsonElement tinyData) = await RunWalkCompleteAsync(tinyCoords);
        Assert.Equal(HttpStatusCode.OK, tinyStatus);
        Assert.Equal("review", tinyData.GetProperty("status").GetString());
        Assert.Equal("near_zero_area", tinyData.GetProperty("issue").GetString());
    }

    [Fact]
    public async Task Wp56ConsumerAuditForNullBoundaryAndNullCentroid()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        // Confirm rename completeness: zero legacy /v1/fields endpoints registered
        EndpointDataSource endpointSource = factory.Services.GetRequiredService<EndpointDataSource>();
        List<string?> routePatterns = endpointSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();
        Assert.Contains(routePatterns, r => r is not null && r.StartsWith("/v1/production-units", StringComparison.Ordinal));
        Assert.DoesNotContain(routePatterns, r => r is not null && r.Contains("/v1/fields", StringComparison.Ordinal));

        Guid adminId = Guid.NewGuid();
        Guid farmerId = Guid.NewGuid();
        string adminJwt = CreateSignedToken(adminId);
        string farmerJwt = CreateSignedToken(farmerId);

        await ProvisionViaAuthMeAsync(client, adminJwt);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantSuperAdminAsync(conn, adminId);
        Guid lgaId = await EnsureLgaAsync(conn);

        using (HttpRequestMessage enableSoilless = new(HttpMethod.Put, "/v1/admin/capabilities/cea.soilless"))
        {
            enableSoilless.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            enableSoilless.Content = JsonContent.Create(new { enabled = true, scopeType = "global" });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(enableSoilless)).StatusCode);
        }

        // Create walked unit (polygon + centroid)
        Guid walkedUnitId;
        {
            using HttpRequestMessage startReq = new(HttpMethod.Post, "/v1/production-units/boundary-sessions");
            startReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            using JsonDocument sDoc = JsonDocument.Parse(await (await client.SendAsync(startReq)).Content.ReadAsStringAsync());
            Guid sid = sDoc.RootElement.GetProperty("data").GetProperty("sessionId").GetGuid();

            DateTimeOffset now = DateTimeOffset.UtcNow.AddMinutes(-15);
            using HttpRequestMessage batchReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sid}/points/batch");
            batchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            batchReq.Content = JsonContent.Create(new
            {
                points = new object[]
                {
                    new { clientGuid = Guid.NewGuid(), sequenceNumber = 1, lat = 7.5000, lng = 3.9500, accuracyM = 4.0, recordedAt = now },
                    new { clientGuid = Guid.NewGuid(), sequenceNumber = 2, lat = 7.5000, lng = 3.9510, accuracyM = 4.0, recordedAt = now.AddMinutes(1) },
                    new { clientGuid = Guid.NewGuid(), sequenceNumber = 3, lat = 7.5010, lng = 3.9510, accuracyM = 4.0, recordedAt = now.AddMinutes(2) },
                    new { clientGuid = Guid.NewGuid(), sequenceNumber = 4, lat = 7.5010, lng = 3.9500, accuracyM = 4.0, recordedAt = now.AddMinutes(3) }
                }
            });
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(batchReq)).StatusCode);

            using HttpRequestMessage compReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sid}/complete");
            compReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(compReq)).StatusCode);

            using HttpRequestMessage confReq = new(HttpMethod.Post, $"/v1/production-units/boundary-sessions/{sid}/confirm");
            confReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            confReq.Content = JsonContent.Create(new
            {
                name = "Walked Open Field",
                systemType = "open_field",
                growingMediumClass = "soil_based",
                lgaId
            });
            using JsonDocument cDoc = JsonDocument.Parse(await (await client.SendAsync(confReq)).Content.ReadAsStringAsync());
            walkedUnitId = cDoc.RootElement.GetProperty("data").GetProperty("productionUnitId").GetGuid();
        }

        // Create pin-drop unit (boundary NULL, centroid NOT NULL)
        Guid pinDropUnitId;
        {
            using HttpRequestMessage pinReq = new(HttpMethod.Post, "/v1/production-units");
            pinReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            pinReq.Content = JsonContent.Create(new
            {
                name = "Pin-Drop Soil Greenhouse",
                systemType = "greenhouse",
                growingMediumClass = "soil_based",
                captureMethod = "gps_pin_drop_with_footprint",
                pin = new { lat = 7.5005, lng = 3.9505, accuracyM = 5.0 },
                footprintSqm = 2000.0m,
                lgaId
            });
            HttpResponseMessage pinResp = await client.SendAsync(pinReq);
            Assert.Equal(HttpStatusCode.Created, pinResp.StatusCode);
            using JsonDocument pDoc = JsonDocument.Parse(await pinResp.Content.ReadAsStringAsync());
            pinDropUnitId = pDoc.RootElement.GetProperty("data").GetProperty("productionUnitId").GetGuid();
        }

        // Create manual-footprint unit (boundary NULL, centroid NULL, lga_id NOT NULL)
        Guid manualUnitId;
        {
            using HttpRequestMessage manReq = new(HttpMethod.Post, "/v1/production-units");
            manReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            manReq.Content = JsonContent.Create(new
            {
                name = "Manual Soilless NFT Rack",
                systemType = "hydroponic_nft",
                growingMediumClass = "soilless",
                captureMethod = "manual_footprint_entry",
                footprintSqm = 250.0m,
                lgaId,
                tierCount = (short)4
            });
            HttpResponseMessage manResp = await client.SendAsync(manReq);
            Assert.Equal(HttpStatusCode.Created, manResp.StatusCode);
            using JsonDocument mDoc = JsonDocument.Parse(await manResp.Content.ReadAsStringAsync());
            manualUnitId = mDoc.RootElement.GetProperty("data").GetProperty("productionUnitId").GetGuid();
        }

        // Create address-only unit (boundary NULL, centroid NOT NULL, area NULL)
        Guid addressUnitId;
        {
            using HttpRequestMessage addrReq = new(HttpMethod.Post, "/v1/production-units");
            addrReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            addrReq.Content = JsonContent.Create(new
            {
                name = "Address-Only Container",
                systemType = "container_farm",
                growingMediumClass = "soilless",
                captureMethod = "address_only",
                pin = new { lat = 7.5008, lng = 3.9508 },
                address = "Plot 9 Agritech Crescent",
                lgaId
            });
            HttpResponseMessage addrResp = await client.SendAsync(addrReq);
            Assert.Equal(HttpStatusCode.Created, addrResp.StatusCode);
            using JsonDocument aDoc = JsonDocument.Parse(await addrResp.Content.ReadAsStringAsync());
            addressUnitId = aDoc.RootElement.GetProperty("data").GetProperty("productionUnitId").GetGuid();
        }

        // Exercise every consumer in Doc/02B-ENG §5
        IReadOnlyList<RadiusMatchedUnitDto> nearby = await ProductionUnitConsumerService.FindUnitsWithinDeliveryRadiusAsync(conn, 7.5005, 3.9505, 5000.0, CancellationToken.None);
        Assert.Contains(nearby, u => u.Id == walkedUnitId);
        Assert.Contains(nearby, u => u.Id == pinDropUnitId);
        Assert.Contains(nearby, u => u.Id == addressUnitId);
        Assert.DoesNotContain(nearby, u => u.Id == manualUnitId);

        Assert.NotNull(await ProductionUnitConsumerService.GetCoarseBuyerLocationAsync(conn, walkedUnitId, CancellationToken.None));
        Assert.NotNull(await ProductionUnitConsumerService.GetCoarseBuyerLocationAsync(conn, pinDropUnitId, CancellationToken.None));
        Assert.Null(await ProductionUnitConsumerService.GetCoarseBuyerLocationAsync(conn, manualUnitId, CancellationToken.None));

        await using (NpgsqlTransaction tx = await conn.BeginTransactionAsync())
        {
            // Non-walked units at identical pin NEVER trigger land overlap events (A-3)
            Assert.Empty(await ProductionUnitConsumerService.DetectOverlapsForUnitAsync(conn, tx, pinDropUnitId, CancellationToken.None));
            Assert.Empty(await ProductionUnitConsumerService.DetectOverlapsForUnitAsync(conn, tx, manualUnitId, CancellationToken.None));
            Assert.Empty(await ProductionUnitConsumerService.DetectOverlapsForUnitAsync(conn, tx, addressUnitId, CancellationToken.None));
            await tx.RollbackAsync();
        }

        Assert.True((await ProductionUnitConsumerService.ValidateSoilSamplingEligibilityAsync(conn, walkedUnitId, CancellationToken.None)).Eligible);
        SoilSamplingEligibilityResult pinSoil = await ProductionUnitConsumerService.ValidateSoilSamplingEligibilityAsync(conn, pinDropUnitId, CancellationToken.None);
        Assert.False(pinSoil.Eligible);
        Assert.Equal("BOUNDARY_REQUIRED", pinSoil.ErrorCode);

        Assert.Equal("land_location", await ProductionUnitConsumerService.GetLandHistoryModeAsync(conn, walkedUnitId, CancellationToken.None));
        Assert.Equal("land_location", await ProductionUnitConsumerService.GetLandHistoryModeAsync(conn, pinDropUnitId, CancellationToken.None));
        Assert.Equal("cultivation_history_only", await ProductionUnitConsumerService.GetLandHistoryModeAsync(conn, manualUnitId, CancellationToken.None));

        decimal soillessYield = ProductionUnitConsumerService.ComputeYieldPrefillKg("soilless", 250m, 0.025m, 4, 8.0m, 25000m);
        decimal soilYield = ProductionUnitConsumerService.ComputeYieldPrefillKg("soil_based", 2000m, 0.2m, 1, 8.0m, 25000m);
        Assert.Equal(8000.00m, soillessYield);
        Assert.Equal(5000.00m, soilYield);

        IReadOnlyList<RegionalPestCountDto> regional = await ProductionUnitConsumerService.AggregatePestCountsByRegionAsync(conn, CancellationToken.None);
        Assert.Contains(regional, r => r.LgaId == lgaId && r.UnitCount >= 4);
        Assert.Equal(lgaId, await ProductionUnitConsumerService.ResolveAdminRegionScopeAsync(conn, manualUnitId, CancellationToken.None));

        SeparateAreaTotalsDto separateTotals = await ProductionUnitConsumerService.GetSeparateAreaTotalsAsync(conn, farmerId, CancellationToken.None);
        Assert.True(separateTotals.MeasuredGeodesicHectares > 0m);
        Assert.Equal(0.2250m, separateTotals.DeclaredHectares);

        NullGeometryReconciliationResult rec = await ProductionUnitConsumerService.RunNullGeometryReconciliationAsync(conn, CancellationToken.None);
        Assert.True(rec.UnitsAudited >= 4);
        Assert.Equal(0, rec.IntegrityViolations);

        await using (NpgsqlCommand cleanupOverride = conn.CreateCommand())
        {
            cleanupOverride.CommandText = "DELETE FROM platform.capability_module_override WHERE module_code = 'cea.soilless';";
            await cleanupOverride.ExecuteNonQueryAsync();
        }
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
