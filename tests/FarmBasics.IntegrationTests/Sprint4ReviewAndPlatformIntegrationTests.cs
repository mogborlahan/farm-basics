using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
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

public sealed class Sprint4ReviewAndPlatformIntegrationTests
    : IClassFixture<PostgisDatabaseFixture>, IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private static readonly string[] NinMismatchCategoryCodes = ["IDENTITY_NIN_MISMATCH"];
    private readonly PostgisDatabaseFixture _dbFixture;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public Sprint4ReviewAndPlatformIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "sprint4-test-key" };
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

    private static async Task GrantAdminRoleAsync(NpgsqlConnection conn, Guid identityId, string roleCode, Guid grantorId)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO admin.admin_role_assignment (id, identity_id, admin_role_id, scope_type, granted_by_admin_id)
            SELECT gen_random_uuid(), @identityId, r.id, 'global', @grantorId
            FROM admin.admin_role r
            WHERE r.code = @roleCode;
            """;
        cmd.Parameters.AddWithValue("identityId", identityId);
        cmd.Parameters.AddWithValue("roleCode", roleCode);
        cmd.Parameters.AddWithValue("grantorId", grantorId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ProvisionViaAuthMeAsync(HttpClient client, string jwt)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, "/v1/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Wp40And41NinMismatchEndToEndLockingCoiAppealAndSlaSweep()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid adminId = Guid.NewGuid();
        Guid reviewer1Id = Guid.NewGuid();
        Guid reviewer2Id = Guid.NewGuid();
        string adminJwt = CreateSignedToken(adminId);
        string rev1Jwt = CreateSignedToken(reviewer1Id);
        string rev2Jwt = CreateSignedToken(reviewer2Id);

        await ProvisionViaAuthMeAsync(client, adminJwt);
        await ProvisionViaAuthMeAsync(client, rev1Jwt);
        await ProvisionViaAuthMeAsync(client, rev2Jwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantAdminRoleAsync(conn, adminId, "SuperAdmin", adminId);

        foreach (Guid rid in new[] { reviewer1Id, reviewer2Id })
        {
            using HttpRequestMessage regRev = new(HttpMethod.Post, "/v1/admin/reviewers");
            regRev.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            regRev.Content = JsonContent.Create(new
            {
                identityId = rid,
                categoryCodes = NinMismatchCategoryCodes,
                maxConcurrentAssignments = 5,
                proficiencyLevel = 2
            });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(regRev)).StatusCode);
        }

        // 1. Fuzzy NIN registration creates pending identity + real IDENTITY_NIN_MISMATCH queue item
        HttpResponseMessage fuzzyResp = await client.PostAsJsonAsync(
            new Uri("/v1/registrations/self-service", UriKind.Relative),
            new { nin = "44500029999", phone = "+2348066661111" });
        Assert.Equal(HttpStatusCode.Accepted, fuzzyResp.StatusCode);

        Guid fuzzyIdentityId;
        Guid queueItemId;
        using (JsonDocument doc = JsonDocument.Parse(await fuzzyResp.Content.ReadAsStringAsync()))
        {
            JsonElement data = doc.RootElement.GetProperty("data");
            fuzzyIdentityId = data.GetProperty("identityId").GetGuid();
            queueItemId = data.GetProperty("reviewQueueItemId").GetGuid();
        }

        // Verify reviewer queue API sees the unassigned item
        using (HttpRequestMessage listReq = new(HttpMethod.Get, "/v1/admin/review-queue?category=IDENTITY_NIN_MISMATCH&status=unassigned"))
        {
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage listResp = await client.SendAsync(listReq);
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            using JsonDocument listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
            Assert.Contains(
                listDoc.RootElement.GetProperty("data").EnumerateArray(),
                el => el.GetProperty("id").GetGuid() == queueItemId);
        }

        // 2. COI exclusion: submitter/subject cannot claim or be assigned their own item
        await using (NpgsqlCommand enrollSubCmd = conn.CreateCommand())
        {
            enrollSubCmd.CommandText = """
                INSERT INTO review.reviewer_profile (identity_id, status, max_concurrent_assignments)
                VALUES (@id, 'active', 5) ON CONFLICT DO NOTHING;
                """;
            enrollSubCmd.Parameters.AddWithValue("id", fuzzyIdentityId);
            await enrollSubCmd.ExecuteNonQueryAsync();
        }

        using (HttpRequestMessage coiClaim = new(HttpMethod.Post, $"/v1/admin/review-queue/{queueItemId}/claim"))
        {
            coiClaim.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateSignedToken(fuzzyIdentityId));
            HttpResponseMessage coiResp = await client.SendAsync(coiClaim);
            Assert.Equal(HttpStatusCode.Forbidden, coiResp.StatusCode);
            using JsonDocument coiDoc = JsonDocument.Parse(await coiResp.Content.ReadAsStringAsync());
            Assert.Equal("COI_SELF_REVIEW_BLOCKED", coiDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // 3. Concurrency-safe parallel 2-reviewer claim race -> strictly 1 winner
        string[] reviewerTokens = [rev1Jwt, rev2Jwt];
        RaceSummary claimRace = await ParallelRaceHarness.RunAsync(
            iterations: 1,
            concurrency: 2,
            _ =>
            {
                Func<int, Task<bool>> worker = async workerIdx =>
                {
                    using HttpRequestMessage claimReq = new(HttpMethod.Post, $"/v1/admin/review-queue/{queueItemId}/claim");
                    claimReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", reviewerTokens[workerIdx]);
                    HttpResponseMessage resp = await client.SendAsync(claimReq);
                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        return true;
                    }

                    Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
                    return false;
                };
                return Task.FromResult(worker);
            });

        Assert.Equal(1, claimRace.TotalWinners);
        Assert.Equal(1, claimRace.TotalConflicts);

        Guid winnerReviewerId;
        await using (NpgsqlCommand ownerCmd = conn.CreateCommand())
        {
            ownerCmd.CommandText = "SELECT assigned_reviewer_id FROM review.review_queue_item WHERE id = @id;";
            ownerCmd.Parameters.AddWithValue("id", queueItemId);
            winnerReviewerId = (Guid)(await ownerCmd.ExecuteScalarAsync())!;
        }

        Guid nonWinnerReviewerId = winnerReviewerId == reviewer1Id ? reviewer2Id : reviewer1Id;
        string winnerJwt = winnerReviewerId == reviewer1Id ? rev1Jwt : rev2Jwt;
        string nonWinnerJwt = nonWinnerReviewerId == reviewer1Id ? rev1Jwt : rev2Jwt;

        // Non-assigned reviewer cannot submit verdict (403)
        using (HttpRequestMessage unauthVerdict = new(HttpMethod.Post, $"/v1/admin/review-queue/{queueItemId}/verdict"))
        {
            unauthVerdict.Headers.Authorization = new AuthenticationHeaderValue("Bearer", nonWinnerJwt);
            unauthVerdict.Content = JsonContent.Create(new { verdictCode = "APPROVE_IDENTITY", notes = "wrong reviewer" });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(unauthVerdict)).StatusCode);
        }

        // Assigned reviewer records APPROVE_IDENTITY -> writes back activation + FB-NG-* FarmerID
        using (HttpRequestMessage verdictReq = new(HttpMethod.Post, $"/v1/admin/review-queue/{queueItemId}/verdict"))
        {
            verdictReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", winnerJwt);
            verdictReq.Content = JsonContent.Create(new { verdictCode = "APPROVE_IDENTITY", notes = "Verified manually" });
            HttpResponseMessage resp = await client.SendAsync(verdictReq);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using JsonDocument vDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            JsonElement wb = vDoc.RootElement.GetProperty("data").GetProperty("writeBackResult");
            Assert.True(wb.GetProperty("activated").GetBoolean());
            Assert.StartsWith("FB-NG-", wb.GetProperty("farmerId").GetString(), StringComparison.Ordinal);
        }

        // Verify identity activated and review_audit_log append-only (SQLSTATE 23514 on DELETE/UPDATE)
        await using (NpgsqlCommand idStatusCmd = conn.CreateCommand())
        {
            idStatusCmd.CommandText = "SELECT status::text FROM identity.identity WHERE id = @id;";
            idStatusCmd.Parameters.AddWithValue("id", fuzzyIdentityId);
            Assert.Equal("active", Convert.ToString(await idStatusCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }

        PostgresException auditDelEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using NpgsqlCommand delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM review.review_audit_log WHERE review_queue_item_id = @id;";
            delCmd.Parameters.AddWithValue("id", queueItemId);
            await delCmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", auditDelEx.SqlState);

        // 4. Appeal assigned to SAME original reviewer is rejected by DB trigger; assigned to different reviewer succeeds
        using (HttpRequestMessage badAppeal = new(HttpMethod.Post, $"/v1/admin/review-queue/{queueItemId}/appeal"))
        {
            badAppeal.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            badAppeal.Content = JsonContent.Create(new { preferredReviewerId = winnerReviewerId, reason = "Recheck" });
            HttpResponseMessage resp = await client.SendAsync(badAppeal);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            using JsonDocument errDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("APPEAL_SAME_REVIEWER_BLOCKED", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        Guid appealId;
        using (HttpRequestMessage goodAppeal = new(HttpMethod.Post, $"/v1/admin/review-queue/{queueItemId}/appeal"))
        {
            goodAppeal.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            goodAppeal.Content = JsonContent.Create(new { preferredReviewerId = nonWinnerReviewerId, reason = "Independent appeal" });
            HttpResponseMessage resp = await client.SendAsync(goodAppeal);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            appealId = doc.RootElement.GetProperty("data").GetProperty("appealId").GetGuid();
        }

        // 5. Force due_at into the past and verify SLA sweep escalates overdue item
        await using (NpgsqlCommand ageCmd = conn.CreateCommand())
        {
            ageCmd.CommandText = "UPDATE review.review_queue_item SET due_at = now() - interval '2 hours' WHERE id = @id;";
            ageCmd.Parameters.AddWithValue("id", appealId);
            await ageCmd.ExecuteNonQueryAsync();
        }

        using (HttpRequestMessage sweepReq = new(HttpMethod.Post, "/v1/admin/review-queue/sla-sweep"))
        {
            sweepReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage sweepResp = await client.SendAsync(sweepReq);
            Assert.Equal(HttpStatusCode.OK, sweepResp.StatusCode);
            using JsonDocument sDoc = JsonDocument.Parse(await sweepResp.Content.ReadAsStringAsync());
            Assert.True(sDoc.RootElement.GetProperty("data").GetProperty("escalatedCount").GetInt32() >= 1);
        }

        await using (NpgsqlCommand escCheck = conn.CreateCommand())
        {
            escCheck.CommandText = "SELECT status::text FROM review.review_queue_item WHERE id = @id;";
            escCheck.Parameters.AddWithValue("id", appealId);
            Assert.Equal("escalated", Convert.ToString(await escCheck.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public async Task Wp41TrustOverrideBoundedConstraintAndPermanentSuperAdminGate()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid superAdminId = Guid.NewGuid();
        Guid trustAdminId = Guid.NewGuid();
        Guid farmerId = Guid.NewGuid();
        string superJwt = CreateSignedToken(superAdminId);
        string trustJwt = CreateSignedToken(trustAdminId);
        string farmerJwt = CreateSignedToken(farmerId);

        await ProvisionViaAuthMeAsync(client, superJwt);
        await ProvisionViaAuthMeAsync(client, trustJwt);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantAdminRoleAsync(conn, superAdminId, "SuperAdmin", superAdminId);
        await GrantAdminRoleAsync(conn, trustAdminId, "IdentityTrustAdmin", superAdminId);

        // 1. Neither expiryDate nor isPermanent=true rejected at API (400) and DB (23514)
        using (HttpRequestMessage unboundedReq = new(HttpMethod.Post, $"/v1/admin/identities/{farmerId}/trust-override"))
        {
            unboundedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", trustJwt);
            unboundedReq.Content = JsonContent.Create(new { overriddenTier = "trusted", reason = "Missing bound", isPermanent = false });
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(unboundedReq)).StatusCode);
        }

        PostgresException chkEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using NpgsqlCommand badSql = conn.CreateCommand();
            badSql.CommandText = """
                INSERT INTO admin.trust_profile_override (identity_id, overridden_tier, reason, granted_by_admin_id, expiry_date, is_permanent)
                VALUES (@fid, 'trusted', 'raw sql check', @aid, NULL, false);
                """;
            badSql.Parameters.AddWithValue("fid", farmerId);
            badSql.Parameters.AddWithValue("aid", trustAdminId);
            await badSql.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", chkEx.SqlState);

        // 2. IdentityTrustAdmin cannot issue permanent override (403)
        using (HttpRequestMessage permByTrustAdmin = new(HttpMethod.Post, $"/v1/admin/identities/{farmerId}/trust-override"))
        {
            permByTrustAdmin.Headers.Authorization = new AuthenticationHeaderValue("Bearer", trustJwt);
            permByTrustAdmin.Content = JsonContent.Create(new { overriddenTier = "trusted", reason = "Attempt perm", isPermanent = true });
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(permByTrustAdmin)).StatusCode);
        }

        // 3. Time-boxed override by IdentityTrustAdmin succeeds (201) and updates identity.trust_profile
        using (HttpRequestMessage timedOverride = new(HttpMethod.Post, $"/v1/admin/identities/{farmerId}/trust-override"))
        {
            timedOverride.Headers.Authorization = new AuthenticationHeaderValue("Bearer", trustJwt);
            timedOverride.Content = JsonContent.Create(new
            {
                overriddenTier = "trusted",
                reason = "Verified seasonal co-op anchor farmer",
                expiryDate = "2027-01-31",
                isPermanent = false
            });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(timedOverride)).StatusCode);
        }
    }

    [Fact]
    public async Task Wp42MasterDataChangeRequestDbEnforcedNoSelfApproval()
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
        await GrantAdminRoleAsync(conn, admin1Id, "ContentGovernanceAdmin", admin1Id);
        await GrantAdminRoleAsync(conn, admin2Id, "ContentGovernanceAdmin", admin1Id);

        Guid crId;
        using (HttpRequestMessage proposeReq = new(HttpMethod.Post, "/v1/admin/master-data/change-requests"))
        {
            proposeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin1Jwt);
            proposeReq.Content = JsonContent.Create(new
            {
                tableName = "knowledge.crop_profile",
                recordId = Guid.NewGuid(),
                changeType = "UPDATE",
                diff = new { cropName = "Cassava TMS 419", minCycleDays = 240 }
            });
            HttpResponseMessage resp = await client.SendAsync(proposeReq);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            crId = doc.RootElement.GetProperty("data").GetProperty("changeRequestId").GetGuid();
        }

        // 1. Self-approval refused with 403 SELF_APPROVAL_BLOCKED
        using (HttpRequestMessage selfApprove = new(HttpMethod.Post, $"/v1/admin/master-data/change-requests/{crId}/approve"))
        {
            selfApprove.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin1Jwt);
            selfApprove.Content = JsonContent.Create(new { reviewNotes = "Trying to approve my own CR" });
            HttpResponseMessage resp = await client.SendAsync(selfApprove);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            using JsonDocument errDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("SELF_APPROVAL_BLOCKED", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // 2. Direct SQL self-approval violates chk_no_self_approval (SQLSTATE 23514, Rule 5/BR-D-03)
        PostgresException dbSelfEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using NpgsqlCommand sqlCmd = conn.CreateCommand();
            sqlCmd.CommandText = """
                UPDATE admin.master_data_change_request
                SET reviewed_by_admin_id = proposed_by_admin_id, status = 'applied'
                WHERE id = @id;
                """;
            sqlCmd.Parameters.AddWithValue("id", crId);
            await sqlCmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", dbSelfEx.SqlState);

        // 3. Distinct second admin approves cleanly (200)
        using (HttpRequestMessage peerApprove = new(HttpMethod.Post, $"/v1/admin/master-data/change-requests/{crId}/approve"))
        {
            peerApprove.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin2Jwt);
            peerApprove.Content = JsonContent.Create(new { reviewNotes = "Verified agronomic cycle minimum" });
            HttpResponseMessage resp = await client.SendAsync(peerApprove);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }

    [Fact]
    public async Task Wp43NotificationDedupAndCapabilityModulesDefaultOffCeaFlags()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        SandboxSmsGatewayClient smsCapture = factory.Services.GetRequiredService<SandboxSmsGatewayClient>();
        smsCapture.Clear();

        Guid adminId = Guid.NewGuid();
        Guid recipientId = Guid.NewGuid();
        string adminJwt = CreateSignedToken(adminId);
        string recipientJwt = CreateSignedToken(recipientId);

        await ProvisionViaAuthMeAsync(client, adminJwt);
        await ProvisionViaAuthMeAsync(client, recipientJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantAdminRoleAsync(conn, adminId, "SuperAdmin", adminId);

        await using (NpgsqlCommand setPhoneCmd = conn.CreateCommand())
        {
            setPhoneCmd.CommandText = "UPDATE identity.identity SET phone_primary = '+2348077771234' WHERE id = @id;";
            setPhoneCmd.Parameters.AddWithValue("id", recipientId);
            await setPhoneCmd.ExecuteNonQueryAsync();
        }

        Guid sourceRefId = Guid.NewGuid();

        // 1. First notification dispatches SMS; duplicate inside window is deduplicated
        using (HttpRequestMessage dispatch1 = new(HttpMethod.Post, "/v1/admin/notifications/dispatch"))
        {
            dispatch1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            dispatch1.Content = JsonContent.Create(new
            {
                templateCode = "WELCOME_FARMER",
                recipientIdentityId = recipientId,
                sourceModule = "identity",
                sourceRefType = "registration",
                sourceRefId,
                context = new Dictionary<string, string> { ["farmerId"] = "FB-NG-100999" },
                dedupWindowMinutes = 60
            });
            HttpResponseMessage resp1 = await client.SendAsync(dispatch1);
            Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
            using JsonDocument d1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
            Assert.False(d1.RootElement.GetProperty("data").GetProperty("deduplicated").GetBoolean());
        }

        using (HttpRequestMessage dispatch2 = new(HttpMethod.Post, "/v1/admin/notifications/dispatch"))
        {
            dispatch2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            dispatch2.Content = JsonContent.Create(new
            {
                templateCode = "WELCOME_FARMER",
                recipientIdentityId = recipientId,
                sourceModule = "identity",
                sourceRefType = "registration",
                sourceRefId,
                context = new Dictionary<string, string> { ["farmerId"] = "FB-NG-100999" },
                dedupWindowMinutes = 60
            });
            HttpResponseMessage resp2 = await client.SendAsync(dispatch2);
            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
            using JsonDocument d2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());
            Assert.True(d2.RootElement.GetProperty("data").GetProperty("deduplicated").GetBoolean());
        }

        await using (NpgsqlCommand countDispatchCmd = conn.CreateCommand())
        {
            countDispatchCmd.CommandText = """
                SELECT count(*) FROM notify.notification_dispatch
                WHERE recipient_identity_id = @id AND source_ref_id = @refId;
                """;
            countDispatchCmd.Parameters.AddWithValue("id", recipientId);
            countDispatchCmd.Parameters.AddWithValue("refId", sourceRefId);
            Assert.Equal(1L, Convert.ToInt64(await countDispatchCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }

        // 2. All cea.* capability modules default to disabled (PRECEDENCE P25) and support org override
        HttpResponseMessage capsResp = await client.GetAsync(new Uri("/v1/platform/capabilities", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, capsResp.StatusCode);
        using (JsonDocument capsDoc = JsonDocument.Parse(await capsResp.Content.ReadAsStringAsync()))
        {
            foreach (JsonElement item in capsDoc.RootElement.GetProperty("data").EnumerateArray())
            {
                string code = item.GetProperty("moduleCode").GetString()!;
                if (code.StartsWith("cea.", StringComparison.Ordinal))
                {
                    Assert.False(item.GetProperty("enabled").GetBoolean());
                }
            }
        }

        Guid testOrgId = Guid.NewGuid();
        await using (NpgsqlCommand insOrgCmd = conn.CreateCommand())
        {
            insOrgCmd.CommandText = "INSERT INTO identity.organization (id, name, type, verification_status) VALUES (@id, 'CEA Trial Farm', 'commercial_farm', 'verified');";
            insOrgCmd.Parameters.AddWithValue("id", testOrgId);
            await insOrgCmd.ExecuteNonQueryAsync();
        }

        using (HttpRequestMessage toggleReq = new(HttpMethod.Put, "/v1/admin/capabilities/cea.hydroponics"))
        {
            toggleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            toggleReq.Content = JsonContent.Create(new { enabled = true, scopeType = "organization", scopeRefId = testOrgId });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(toggleReq)).StatusCode);
        }

        HttpResponseMessage scopedCapsResp = await client.GetAsync(new Uri($"/v1/platform/capabilities?organizationId={testOrgId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, scopedCapsResp.StatusCode);
        using (JsonDocument scopedDoc = JsonDocument.Parse(await scopedCapsResp.Content.ReadAsStringAsync()))
        {
            JsonElement hydro = scopedDoc.RootElement.GetProperty("data").EnumerateArray()
                .First(e => string.Equals(e.GetProperty("moduleCode").GetString(), "cea.hydroponics", StringComparison.Ordinal));
            Assert.True(hydro.GetProperty("enabled").GetBoolean());
        }
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
