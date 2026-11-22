using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FarmBasics.Api.Accounts;
using FarmBasics.BuildingBlocks.Observability;
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

public sealed class Sprint8LedgerIntegrationTests
    : IClassFixture<PostgisDatabaseFixture>, IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private readonly PostgisDatabaseFixture _dbFixture;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;

    public Sprint8LedgerIntegrationTests(PostgisDatabaseFixture dbFixture)
    {
        _dbFixture = dbFixture;
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "sprint8-test-key" };
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

    private static async Task GrantRoleAsync(NpgsqlConnection conn, Guid identityId, string roleCode, Guid grantorId)
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
    public async Task Wp80ChartOfAccountsDeferredBalanceTriggerAndPrivilegeImmutability()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        // Confirm Doc 03 §12 rule: zero manual farmer-facing journal entry write routes
        EndpointDataSource endpointSource = factory.Services.GetRequiredService<EndpointDataSource>();
        List<string?> routePatterns = endpointSource.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();
        Assert.DoesNotContain(routePatterns, r => r is not null && r.Contains("/v1/accounts/journal-entries", StringComparison.Ordinal));

        Guid financeId = Guid.NewGuid();
        Guid farmerId = Guid.NewGuid();
        string financeJwt = CreateSignedToken(financeId);
        string farmerJwt = CreateSignedToken(farmerId);

        await ProvisionViaAuthMeAsync(client, financeJwt);
        await ProvisionViaAuthMeAsync(client, farmerJwt);

        await using NpgsqlConnection conn = new(_dbFixture.ConnectionString);
        await conn.OpenAsync();
        await GrantRoleAsync(conn, financeId, "Finance", financeId);

        // 1. Verify Chart of Accounts seeded with accountant signoff metadata
        HttpResponseMessage chartResp = await client.GetAsync(new Uri("/v1/accounts/chart", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, chartResp.StatusCode);
        using (JsonDocument cDoc = JsonDocument.Parse(await chartResp.Content.ReadAsStringAsync()))
        {
            JsonElement arr = cDoc.RootElement.GetProperty("data");
            Assert.True(arr.GetArrayLength() >= 19);
            Assert.Contains(arr.EnumerateArray(), a => a.GetProperty("code").GetString() == "1000");
            Assert.Contains(arr.EnumerateArray(), a => a.GetProperty("code").GetString() == "5000");
        }

        Guid cashAcctId;
        Guid seedAcctId;
        await using (NpgsqlCommand qAcct = conn.CreateCommand())
        {
            qAcct.CommandText = "SELECT id FROM accounts.account WHERE code = '1000';";
            cashAcctId = (Guid)(await qAcct.ExecuteScalarAsync())!;
            qAcct.CommandText = "SELECT id FROM accounts.account WHERE code = '5000';";
            seedAcctId = (Guid)(await qAcct.ExecuteScalarAsync())!;
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 2. Unbalanced journal entry rejected at COMMIT by deferred trigger trg_journal_balance (BR-C-08 / SQLSTATE 23514)
        await using (NpgsqlTransaction badTx = await conn.BeginTransactionAsync())
        {
            Guid badJeId = Guid.NewGuid();
            await using NpgsqlCommand ins = conn.CreateCommand();
            ins.Transaction = badTx;
            ins.CommandText = """
                INSERT INTO accounts.journal_entry (id, farm_identity_id, entry_date, description, source_module)
                VALUES (@jeId, @farmId, @d, 'Unbalanced test entry', 'test');
                INSERT INTO accounts.journal_entry_line (journal_entry_id, journal_entry_date, account_id, debit_amount, credit_amount)
                VALUES (@jeId, @d, @seedAcct, 100.00, 0),
                       (@jeId, @d, @cashAcct, 0, 80.00);
                """;
            ins.Parameters.AddWithValue("jeId", badJeId);
            ins.Parameters.AddWithValue("farmId", farmerId);
            ins.Parameters.AddWithValue("d", today);
            ins.Parameters.AddWithValue("seedAcct", seedAcctId);
            ins.Parameters.AddWithValue("cashAcct", cashAcctId);
            await ins.ExecuteNonQueryAsync();

            PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => badTx.CommitAsync());
            Assert.Equal("23514", ex.SqlState);
        }

        // 3. Header-only empty journal entry rejected at COMMIT by trg_journal_entry_not_empty (F-25(6) / SQLSTATE 23514)
        await using (NpgsqlTransaction emptyTx = await conn.BeginTransactionAsync())
        {
            await using NpgsqlCommand ins = conn.CreateCommand();
            ins.Transaction = emptyTx;
            ins.CommandText = """
                INSERT INTO accounts.journal_entry (id, farm_identity_id, entry_date, description, source_module)
                VALUES (@jeId, @farmId, @d, 'Empty header test', 'test');
                """;
            ins.Parameters.AddWithValue("jeId", Guid.NewGuid());
            ins.Parameters.AddWithValue("farmId", farmerId);
            ins.Parameters.AddWithValue("d", today);
            await ins.ExecuteNonQueryAsync();

            PostgresException ex = await Assert.ThrowsAsync<PostgresException>(() => emptyTx.CommitAsync());
            Assert.Equal("23514", ex.SqlState);
        }

        // 4. Insert valid balanced entry, then assert UPDATE/DELETE denied at DB privilege level (farmbasics_ledger_app -> 42501)
        Guid validJeId = Guid.NewGuid();
        await using (NpgsqlTransaction okTx = await conn.BeginTransactionAsync())
        {
            await using NpgsqlCommand ins = conn.CreateCommand();
            ins.Transaction = okTx;
            ins.CommandText = """
                SET LOCAL ROLE farmbasics_ledger_app;
                INSERT INTO accounts.journal_entry (id, farm_identity_id, entry_date, description, source_module)
                VALUES (@jeId, @farmId, @d, 'Seed purchase via app role', 'test');
                INSERT INTO accounts.journal_entry_line (journal_entry_id, journal_entry_date, account_id, debit_amount, credit_amount)
                VALUES (@jeId, @d, @seedAcct, 5000.00, 0),
                       (@jeId, @d, @cashAcct, 0, 5000.00);
                """;
            ins.Parameters.AddWithValue("jeId", validJeId);
            ins.Parameters.AddWithValue("farmId", farmerId);
            ins.Parameters.AddWithValue("d", today);
            ins.Parameters.AddWithValue("seedAcct", seedAcctId);
            ins.Parameters.AddWithValue("cashAcct", cashAcctId);
            await ins.ExecuteNonQueryAsync();
            await okTx.CommitAsync();
        }

        await using (NpgsqlTransaction privUpdTx = await conn.BeginTransactionAsync())
        {
            await using NpgsqlCommand updCmd = conn.CreateCommand();
            updCmd.Transaction = privUpdTx;
            updCmd.CommandText = """
                SET LOCAL ROLE farmbasics_ledger_app;
                UPDATE accounts.journal_entry SET description = 'Tampered' WHERE id = @jeId;
                """;
            updCmd.Parameters.AddWithValue("jeId", validJeId);
            PostgresException updEx = await Assert.ThrowsAsync<PostgresException>(() => updCmd.ExecuteNonQueryAsync());
            Assert.Equal("42501", updEx.SqlState);
            await privUpdTx.RollbackAsync();
        }

        await using (NpgsqlTransaction privDelTx = await conn.BeginTransactionAsync())
        {
            await using NpgsqlCommand delCmd = conn.CreateCommand();
            delCmd.Transaction = privDelTx;
            delCmd.CommandText = """
                SET LOCAL ROLE farmbasics_ledger_app;
                DELETE FROM accounts.journal_entry_line WHERE journal_entry_id = @jeId;
                """;
            delCmd.Parameters.AddWithValue("jeId", validJeId);
            PostgresException delEx = await Assert.ThrowsAsync<PostgresException>(() => delCmd.ExecuteNonQueryAsync());
            Assert.Equal("42501", delEx.SqlState);
            await privDelTx.RollbackAsync();
        }

        // 5. Finance admin creates reversing entry without altering original entry
        using (HttpRequestMessage revReq = new(HttpMethod.Post, "/v1/admin/accounts/journal-entries/reversing"))
        {
            revReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", financeJwt);
            revReq.Content = JsonContent.Create(new
            {
                originalEntryId = validJeId,
                reason = "Duplicate supplier invoice adjustment"
            });
            HttpResponseMessage revResp = await client.SendAsync(revReq);
            Assert.Equal(HttpStatusCode.Created, revResp.StatusCode);
        }

        using (HttpRequestMessage tbReq = new(HttpMethod.Get, "/v1/accounts/statements/trial-balance"))
        {
            tbReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            HttpResponseMessage tbResp = await client.SendAsync(tbReq);
            Assert.Equal(HttpStatusCode.OK, tbResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await tbResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("isBalanced").GetBoolean());
            Assert.Equal(10000.00m, data.GetProperty("totalDebits").GetDecimal());
            Assert.Equal(10000.00m, data.GetProperty("totalCredits").GetDecimal());
        }
    }

    [Fact]
    public async Task Wp81ConfigPostingRulesOutboxWorkerExactlyOnceAndCostCentres()
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
        await GrantRoleAsync(conn, adminId, "Finance", adminId);

        Guid unitId = Guid.NewGuid();
        Guid cycleId = Guid.NewGuid();
        Guid maizeId;
        await using (NpgsqlCommand setupCmd = conn.CreateCommand())
        {
            setupCmd.CommandText = "SELECT id FROM field.crop_profile WHERE common_name = 'Maize' LIMIT 1;";
            maizeId = (Guid)(await setupCmd.ExecuteScalarAsync())!;

            await using NpgsqlCommand insUnit = conn.CreateCommand();
            insUnit.CommandText = """
                INSERT INTO field.production_unit (id, farm_identity_id, name, system_type, capture_method, growing_medium_class, boundary)
                VALUES (
                    @uid, @fid, 'Cost Centre Plot', 'open_field', 'gps_walked_boundary', 'soil_based',
                    ST_GeomFromText('POLYGON((3.9200 7.6200, 3.9210 7.6200, 3.9210 7.6210, 3.9200 7.6210, 3.9200 7.6200))', 4326)::geography
                );
                INSERT INTO field.crop_cycle (id, production_unit_id, crop_profile_id, planting_date, stage, area_ha, unit_system_type, growing_medium_class)
                VALUES (@cid, @uid, @cropId, CURRENT_DATE, 'crop_care', 1.0, 'open_field', 'soil_based');
                """;
            insUnit.Parameters.AddWithValue("uid", unitId);
            insUnit.Parameters.AddWithValue("fid", farmerId);
            insUnit.Parameters.AddWithValue("cid", cycleId);
            insUnit.Parameters.AddWithValue("cropId", maizeId);
            await insUnit.ExecuteNonQueryAsync();
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid outboxSeedId;
        Guid outboxSaleId;
        await using (NpgsqlTransaction enqTx = await conn.BeginTransactionAsync())
        {
            await OutboxLedgerProcessor.EnqueueLedgerEventAsync(
                conn, enqTx, "accounts", farmerId, "owner.capital_injected",
                new LedgerEventPayload(farmerId, today, "Opening working capital", 50000m),
                CancellationToken.None);

            outboxSeedId = await OutboxLedgerProcessor.EnqueueLedgerEventAsync(
                conn, enqTx, "field", cycleId, "operation.seed_cost",
                new LedgerEventPayload(farmerId, today, "Certified maize seed", 12000m, CropCycleId: cycleId, ProductionUnitId: unitId),
                CancellationToken.None);

            outboxSaleId = await OutboxLedgerProcessor.EnqueueLedgerEventAsync(
                conn, enqTx, "trade", cycleId, "marketplace.escrow_released",
                new LedgerEventPayload(farmerId, today, "Harvest sale proceeds", 30000m, CommissionNgn: 1500m, CropCycleId: cycleId, ProductionUnitId: unitId),
                CancellationToken.None);

            await enqTx.CommitAsync();
        }

        // First worker batch posts all 3 events
        using (HttpRequestMessage procReq = new(HttpMethod.Post, "/v1/admin/accounts/outbox/process"))
        {
            procReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage procResp = await client.SendAsync(procReq);
            Assert.Equal(HttpStatusCode.OK, procResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await procResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("posted").GetInt32() >= 3);
        }

        // Simulate worker restart/duplicate pending state -> strictly once-only (posted == 0, deduplicated == 2)
        await using (NpgsqlCommand resetCmd = conn.CreateCommand())
        {
            resetCmd.CommandText = "UPDATE platform.events_outbox SET status = 'pending' WHERE id IN (@s1, @s2);";
            resetCmd.Parameters.AddWithValue("s1", outboxSeedId);
            resetCmd.Parameters.AddWithValue("s2", outboxSaleId);
            await resetCmd.ExecuteNonQueryAsync();
        }

        using (HttpRequestMessage replayProc = new(HttpMethod.Post, "/v1/admin/accounts/outbox/process"))
        {
            replayProc.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            HttpResponseMessage replayResp = await client.SendAsync(replayProc);
            Assert.Equal(HttpStatusCode.OK, replayResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await replayResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal(0, data.GetProperty("posted").GetInt32());
            Assert.Equal(2, data.GetProperty("deduplicated").GetInt32());
        }

        // Verify cost-centre P&L scoped by ?cropCycleId=
        using (HttpRequestMessage pnlReq = new(HttpMethod.Get, $"/v1/accounts/statements/profit-loss?cropCycleId={cycleId}"))
        {
            pnlReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            HttpResponseMessage pnlResp = await client.SendAsync(pnlReq);
            Assert.Equal(HttpStatusCode.OK, pnlResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await pnlResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal(30000.00m, data.GetProperty("totalIncome").GetDecimal());
            Assert.Equal(13500.00m, data.GetProperty("totalExpenses").GetDecimal());
            Assert.Equal(16500.00m, data.GetProperty("netProfitLoss").GetDecimal());
        }

        // Closed period rejects subsequent outbox postings to dead_letter with PERIOD_CLOSED
        DateOnly periodStart = today.AddDays(-5);
        DateOnly periodEnd = today.AddDays(5);
        Guid periodId;
        using (HttpRequestMessage createP = new(HttpMethod.Post, "/v1/admin/accounting-periods"))
        {
            createP.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            createP.Content = JsonContent.Create(new
            {
                farmIdentityId = farmerId,
                startDate = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                endDate = periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
            using JsonDocument pDoc = JsonDocument.Parse(await (await client.SendAsync(createP)).Content.ReadAsStringAsync());
            periodId = pDoc.RootElement.GetProperty("data").GetProperty("periodId").GetGuid();
        }

        using (HttpRequestMessage closeP = new(HttpMethod.Post, $"/v1/admin/accounting-periods/{periodId}/close"))
        {
            closeP.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(closeP)).StatusCode);
        }

        Guid closedOutboxId;
        await using (NpgsqlTransaction enqClosed = await conn.BeginTransactionAsync())
        {
            closedOutboxId = await OutboxLedgerProcessor.EnqueueLedgerEventAsync(
                conn, enqClosed, "field", cycleId, "operation.labor_cost",
                new LedgerEventPayload(farmerId, today, "Late labor inside closed period", 3000m, CropCycleId: cycleId),
                CancellationToken.None);
            await enqClosed.CommitAsync();
        }

        using (HttpRequestMessage closedProc = new(HttpMethod.Post, "/v1/admin/accounts/outbox/process"))
        {
            closedProc.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            await client.SendAsync(closedProc);
        }

        await using (NpgsqlCommand chkDeadCmd = conn.CreateCommand())
        {
            chkDeadCmd.CommandText = "SELECT status::text, last_error FROM platform.events_outbox WHERE id = @id;";
            chkDeadCmd.Parameters.AddWithValue("id", closedOutboxId);
            await using NpgsqlDataReader dr = await chkDeadCmd.ExecuteReaderAsync();
            Assert.True(await dr.ReadAsync());
            Assert.Equal("dead_letter", dr.GetString(0));
            Assert.Equal("PERIOD_CLOSED", dr.GetString(1));
        }
    }

    [Fact]
    public async Task Wp82AccountBalanceCacheReconciliationAlertsWithoutSilentFixAndStatements()
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
        await GrantRoleAsync(conn, adminId, "Finance", adminId);

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (NpgsqlTransaction tx = await conn.BeginTransactionAsync())
        {
            await OutboxLedgerProcessor.EnqueueLedgerEventAsync(
                conn, tx, "accounts", farmerId, "owner.capital_injected",
                new LedgerEventPayload(farmerId, today, "Farmer equity injection", 40000m),
                CancellationToken.None);
            await OutboxLedgerProcessor.EnqueueLedgerEventAsync(
                conn, tx, "field", farmerId, "operation.fertilizer_cost",
                new LedgerEventPayload(farmerId, today, "NPK basal fertilizer", 10000m),
                CancellationToken.None);
            await OutboxLedgerProcessor.EnqueueLedgerEventAsync(
                conn, tx, "trade", farmerId, "marketplace.escrow_released",
                new LedgerEventPayload(farmerId, today, "Crop harvest sale", 25000m, CommissionNgn: 1000m),
                CancellationToken.None);
            await tx.CommitAsync();
        }

        using (HttpRequestMessage proc = new(HttpMethod.Post, "/v1/admin/accounts/outbox/process"))
        {
            proc.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(proc)).StatusCode);
        }

        // 1. Verify Balance Sheet equation holds mid-period (Assets == Liabilities + Equity + NetIncome)
        using (HttpRequestMessage bsReq = new(HttpMethod.Get, "/v1/accounts/statements/balance-sheet"))
        {
            bsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", farmerJwt);
            HttpResponseMessage bsResp = await client.SendAsync(bsReq);
            Assert.Equal(HttpStatusCode.OK, bsResp.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await bsResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal(54000.00m, data.GetProperty("totalAssets").GetDecimal());
            Assert.Equal(14000.00m, data.GetProperty("netIncomeToDate").GetDecimal());
            Assert.Equal(54000.00m, data.GetProperty("totalEquity").GetDecimal());
            Assert.True(data.GetProperty("accountingEquationHolds").GetBoolean());
        }

        // 2. Create & close period -> populates AccountBalance cache -> clean reconciliation emits 0 alerts
        Guid periodId;
        using (HttpRequestMessage createP = new(HttpMethod.Post, "/v1/admin/accounting-periods"))
        {
            createP.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            createP.Content = JsonContent.Create(new
            {
                farmIdentityId = farmerId,
                startDate = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                endDate = today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
            using JsonDocument pDoc = JsonDocument.Parse(await (await client.SendAsync(createP)).Content.ReadAsStringAsync());
            periodId = pDoc.RootElement.GetProperty("data").GetProperty("periodId").GetGuid();
        }

        using (HttpRequestMessage closeP = new(HttpMethod.Post, $"/v1/admin/accounting-periods/{periodId}/close"))
        {
            closeP.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(closeP)).StatusCode);
        }

        using (HttpRequestMessage recClean = new(HttpMethod.Post, "/v1/admin/accounts/reconcile"))
        {
            recClean.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            recClean.Content = JsonContent.Create(new { farmIdentityId = farmerId, periodId });
            using JsonDocument doc = JsonDocument.Parse(await (await client.SendAsync(recClean)).Content.ReadAsStringAsync());
            Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("driftAlertCount").GetInt32());
        }

        // 3. Inject drift directly in AccountBalance cache -> reconcile MUST alert CRITICAL without silently fixing cache
        await using (NpgsqlCommand tamperCmd = conn.CreateCommand())
        {
            tamperCmd.CommandText = """
                UPDATE accounts.account_balance ab
                SET closing_balance = closing_balance + 9999.00
                FROM accounts.account a
                WHERE ab.account_id = a.id
                  AND ab.farm_identity_id = @farmId
                  AND ab.period_id = @pid
                  AND a.code = '1000';
                """;
            tamperCmd.Parameters.AddWithValue("farmId", farmerId);
            tamperCmd.Parameters.AddWithValue("pid", periodId);
            await tamperCmd.ExecuteNonQueryAsync();
        }

        double metricBefore = PlatformMetrics.LedgerReconciliationDriftTotal.Value;

        using (HttpRequestMessage recDrift = new(HttpMethod.Post, "/v1/admin/accounts/reconcile"))
        {
            recDrift.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminJwt);
            recDrift.Content = JsonContent.Create(new { farmIdentityId = farmerId, periodId });
            HttpResponseMessage recResp = await client.SendAsync(recDrift);
            Assert.Equal(HttpStatusCode.OK, recResp.StatusCode);

            using JsonDocument doc = JsonDocument.Parse(await recResp.Content.ReadAsStringAsync());
            JsonElement data = doc.RootElement.GetProperty("data");
            Assert.Equal(1, data.GetProperty("driftAlertCount").GetInt32());
            JsonElement alert = data.GetProperty("alerts")[0];
            Assert.Equal("CRITICAL", alert.GetProperty("severity").GetString());
            Assert.Equal("1000", alert.GetProperty("accountCode").GetString());
            Assert.Equal(9999.00m, alert.GetProperty("driftAmount").GetDecimal());
        }

        Assert.True(PlatformMetrics.LedgerReconciliationDriftTotal.Value >= metricBefore + 1.0);

        // Cache row remains tampered (63999.00), NOT silently overwritten back to 54000.00!
        await using (NpgsqlCommand verifyTamperedStillPresent = conn.CreateCommand())
        {
            verifyTamperedStillPresent.CommandText = """
                SELECT ab.closing_balance
                FROM accounts.account_balance ab
                JOIN accounts.account a ON a.id = ab.account_id
                WHERE ab.farm_identity_id = @farmId
                  AND ab.period_id = @pid
                  AND a.code = '1000';
                """;
            verifyTamperedStillPresent.Parameters.AddWithValue("farmId", farmerId);
            verifyTamperedStillPresent.Parameters.AddWithValue("pid", periodId);
            decimal cachedStill = (decimal)(await verifyTamperedStillPresent.ExecuteScalarAsync())!;
            Assert.Equal(63999.00m, cachedStill);
        }
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
