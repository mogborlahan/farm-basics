using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Geography;
using FarmBasics.Api.Persistence;
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

public sealed class Sprint1FoundationIntegrationTests : IClassFixture<PostgisDatabaseFixture>, IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private static readonly string[] AdminClaimSpoof = ["SuperAdmin"];
    private readonly PostgisDatabaseFixture _dbFixture;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public Sprint1FoundationIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "sprint1-test-key" };
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
                    ["Auth:RequireHttpsMetadata"] = "false"
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

    private string CreateSignedToken(Guid subjectGuid, IDictionary<string, object>? extraClaims = null)
    {
        SigningCredentials credentials = new(_signingKey, SecurityAlgorithms.RsaSha256);
        DateTime now = DateTime.UtcNow;

        Dictionary<string, object> claims = new()
        {
            ["sub"] = subjectGuid.ToString(),
            ["iss"] = TestIssuer,
            ["aud"] = "farm-basics-api"
        };

        if (extraClaims is not null)
        {
            foreach (KeyValuePair<string, object> kvp in extraClaims)
            {
                claims[kvp.Key] = kvp.Value;
            }
        }

        SecurityTokenDescriptor descriptor = new()
        {
            Subject = new ClaimsIdentity([new Claim("sub", subjectGuid.ToString())]),
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddMinutes(15),
            IssuedAt = now,
            SigningCredentials = credentials,
            Claims = claims
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    [Fact]
    public async Task Wp10NigeriaStatesAndLgasSeededQueryableAndMismatchedSourceBlocked()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage statesResp = await client.GetAsync(new Uri("/v1/geography/states", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, statesResp.StatusCode);

        using JsonDocument statesDoc = JsonDocument.Parse(await statesResp.Content.ReadAsStringAsync());
        JsonElement statesArray = statesDoc.RootElement.GetProperty("data");
        Assert.Equal(37, statesArray.GetArrayLength());

        Guid lagosId = Guid.Empty;
        foreach (JsonElement state in statesArray.EnumerateArray())
        {
            if (string.Equals(state.GetProperty("code").GetString(), "LA", StringComparison.Ordinal))
            {
                lagosId = state.GetProperty("id").GetGuid();
                break;
            }
        }
        Assert.NotEqual(Guid.Empty, lagosId);

        HttpResponseMessage lgasResp = await client.GetAsync(new Uri($"/v1/geography/states/{lagosId}/lgas", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, lgasResp.StatusCode);

        using JsonDocument lgasDoc = JsonDocument.Parse(await lgasResp.Content.ReadAsStringAsync());
        Assert.Contains(
            lgasDoc.RootElement.GetProperty("data").EnumerateArray(),
            l => string.Equals(l.GetProperty("name").GetString(), "Ikeja", StringComparison.Ordinal));

        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_dbFixture.ConnectionString);
        GeographyDataset primary = GeographySeeder.LoadBaselineDataset();
        GeographyDataset mismatchedSecondary = new([new StateSeedEntry("OnlyOne", "OO", [])]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => GeographySeeder.SeedAsync(dataSource, primary, mismatchedSecondary, CancellationToken.None));
    }

    [Fact]
    public async Task Wp11SharedHouseholdPhoneAllowedNoPasswordHashJitAuditedAndParallelNinRace()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        // Ensure schema initialized via /v1/auth/me JIT call
        Guid jitUserId = Guid.NewGuid();
        string jitJwt = CreateSignedToken(jitUserId, new Dictionary<string, object>
        {
            ["given_name"] = "Amina",
            ["family_name"] = "Bello",
            ["phone_number"] = "+2348012345678",
            ["email"] = $"amina.{jitUserId:N}@synthetic.farmbasics.test"
        });

        using (HttpRequestMessage req1 = new(HttpMethod.Get, "/v1/auth/me"))
        {
            req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jitJwt);
            HttpResponseMessage resp1 = await client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
            using JsonDocument doc1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
            Assert.True(doc1.RootElement.GetProperty("data").GetProperty("jitProvisioned").GetBoolean());
        }

        using (HttpRequestMessage req2 = new(HttpMethod.Get, "/v1/auth/me"))
        {
            req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jitJwt);
            HttpResponseMessage resp2 = await client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
            using JsonDocument doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());
            Assert.False(doc2.RootElement.GetProperty("data").GetProperty("jitProvisioned").GetBoolean());
        }

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();

        // Verify password_hash column is absent (PRECEDENCE #16 / ADR-008)
        await using (NpgsqlCommand colCmd = conn.CreateCommand())
        {
            colCmd.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'identity'
                      AND table_name = 'identity'
                      AND column_name = 'password_hash'
                );
                """;
            bool hasPasswordHash = Convert.ToBoolean(await colCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            Assert.False(hasPasswordHash);
        }

        // Verify non-unique index idx_identity_phone exists (PRECEDENCE #15)
        await using (NpgsqlCommand idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = """
                SELECT i.indisunique
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indexrelid
                WHERE c.relname = 'idx_identity_phone';
                """;
            object? isUnique = await idxCmd.ExecuteScalarAsync();
            Assert.True(isUnique is bool u && !u);
        }

        // Verify same-transaction JIT audit entry recorded exactly once
        await using (NpgsqlCommand auditCmd = conn.CreateCommand())
        {
            auditCmd.CommandText = """
                SELECT count(*) FROM audit.audit_log_entry
                WHERE entity_id = @entityId AND event_type = 'identity.jit_provisioned';
                """;
            auditCmd.Parameters.AddWithValue("entityId", jitUserId);
            long auditCount = Convert.ToInt64(await auditCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            Assert.Equal(1L, auditCount);
        }

        // Shared household phone (+2348012345678) allowed across distinct identities
        string sharedPhone = "+2348099988877";
        for (int i = 0; i < 2; i++)
        {
            await using NpgsqlCommand phoneCmd = conn.CreateCommand();
            phoneCmd.CommandText = """
                INSERT INTO identity.identity (id, nin_verification_ref, first_name, last_name, phone_primary, status)
                VALUES (@id, @ninRef, @fn, @ln, @phone, 'active');
                """;
            phoneCmd.Parameters.AddWithValue("id", Guid.NewGuid());
            phoneCmd.Parameters.AddWithValue("ninRef", $"nin-household-{Guid.NewGuid():N}");
            phoneCmd.Parameters.AddWithValue("fn", $"Farmer{i}");
            phoneCmd.Parameters.AddWithValue("ln", "Household");
            phoneCmd.Parameters.AddWithValue("phone", sharedPhone);
            await phoneCmd.ExecuteNonQueryAsync();
        }

        // 50-iteration x 4-thread parallel race on unique nin_verification_ref (BR-A-01, Rule 4)
        await using NpgsqlDataSource raceDs = NpgsqlDataSource.Create(_dbFixture.ConnectionString);
        List<string> raceRefs = [];
        RaceSummary ninRace = await ParallelRaceHarness.RunAsync(
            iterations: 50,
            concurrency: 4,
            iteration =>
            {
                string ninRef = $"nin-race-{iteration}-{Guid.NewGuid():N}";
                raceRefs.Add(ninRef);

                Func<int, Task<bool>> worker = async _ =>
                {
                    await using NpgsqlConnection wConn = await raceDs.OpenConnectionAsync();
                    await using NpgsqlCommand insertCmd = wConn.CreateCommand();
                    insertCmd.CommandText = """
                        INSERT INTO identity.identity (id, nin_verification_ref, first_name, last_name, phone_primary, status)
                        VALUES (@id, @ninRef, 'Race', 'Farmer', '+2348000000000', 'active');
                        """;
                    insertCmd.Parameters.AddWithValue("id", Guid.NewGuid());
                    insertCmd.Parameters.AddWithValue("ninRef", ninRef);
                    try
                    {
                        await insertCmd.ExecuteNonQueryAsync();
                        return true;
                    }
                    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
                    {
                        return false;
                    }
                };
                return Task.FromResult(worker);
            });

        Assert.Equal(50, ninRace.TotalWinners);
        Assert.Equal(150, ninRace.TotalConflicts);
        Assert.All(ninRace.WinnersPerIteration, w => Assert.Equal(1, w));
    }

    [Fact]
    public async Task Wp11FobReplacementAllowedRevokedFobIrrecoverableAndJitEmailConflictSafe()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid farmerId = Guid.NewGuid();
        Guid agentId = Guid.NewGuid();
        string sharedEmail = $"shared.{farmerId:N}@synthetic.farmbasics.test";

        string farmerJwt = CreateSignedToken(farmerId, new Dictionary<string, object>
        {
            ["given_name"] = "Ibrahim",
            ["family_name"] = "Musa",
            ["email"] = sharedEmail
        });
        string agentJwt = CreateSignedToken(agentId, new Dictionary<string, object>
        {
            ["given_name"] = "Kemi",
            ["family_name"] = "Ade",
            ["email"] = sharedEmail
        });

        foreach (string jwt in new[] { farmerJwt, agentJwt })
        {
            using HttpRequestMessage meReq = new(HttpMethod.Get, "/v1/auth/me");
            meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            HttpResponseMessage meResp = await client.SendAsync(meReq);
            Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

            using JsonDocument doc = JsonDocument.Parse(await meResp.Content.ReadAsStringAsync());
            Assert.True(doc.RootElement.GetProperty("data").GetProperty("jitProvisioned").GetBoolean());
        }

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();

        // Second identity sharing the same email falls back to NULL email_primary without throwing 23505
        await using (NpgsqlCommand emailCmd = conn.CreateCommand())
        {
            emailCmd.CommandText = "SELECT email_primary FROM identity.identity WHERE id = @id;";
            emailCmd.Parameters.AddWithValue("id", agentId);
            object? secondEmail = await emailCmd.ExecuteScalarAsync();
            Assert.Equal(DBNull.Value, secondEmail);
        }

        // Issue active fob, revoke it, verify active replacement fob referencing old fob succeeds
        Guid oldFobId = Guid.NewGuid();
        Guid replacementFobId = Guid.NewGuid();

        await using (NpgsqlCommand issueCmd = conn.CreateCommand())
        {
            issueCmd.CommandText = """
                INSERT INTO identity.fob_credential (id, fob_serial, farmer_identity_id, status, issued_by_agent_id)
                VALUES (@id, @serial, @farmerId, 'active', @agentId);
                """;
            issueCmd.Parameters.AddWithValue("id", oldFobId);
            issueCmd.Parameters.AddWithValue("serial", $"FOB-OLD-{oldFobId:N}");
            issueCmd.Parameters.AddWithValue("farmerId", farmerId);
            issueCmd.Parameters.AddWithValue("agentId", agentId);
            await issueCmd.ExecuteNonQueryAsync();
        }

        await using (NpgsqlCommand revokeFobCmd = conn.CreateCommand())
        {
            revokeFobCmd.CommandText = """
                UPDATE identity.fob_credential
                SET status = 'revoked', revoked_at = now()
                WHERE id = @id;
                """;
            revokeFobCmd.Parameters.AddWithValue("id", oldFobId);
            await revokeFobCmd.ExecuteNonQueryAsync();
        }

        await using (NpgsqlCommand replaceCmd = conn.CreateCommand())
        {
            replaceCmd.CommandText = """
                INSERT INTO identity.fob_credential (id, fob_serial, farmer_identity_id, status, issued_by_agent_id, replaces_fob_id)
                VALUES (@id, @serial, @farmerId, 'active', @agentId, @replacesId);
                """;
            replaceCmd.Parameters.AddWithValue("id", replacementFobId);
            replaceCmd.Parameters.AddWithValue("serial", $"FOB-NEW-{replacementFobId:N}");
            replaceCmd.Parameters.AddWithValue("farmerId", farmerId);
            replaceCmd.Parameters.AddWithValue("agentId", agentId);
            replaceCmd.Parameters.AddWithValue("replacesId", oldFobId);
            await replaceCmd.ExecuteNonQueryAsync();
        }

        // Reactivating a revoked fob must fail closed at DB level with SQLSTATE 23514 (Rule 12 / BR-A-02)
        PostgresException reactivateEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using NpgsqlCommand reactivateCmd = conn.CreateCommand();
            reactivateCmd.CommandText = """
                UPDATE identity.fob_credential
                SET status = 'active', revoked_at = NULL
                WHERE id = @id;
                """;
            reactivateCmd.Parameters.AddWithValue("id", oldFobId);
            await reactivateCmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", reactivateEx.SqlState);
    }

    [Fact]
    public async Task Wp12RevokedAdminLosesAccessOnVeryNextRequestEvenWithValidJwt()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        // Trigger migration + JIT provision grantor and candidate admin
        Guid grantorId = Guid.NewGuid();
        Guid adminUserId = Guid.NewGuid();
        string grantorJwt = CreateSignedToken(grantorId);
        string adminJwt = CreateSignedToken(adminUserId, new Dictionary<string, object> { ["roles"] = AdminClaimSpoof });

        foreach (string jwt in new[] { grantorJwt, adminJwt })
        {
            using HttpRequestMessage meReq = new(HttpMethod.Get, "/v1/auth/me");
            meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            HttpResponseMessage meResp = await client.SendAsync(meReq);
            Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);
        }

        // Spoofed JWT roles must fail closed before DB assignment
        using (HttpRequestMessage preAssignPing = new(HttpMethod.Get, "/v1/admin/ping"))
        {
            preAssignPing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage resp = await client.SendAsync(preAssignPing);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();

        Guid assignmentId = Guid.NewGuid();
        await using (NpgsqlCommand grantCmd = conn.CreateCommand())
        {
            grantCmd.CommandText = """
                INSERT INTO admin.admin_role_assignment (id, identity_id, admin_role_id, scope_type, granted_by_admin_id)
                SELECT @id, @identityId, r.id, 'global', @grantorId
                FROM admin.admin_role r
                WHERE r.code = 'SuperAdmin';
                """;
            grantCmd.Parameters.AddWithValue("id", assignmentId);
            grantCmd.Parameters.AddWithValue("identityId", adminUserId);
            grantCmd.Parameters.AddWithValue("grantorId", grantorId);
            await grantCmd.ExecuteNonQueryAsync();
        }

        // Active assignment grants access immediately
        using (HttpRequestMessage activePing = new(HttpMethod.Get, "/v1/admin/ping"))
        {
            activePing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage resp = await client.SendAsync(activePing);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // Suspended identity fails closed immediately even while admin_role_assignment.revoked_at IS NULL
        await using (NpgsqlCommand suspendCmd = conn.CreateCommand())
        {
            suspendCmd.CommandText = "UPDATE identity.identity SET status = 'suspended' WHERE id = @identityId;";
            suspendCmd.Parameters.AddWithValue("identityId", adminUserId);
            await suspendCmd.ExecuteNonQueryAsync();
        }

        using (HttpRequestMessage suspendedPing = new(HttpMethod.Get, "/v1/admin/ping"))
        {
            suspendedPing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage resp = await client.SendAsync(suspendedPing);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("PERMISSION_DENIED", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        await using (NpgsqlCommand restoreCmd = conn.CreateCommand())
        {
            restoreCmd.CommandText = "UPDATE identity.identity SET status = 'active' WHERE id = @identityId;";
            restoreCmd.Parameters.AddWithValue("identityId", adminUserId);
            await restoreCmd.ExecuteNonQueryAsync();
        }

        using (HttpRequestMessage restoredPing = new(HttpMethod.Get, "/v1/admin/ping"))
        {
            restoredPing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage resp = await client.SendAsync(restoredPing);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // Revoke assignment and replay the EXACT SAME unexpired JWT immediately
        await using (NpgsqlCommand revokeCmd = conn.CreateCommand())
        {
            revokeCmd.CommandText = """
                UPDATE admin.admin_role_assignment
                SET revoked_at = now(), revoked_by_admin_id = @grantorId
                WHERE id = @id;
                """;
            revokeCmd.Parameters.AddWithValue("id", assignmentId);
            revokeCmd.Parameters.AddWithValue("grantorId", grantorId);
            await revokeCmd.ExecuteNonQueryAsync();
        }

        using (HttpRequestMessage afterRevokePing = new(HttpMethod.Get, "/v1/admin/ping"))
        {
            afterRevokePing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage resp = await client.SendAsync(afterRevokePing);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("PERMISSION_DENIED", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Wp13AuditPartitionedAppendOnlyEnforcedAndRolledBackTxWritesZeroAuditRows()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        // Ensure schema migrated via geography probe
        HttpResponseMessage probe = await client.GetAsync(new Uri("/v1/geography/states", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();

        // Confirm audit.audit_log_entry is partitioned (relkind='p') and AuditPartitionManager adds future partitions
        await using (NpgsqlCommand relCmd = conn.CreateCommand())
        {
            relCmd.CommandText = """
                SELECT c.relkind::text
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'audit' AND c.relname = 'audit_log_entry';
                """;
            Assert.Equal("p", Convert.ToString(await relCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }

        DateTimeOffset futureReference = DateTimeOffset.UtcNow.AddMonths(4);
        IReadOnlyList<string> createdFuture = await AuditPartitionManager.EnsureMonthlyPartitionsAsync(
            conn,
            futureReference,
            monthsAhead: 2,
            CancellationToken.None);
        Assert.Equal(3, createdFuture.Count);

        // Write an audit record inside a committed transaction, then verify UPDATE & DELETE raise SQLSTATE 23514 (Rule 7)
        Guid entityId = Guid.NewGuid();
        Guid auditId;
        await using (NpgsqlTransaction tx = await conn.BeginTransactionAsync())
        {
            auditId = await AuditWriter.WriteAsync(
                conn,
                tx,
                new AuditRecord("sprint1_test", "audit.append_only_check", "test_entity", entityId, null, "{\"ok\":true}"));
            await tx.CommitAsync();
        }

        PostgresException updateEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using NpgsqlCommand upd = conn.CreateCommand();
            upd.CommandText = "UPDATE audit.audit_log_entry SET event_type = 'tampered' WHERE id = @id;";
            upd.Parameters.AddWithValue("id", auditId);
            await upd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", updateEx.SqlState);

        PostgresException deleteEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using NpgsqlCommand del = conn.CreateCommand();
            del.CommandText = "DELETE FROM audit.audit_log_entry WHERE id = @id;";
            del.Parameters.AddWithValue("id", auditId);
            await del.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", deleteEx.SqlState);

        // Verify an audit entry written in a rolled-back transaction leaves zero audit rows (01A §6.2)
        Guid rolledBackEntityId = Guid.NewGuid();
        await using (NpgsqlTransaction rolledTx = await conn.BeginTransactionAsync())
        {
            await AuditWriter.WriteAsync(
                conn,
                rolledTx,
                new AuditRecord("sprint1_test", "audit.rollback_check", "test_entity", rolledBackEntityId));
            await rolledTx.RollbackAsync();
        }

        await using (NpgsqlCommand countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT count(*) FROM audit.audit_log_entry WHERE entity_id = @entityId;";
            countCmd.Parameters.AddWithValue("entityId", rolledBackEntityId);
            long count = Convert.ToInt64(await countCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            Assert.Equal(0L, count);
        }
    }

    [Fact]
    public async Task Wp14IdempotencyFilterUniform404EnvelopeAndAuditSearch30PerMinLimit()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        // 1. Unknown route returns uniform ApiErrorResponse JSON on 404
        HttpResponseMessage notFoundResp = await client.GetAsync(new Uri("/v1/nonexistent-sprint1-route", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        using (JsonDocument nfDoc = JsonDocument.Parse(await notFoundResp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("RESOURCE_NOT_FOUND", nfDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(nfDoc.RootElement.GetProperty("meta").GetProperty("requestId").GetString()));
        }

        // Provision AuditComplianceAdmin user + SuperAdmin grantor
        Guid grantorId = Guid.NewGuid();
        Guid auditorId = Guid.NewGuid();
        string grantorJwt = CreateSignedToken(grantorId);
        string auditorJwt = CreateSignedToken(auditorId);

        foreach (string jwt in new[] { grantorJwt, auditorJwt })
        {
            using HttpRequestMessage meReq = new(HttpMethod.Get, "/v1/auth/me");
            meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(meReq)).StatusCode);
        }

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await using (NpgsqlCommand grantCmd = conn.CreateCommand())
        {
            grantCmd.CommandText = """
                INSERT INTO admin.admin_role_assignment (id, identity_id, admin_role_id, scope_type, granted_by_admin_id)
                SELECT gen_random_uuid(), @identityId, r.id, 'global', @grantorId
                FROM admin.admin_role r
                WHERE r.code = 'AuditComplianceAdmin';
                """;
            grantCmd.Parameters.AddWithValue("identityId", auditorId);
            grantCmd.Parameters.AddWithValue("grantorId", grantorId);
            await grantCmd.ExecuteNonQueryAsync();
        }

        // 2. Reusable RequireIdempotency filter on POST /v1/admin/audit/partitions/ensure
        using (HttpRequestMessage noKeyReq = new(HttpMethod.Post, "/v1/admin/audit/partitions/ensure"))
        {
            noKeyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auditorJwt);
            noKeyReq.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            HttpResponseMessage noKeyResp = await client.SendAsync(noKeyReq);
            Assert.Equal(HttpStatusCode.BadRequest, noKeyResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await noKeyResp.Content.ReadAsStringAsync());
            Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        Guid idemKey = Guid.NewGuid();
        string firstBody;
        using (HttpRequestMessage req1 = new(HttpMethod.Post, "/v1/admin/audit/partitions/ensure?monthsAhead=3"))
        {
            req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auditorJwt);
            req1.Headers.Add("Idempotency-Key", idemKey.ToString());
            req1.Content = new StringContent("{\"reason\":\"monthly-check\"}", Encoding.UTF8, "application/json");
            HttpResponseMessage resp1 = await client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
            firstBody = await resp1.Content.ReadAsStringAsync();
        }

        using (HttpRequestMessage reqReplay = new(HttpMethod.Post, "/v1/admin/audit/partitions/ensure?monthsAhead=3"))
        {
            reqReplay.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auditorJwt);
            reqReplay.Headers.Add("Idempotency-Key", idemKey.ToString());
            reqReplay.Content = new StringContent("{\"reason\":\"monthly-check\"}", Encoding.UTF8, "application/json");
            HttpResponseMessage respReplay = await client.SendAsync(reqReplay);
            Assert.Equal(HttpStatusCode.OK, respReplay.StatusCode);
            string replayBody = await respReplay.Content.ReadAsStringAsync();
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(firstBody),
                System.Text.Json.Nodes.JsonNode.Parse(replayBody)));
        }

        using (HttpRequestMessage reqTampered = new(HttpMethod.Post, "/v1/admin/audit/partitions/ensure?monthsAhead=3"))
        {
            reqTampered.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auditorJwt);
            reqTampered.Headers.Add("Idempotency-Key", idemKey.ToString());
            reqTampered.Content = new StringContent("{\"reason\":\"tampered-body\"}", Encoding.UTF8, "application/json");
            HttpResponseMessage respTampered = await client.SendAsync(reqTampered);
            Assert.Equal(HttpStatusCode.Conflict, respTampered.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await respTampered.Content.ReadAsStringAsync());
            Assert.Equal("IDEMPOTENCY_KEY_REUSED", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // Only 1 audit.partitions_ensured record persisted despite 3 HTTP calls
        await using (NpgsqlCommand auditCountCmd = conn.CreateCommand())
        {
            auditCountCmd.CommandText = """
                SELECT count(*) FROM audit.audit_log_entry
                WHERE actor_id = @actorId AND event_type = 'audit.partitions_ensured';
                """;
            auditCountCmd.Parameters.AddWithValue("actorId", auditorId);
            long cnt = Convert.ToInt64(await auditCountCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            Assert.Equal(1L, cnt);
        }

        // 3. Verify GET /v1/admin/audit/search enforces 30 requests/minute per admin (Doc 03 §15)
        for (int attempt = 1; attempt <= 31; attempt++)
        {
            using HttpRequestMessage searchReq = new(HttpMethod.Get, "/v1/admin/audit/search?limit=5");
            searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auditorJwt);
            HttpResponseMessage searchResp = await client.SendAsync(searchReq);

            if (attempt <= 30)
            {
                Assert.Equal(HttpStatusCode.OK, searchResp.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, searchResp.StatusCode);
                using JsonDocument rlDoc = JsonDocument.Parse(await searchResp.Content.ReadAsStringAsync());
                Assert.Equal("RATE_LIMIT_EXCEEDED", rlDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
            }
        }
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
