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

public sealed class SyncSpikeIdempotencyIntegrationTests : IClassFixture<PostgisDatabaseFixture>, IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private readonly PostgisDatabaseFixture _dbFixture;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public SyncSpikeIdempotencyIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "sync-spike-test-key" };
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

    private string CreateSignedToken(Guid subjectGuid)
    {
        string subject = subjectGuid.ToString();
        SigningCredentials credentials = new(_signingKey, SecurityAlgorithms.RsaSha256);
        DateTime now = DateTime.UtcNow;

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = TestIssuer,
            Audience = "farm-basics-api",
            Subject = new ClaimsIdentity([new Claim("sub", subject)]),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = subject,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
            },
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddMinutes(10),
            SigningCredentials = credentials
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    [Fact]
    public async Task SyncSpikeEnforcesIdempotentTripleReplayTimestampPreservationAndKeyReuseRejection()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        // Missing Idempotency-Key returns 400 IDEMPOTENCY_KEY_REQUIRED (03-ENG §1.5)
        Guid orphanClientGuid = Guid.NewGuid();
        HttpResponseMessage missingKeyResponse = await client.PostAsJsonAsync(
            "/v1/sync/spike-records",
            new
            {
                clientGuid = orphanClientGuid,
                entityType = "operation_log",
                payloadJson = "{\"note\":\"missing header\"}",
                clientRecordedAt = DateTimeOffset.UtcNow
            });
        Assert.Equal(HttpStatusCode.BadRequest, missingKeyResponse.StatusCode);

        // Initial offline-recorded sync (anonymous identity_id IS NULL) + 2 identical replays -> exactly 1 server row
        Guid idempotencyKey = Guid.NewGuid();
        Guid clientGuid = Guid.NewGuid();
        DateTimeOffset clientRecordedAt = DateTimeOffset.UtcNow.AddHours(-72);
        string requestBody = JsonSerializer.Serialize(new
        {
            clientGuid,
            entityType = "operation_log",
            payloadJson = "{\"task\":\"fertilizer_topdress\",\"lga\":\"KN-KMC\"}",
            clientRecordedAt
        });

        string? firstRecordId = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpRequestMessage req = new(HttpMethod.Post, "/v1/sync/spike-records");
            req.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
            req.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            HttpResponseMessage resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

            using JsonDocument json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            string recordId = json.RootElement.GetProperty("data").GetProperty("recordId").GetString()!;
            if (firstRecordId is null)
            {
                firstRecordId = recordId;
            }
            else
            {
                Assert.Equal(firstRecordId, recordId);
            }
        }

        // Verify PostgreSQL row count == 1, client_recorded_at preserved, and NULLS NOT DISTINCT present in pg_index
        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();

        await using (NpgsqlCommand checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = """
                SELECT count(*), min(client_recorded_at), min(server_received_at)
                FROM platform.sync_spike_records
                WHERE client_guid = @cg;
                """;
            checkCmd.Parameters.AddWithValue("cg", clientGuid);

            await using NpgsqlDataReader reader = await checkCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));

            DateTimeOffset storedClientAt = reader.GetFieldValue<DateTimeOffset>(1);
            DateTimeOffset storedServerAt = reader.GetFieldValue<DateTimeOffset>(2);
            Assert.True(Math.Abs((storedClientAt - clientRecordedAt).TotalSeconds) < 1.0);
            Assert.True((storedServerAt - storedClientAt).TotalHours > 70.0);
        }

        await using (NpgsqlCommand idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = """
                SELECT i.indnullsnotdistinct
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indexrelid
                WHERE c.relname = 'uq_idempotency_key_per_identity_route';
                """;
            object? nullsNotDistinct = await idxCmd.ExecuteScalarAsync();
            Assert.True(nullsNotDistinct is bool b && b);
        }

        // Reusing the same Idempotency-Key with a DIFFERENT payload returns 409 IDEMPOTENCY_KEY_REUSED
        string tamperedBody = JsonSerializer.Serialize(new
        {
            clientGuid,
            entityType = "operation_log",
            payloadJson = "{\"task\":\"tampered_payload\"}",
            clientRecordedAt
        });
        using (HttpRequestMessage tamperedReq = new(HttpMethod.Post, "/v1/sync/spike-records"))
        {
            tamperedReq.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
            tamperedReq.Content = new StringContent(tamperedBody, Encoding.UTF8, "application/json");

            HttpResponseMessage tamperedResp = await client.SendAsync(tamperedReq);
            Assert.Equal(HttpStatusCode.Conflict, tamperedResp.StatusCode);

            using JsonDocument errDoc = JsonDocument.Parse(await tamperedResp.Content.ReadAsStringAsync());
            Assert.Equal("IDEMPOTENCY_KEY_REUSED", errDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task ParallelConcurrentIdempotentSyncInsertsExactlyOneRowPerKeyAcross50Iterations()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        const int iterations = 50;
        const int concurrency = 4;
        List<Guid> clientGuids = [];

        RaceSummary summary = await ParallelRaceHarness.RunAsync(
            iterations,
            concurrency,
            iteration =>
            {
                Guid farmerIdentityId = Guid.NewGuid();
                string bearerToken = CreateSignedToken(farmerIdentityId);
                Guid idempotencyKey = Guid.NewGuid();
                Guid clientGuid = Guid.NewGuid();
                clientGuids.Add(clientGuid);

                string payload = JsonSerializer.Serialize(new
                {
                    clientGuid,
                    entityType = "boundary_session",
                    payloadJson = $"{{\"iteration\":{iteration}}}",
                    clientRecordedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
                });

                Func<int, Task<bool>> worker = async _ =>
                {
                    using HttpRequestMessage req = new(HttpMethod.Post, "/v1/sync/spike-records");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                    req.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
                    req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    HttpResponseMessage resp = await client.SendAsync(req);
                    return resp.StatusCode == HttpStatusCode.Created;
                };
                return Task.FromResult(worker);
            });

        // Every concurrent retry receives the winner's 201 stored response
        Assert.Equal(iterations * concurrency, summary.TotalWinners);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM platform.sync_spike_records WHERE client_guid = ANY(@guids);";
        cmd.Parameters.AddWithValue("guids", clientGuids.ToArray());

        long insertedCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(iterations, insertedCount);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
