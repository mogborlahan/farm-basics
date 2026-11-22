using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

public sealed partial class Sprint3AgentAndFobIntegrationTests
    : IClassFixture<PostgisDatabaseFixture>, IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private readonly PostgisDatabaseFixture _dbFixture;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public Sprint3AgentAndFobIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "sprint3-test-key" };
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

    private static async Task GrantSuperAdminAsync(NpgsqlConnection conn, Guid identityId, Guid grantorId)
    {
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO admin.admin_role_assignment (id, identity_id, admin_role_id, scope_type, granted_by_admin_id)
            SELECT gen_random_uuid(), @identityId, r.id, 'global', @grantorId
            FROM admin.admin_role r
            WHERE r.code = 'SuperAdmin';
            """;
        cmd.Parameters.AddWithValue("identityId", identityId);
        cmd.Parameters.AddWithValue("grantorId", grantorId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ProvisionViaAuthMeAsync(HttpClient client, string jwt)
    {
        using HttpRequestMessage req = new(HttpMethod.Get, "/v1/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        HttpResponseMessage resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
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
        await GrantSuperAdminAsync(conn, adminId, adminId);

        Guid orgId;
        using (HttpRequestMessage createOrg = new(HttpMethod.Post, "/v1/organizations"))
        {
            createOrg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminJwt);
            createOrg.Content = JsonContent.Create(new { name = $"Agro Co-op {Guid.NewGuid():N}", type = "cooperative", registrationNumber = "RC-998877" });
            HttpResponseMessage resp = await client.SendAsync(createOrg);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            orgId = doc.RootElement.GetProperty("data").GetProperty("organizationId").GetGuid();
        }

        using (HttpRequestMessage verifyOrg = new(HttpMethod.Post, $"/v1/admin/organizations/{orgId}/verify"))
        {
            verifyOrg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage resp = await client.SendAsync(verifyOrg);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        using (HttpRequestMessage addAgent = new(HttpMethod.Post, $"/v1/organizations/{orgId}/agents"))
        {
            addAgent.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminJwt);
            addAgent.Content = JsonContent.Create(new { agentIdentityId = agentId });
            HttpResponseMessage resp = await client.SendAsync(addAgent);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
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

    [Fact]
    public async Task Wp33AgentAdditionRejectedBeforeOrganizationVerifiedAndAllowedAfter()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid adminId = Guid.NewGuid();
        Guid orgAdminId = Guid.NewGuid();
        Guid candidateAgentId = Guid.NewGuid();
        string adminJwt = CreateSignedToken(adminId);
        string orgAdminJwt = CreateSignedToken(orgAdminId);
        string agentJwt = CreateSignedToken(candidateAgentId);

        await ProvisionViaAuthMeAsync(client, adminJwt);
        await ProvisionViaAuthMeAsync(client, orgAdminJwt);
        await ProvisionViaAuthMeAsync(client, agentJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantSuperAdminAsync(conn, adminId, adminId);

        // 1. Create pending_verification organization
        Guid orgId;
        using (HttpRequestMessage createOrg = new(HttpMethod.Post, "/v1/organizations"))
        {
            createOrg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminJwt);
            createOrg.Content = JsonContent.Create(new { name = "Unverified Extension Hub", type = "ngo" });
            HttpResponseMessage resp = await client.SendAsync(createOrg);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            orgId = doc.RootElement.GetProperty("data").GetProperty("organizationId").GetGuid();
        }

        // 2. Attempting to add agent before org verification returns 403 AGENT_ORGANIZATION_NOT_VERIFIED (BR-A-03)
        using (HttpRequestMessage blockedAdd = new(HttpMethod.Post, $"/v1/organizations/{orgId}/agents"))
        {
            blockedAdd.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminJwt);
            blockedAdd.Content = JsonContent.Create(new { agentIdentityId = candidateAgentId });
            HttpResponseMessage resp = await client.SendAsync(blockedAdd);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            using JsonDocument errDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("AGENT_ORGANIZATION_NOT_VERIFIED", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // Verify zero agent role_profile created while unverified
        await using (NpgsqlCommand checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT count(*) FROM identity.role_profile WHERE identity_id = @id AND role_type = 'agent';";
            checkCmd.Parameters.AddWithValue("id", candidateAgentId);
            Assert.Equal(0L, Convert.ToInt64(await checkCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }

        // 3. SuperAdmin verifies organization -> onboarding succeeds immediately
        using (HttpRequestMessage verifyReq = new(HttpMethod.Post, $"/v1/admin/organizations/{orgId}/verify"))
        {
            verifyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(verifyReq)).StatusCode);
        }

        using (HttpRequestMessage allowedAdd = new(HttpMethod.Post, $"/v1/organizations/{orgId}/agents"))
        {
            allowedAdd.Headers.Authorization = new AuthenticationHeaderValue("Bearer", orgAdminJwt);
            allowedAdd.Content = JsonContent.Create(new { agentIdentityId = candidateAgentId });
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(allowedAdd)).StatusCode);
        }
    }

    [Fact]
    public async Task Wp30AgentAssistedRegistrationTabletBindingVoiceOtpAndLivenessDiscarded()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        SandboxVoiceOtpGatewayClient voiceCapture = factory.Services.GetRequiredService<SandboxVoiceOtpGatewayClient>();
        voiceCapture.Clear();

        Guid adminId = Guid.NewGuid();
        Guid orgAdminId = Guid.NewGuid();
        Guid agentId = Guid.NewGuid();
        string adminJwt = CreateSignedToken(adminId);
        string orgAdminJwt = CreateSignedToken(orgAdminId);
        string agentJwt = CreateSignedToken(agentId);

        (_, Guid tabletId, Guid sessionId) = await SetupVerifiedOrgAgentAndSessionAsync(
            client,
            adminId,
            adminJwt,
            orgAdminJwt,
            agentId,
            agentJwt,
            $"TAB-SER-{Guid.NewGuid():N}",
            $"FP-{Guid.NewGuid():N}");

        // Session lifetime > 72h rejected (ADR-009 / P2)
        using (HttpRequestMessage badSessReq = new(HttpMethod.Post, "/v1/agents/sessions"))
        {
            badSessReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            badSessReq.Content = JsonContent.Create(new { tabletId, deviceFingerprint = "unused", lifetimeHours = 73 });
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(badSessReq)).StatusCode);
        }

        // Agent-assisted registration with liveness photo (verified then discarded)
        string farmerNin = "91000011234";
        string farmerPhone = "+2348044441234";
        string fakeLivenessBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("SYNTHETIC_LIVENESS_FRAME_01"));

        Guid farmerIdentityId;
        using (HttpRequestMessage regReq = new(HttpMethod.Post, "/v1/registrations/agent-assisted"))
        {
            regReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            regReq.Headers.Add("X-Agent-Session-Id", sessionId.ToString());
            regReq.Content = JsonContent.Create(new
            {
                nin = farmerNin,
                phone = farmerPhone,
                livenessPhotoBase64 = fakeLivenessBase64
            });

            HttpResponseMessage resp = await client.SendAsync(regReq);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            farmerIdentityId = data.GetProperty("identityId").GetGuid();
            Assert.Equal("initiated", data.GetProperty("voiceOtpCallStatus").GetString());
            Assert.True(data.GetProperty("livenessMatched").GetBoolean());
            Assert.True(data.GetProperty("livenessPhotoDiscarded").GetBoolean());
        }

        // Confirm liveness_check_record table contains zero raw photo/base64 columns (Doc 05 §3.3, S3.1.M3)
        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await using (NpgsqlCommand colCmd = conn.CreateCommand())
        {
            colCmd.CommandText = """
                SELECT count(*)
                FROM information_schema.columns
                WHERE table_schema = 'identity'
                  AND table_name = 'liveness_check_record'
                  AND column_name IN ('photo', 'photo_base64', 'liveness_photo', 'image_bytes');
                """;
            Assert.Equal(0L, Convert.ToInt64(await colCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }

        await using (NpgsqlCommand livenessRowCmd = conn.CreateCommand())
        {
            livenessRowCmd.CommandText = """
                SELECT matched AND photo_discarded
                FROM identity.liveness_check_record
                WHERE identity_id = @id;
                """;
            livenessRowCmd.Parameters.AddWithValue("id", farmerIdentityId);
            Assert.True(Convert.ToBoolean(await livenessRowCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }

        // Confirm VoiceOTP relayed by farmer activates account + logs actingAgentId
        VoiceOtpCallRequest voiceCall = voiceCapture.SentCalls.Last(c => c.ToPhoneE164 == farmerPhone);
        using (HttpRequestMessage confirmReq = new(HttpMethod.Post, $"/v1/registrations/agent-assisted/{farmerIdentityId}/confirm-voice-otp"))
        {
            confirmReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            confirmReq.Headers.Add("X-Agent-Session-Id", sessionId.ToString());
            confirmReq.Content = JsonContent.Create(new { otp = voiceCall.OtpCode });
            HttpResponseMessage resp = await client.SendAsync(confirmReq);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal("active", data.GetProperty("status").GetString());
            Assert.StartsWith("FB-NG-", data.GetProperty("farmerId").GetString(), StringComparison.Ordinal);
        }

        // Remote tablet deactivation immediately invalidates the bound tablet & active session
        using (HttpRequestMessage deactivateReq = new(HttpMethod.Post, $"/v1/admin/tablets/{tabletId}/deactivate"))
        {
            deactivateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage resp = await client.SendAsync(deactivateReq);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        using (HttpRequestMessage blockedAfterDeactivate = new(HttpMethod.Post, "/v1/registrations/agent-assisted"))
        {
            blockedAfterDeactivate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            blockedAfterDeactivate.Headers.Add("X-Agent-Session-Id", sessionId.ToString());
            blockedAfterDeactivate.Content = JsonContent.Create(new { nin = "91000012345", phone = "+2348044441235" });
            HttpResponseMessage resp = await client.SendAsync(blockedAfterDeactivate);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            using JsonDocument errDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("TABLET_DEVICE_INACTIVE", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Wp31FobLifecycleIssuancePermanentRevocationAndReissue()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();
        SandboxVoiceOtpGatewayClient voiceCapture = factory.Services.GetRequiredService<SandboxVoiceOtpGatewayClient>();

        Guid adminId = Guid.NewGuid();
        Guid orgAdminId = Guid.NewGuid();
        Guid agentId = Guid.NewGuid();
        Guid farmer2Id = Guid.NewGuid();
        string adminJwt = CreateSignedToken(adminId);
        string orgAdminJwt = CreateSignedToken(orgAdminId);
        string agentJwt = CreateSignedToken(agentId);
        string farmer2Jwt = CreateSignedToken(farmer2Id);

        (_, _, Guid sessionId) = await SetupVerifiedOrgAgentAndSessionAsync(
            client,
            adminId,
            adminJwt,
            orgAdminJwt,
            agentId,
            agentJwt,
            $"TAB-FOB-{Guid.NewGuid():N}",
            $"FP-FOB-{Guid.NewGuid():N}");

        await ProvisionViaAuthMeAsync(client, farmer2Jwt);

        // Register and activate farmer1 via agent
        string farmerPhone = "+2348055551001";
        Guid farmer1Id;
        using (HttpRequestMessage regReq = new(HttpMethod.Post, "/v1/registrations/agent-assisted"))
        {
            regReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            regReq.Headers.Add("X-Agent-Session-Id", sessionId.ToString());
            regReq.Content = JsonContent.Create(new { nin = "92000011001", phone = farmerPhone });
            HttpResponseMessage resp = await client.SendAsync(regReq);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            farmer1Id = doc.RootElement.GetProperty("data").GetProperty("identityId").GetGuid();
        }

        string voiceOtp = voiceCapture.SentCalls.Last(c => c.ToPhoneE164 == farmerPhone).OtpCode;
        using (HttpRequestMessage confirmReq = new(HttpMethod.Post, $"/v1/registrations/agent-assisted/{farmer1Id}/confirm-voice-otp"))
        {
            confirmReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            confirmReq.Content = JsonContent.Create(new { otp = voiceOtp });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(confirmReq)).StatusCode);
        }

        // 1. Issue active fob1
        string fobSerial1 = $"NFC-{Guid.NewGuid():N}";
        Guid fob1Id;
        using (HttpRequestMessage issue1 = new(HttpMethod.Post, "/v1/fobs/issue"))
        {
            issue1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            issue1.Headers.Add("X-Agent-Session-Id", sessionId.ToString());
            issue1.Content = JsonContent.Create(new { identityId = farmer1Id, fobSerial = fobSerial1 });
            HttpResponseMessage resp = await client.SendAsync(issue1);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            fob1Id = doc.RootElement.GetProperty("data").GetProperty("fobId").GetGuid();
        }

        // Duplicate serial returns 409 FOB_SERIAL_ALREADY_BOUND
        using (HttpRequestMessage dupIssue = new(HttpMethod.Post, "/v1/fobs/issue"))
        {
            dupIssue.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            dupIssue.Headers.Add("X-Agent-Session-Id", sessionId.ToString());
            dupIssue.Content = JsonContent.Create(new { identityId = farmer2Id, fobSerial = fobSerial1 });
            HttpResponseMessage resp = await client.SendAsync(dupIssue);
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            using JsonDocument errDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("FOB_SERIAL_ALREADY_BOUND", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // 2. Permanent immediate revocation via report-lost (even by IdentityTrustAdmin/SuperAdmin without confirm step)
        using (HttpRequestMessage lostReq = new(HttpMethod.Post, $"/v1/fobs/{fob1Id}/report-lost"))
        {
            lostReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage resp = await client.SendAsync(lostReq);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("revoked", doc.RootElement.GetProperty("data").GetProperty("status").GetString());
        }

        // Even at raw SQL level, a revoked fob CANNOT return to active (Rule 12 / BR-A-02)
        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        PostgresException dbReactivateEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using NpgsqlCommand reactivateCmd = conn.CreateCommand();
            reactivateCmd.CommandText = "UPDATE identity.fob_credential SET status = 'active', revoked_at = NULL WHERE id = @id;";
            reactivateCmd.Parameters.AddWithValue("id", fob1Id);
            await reactivateCmd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", dbReactivateEx.SqlState);

        // 3. Remote admin reissue without agent tablet session rejected (BR-D-09)
        string fobSerial2 = $"NFC-{Guid.NewGuid():N}";
        using (HttpRequestMessage remoteReissue = new(HttpMethod.Post, "/v1/fobs/reissue"))
        {
            remoteReissue.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            remoteReissue.Content = JsonContent.Create(new { identityId = farmer1Id, fobSerial = fobSerial2, replacesFobId = fob1Id });
            HttpResponseMessage resp = await client.SendAsync(remoteReissue);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            using JsonDocument errDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("AGENT_SESSION_REQUIRED", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // In-person agent reissue succeeds, marks old fob 'replaced', and binds new fob active
        Guid fob2Id;
        using (HttpRequestMessage agentReissue = new(HttpMethod.Post, "/v1/fobs/reissue"))
        {
            agentReissue.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agentJwt);
            agentReissue.Headers.Add("X-Agent-Session-Id", sessionId.ToString());
            agentReissue.Content = JsonContent.Create(new { identityId = farmer1Id, fobSerial = fobSerial2, replacesFobId = fob1Id });
            HttpResponseMessage resp = await client.SendAsync(agentReissue);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            fob2Id = doc.RootElement.GetProperty("data").GetProperty("fobId").GetGuid();
            Assert.Equal(fob1Id, doc.RootElement.GetProperty("data").GetProperty("replacesFobId").GetGuid());
        }

        Assert.NotEqual(fob1Id, fob2Id);
    }

    [Fact]
    public async Task Wp32RegistrationAttestationAuditableAndAppendOnly()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        Guid farmerId = Guid.NewGuid();
        string farmerJwt = CreateSignedToken(farmerId);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        // 1. Verify schema obeys P7 (zero audio/recording columns in registration_attestation)
        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await using (NpgsqlCommand audioCheckCmd = conn.CreateCommand())
        {
            audioCheckCmd.CommandText = """
                SELECT count(*)
                FROM information_schema.columns
                WHERE table_schema = 'identity'
                  AND table_name = 'registration_attestation'
                  AND column_name IN ('voice_recording_url', 'audio_blob', 'audio_url', 'voiceprint');
                """;
            Assert.Equal(0L, Convert.ToInt64(await audioCheckCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }

        // 2. Incomplete teach-back returns 422 ERR_ATTESTATION_INCOMPLETE
        using (HttpRequestMessage incompleteReq = new(HttpMethod.Post, $"/v1/registrations/{farmerId}/attestations"))
        {
            incompleteReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            incompleteReq.Content = JsonContent.Create(new
            {
                summaryVersion = "2026.1",
                pilotConsentVersion = "2026.1",
                languageCode = "ha",
                deliveryMode = "AGENT_SPOKEN_PLUS_LAMINATED_CARD",
                teachBackQ1Passed = true,
                teachBackQ2Passed = false,
                teachBackQ3Passed = true,
                clientRecordedAt = DateTimeOffset.UtcNow
            });

            HttpResponseMessage resp = await client.SendAsync(incompleteReq);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            using JsonDocument errDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal("ERR_ATTESTATION_INCOMPLETE", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        // 3. Complete teach-back persists attestation + audit entry and enforces append-only trigger (SQLSTATE 23514)
        Guid attestationId;
        using (HttpRequestMessage validReq = new(HttpMethod.Post, $"/v1/registrations/{farmerId}/attestations"))
        {
            validReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            validReq.Content = JsonContent.Create(new
            {
                summaryVersion = "2026.1",
                pilotConsentVersion = "2026.1",
                languageCode = "yo",
                deliveryMode = "IVR_PLAYBACK",
                teachBackQ1Passed = true,
                teachBackQ2Passed = true,
                teachBackQ3Passed = true,
                clientRecordedAt = DateTimeOffset.UtcNow
            });

            HttpResponseMessage resp = await client.SendAsync(validReq);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            attestationId = data.GetProperty("attestationId").GetGuid();
            Assert.True(data.GetProperty("teachBackPassed").GetBoolean());
            Assert.Equal("NDPA-2023-MULTI-V1", data.GetProperty("lawfulBasisScheduleRef").GetString());
        }

        using (HttpRequestMessage listReq = new(HttpMethod.Get, $"/v1/registrations/{farmerId}/attestations"))
        {
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            HttpResponseMessage listResp = await client.SendAsync(listReq);
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            using JsonDocument listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
            Assert.Single(listDoc.RootElement.GetProperty("data").EnumerateArray());
        }

        await using (NpgsqlCommand auditCmd = conn.CreateCommand())
        {
            auditCmd.CommandText = """
                SELECT count(*) FROM audit.audit_log_entry
                WHERE entity_id = @attId AND event_type = 'identity.attestation_recorded';
                """;
            auditCmd.Parameters.AddWithValue("attId", attestationId);
            Assert.Equal(1L, Convert.ToInt64(await auditCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }

        PostgresException updEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using NpgsqlCommand upd = conn.CreateCommand();
            upd.CommandText = "UPDATE identity.registration_attestation SET language_code = 'ig' WHERE attestation_id = @id;";
            upd.Parameters.AddWithValue("id", attestationId);
            await upd.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", updEx.SqlState);

        PostgresException delEx = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using NpgsqlCommand del = conn.CreateCommand();
            del.CommandText = "DELETE FROM identity.registration_attestation WHERE attestation_id = @id;";
            del.Parameters.AddWithValue("id", attestationId);
            await del.ExecuteNonQueryAsync();
        });
        Assert.Equal("23514", delEx.SqlState);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
