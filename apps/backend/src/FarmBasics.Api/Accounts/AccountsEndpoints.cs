using System.Text.Json;
using FarmBasics.Api.Agents;
using FarmBasics.Api.Audit;
using FarmBasics.Api.Auth;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Accounts;

public sealed record CreateReversingEntryRequest(
    Guid OriginalEntryId,
    string Reason,
    DateOnly? ReversingDate = null);

public sealed record CreateAccountingPeriodRequest(
    Guid FarmIdentityId,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record ReconcileBalancesRequest(Guid FarmIdentityId, Guid PeriodId);

/// <summary>
/// Read-only computed financial statements (P&amp;L, Balance Sheet, Trial Balance) and admin ledger governance (Doc 03 §12 / WP-80..WP-82).
/// Zero manual JournalEntry creation/editing endpoints exist for farmer callers.
/// </summary>
internal static class AccountsEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/accounts/chart", HandleGetChartOfAccountsAsync);

        app.MapGet("/v1/accounts/statements/profit-loss", HandleProfitLossAsync)
            .RequireAuthorization();

        app.MapGet("/v1/accounts/statements/balance-sheet", HandleBalanceSheetAsync)
            .RequireAuthorization();

        app.MapGet("/v1/accounts/statements/trial-balance", HandleTrialBalanceAsync)
            .RequireAuthorization();

        app.MapGet("/v1/accounts/ledger", HandleListLedgerEntriesAsync)
            .RequireAuthorization();

        app.MapPost("/v1/admin/accounts/journal-entries/reversing", HandleCreateReversingEntryAsync)
            .RequireAuthorization()
            .RequireAdminPermission("accounts", "manage");

        app.MapPost("/v1/admin/accounting-periods", HandleCreatePeriodAsync)
            .RequireAuthorization()
            .RequireAdminPermission("accounts", "manage");

        app.MapPost("/v1/admin/accounting-periods/{periodId:guid}/close", HandleClosePeriodAsync)
            .RequireAuthorization()
            .RequireAdminPermission("accounts", "manage");

        app.MapPost("/v1/admin/accounts/reconcile", HandleReconcileAsync)
            .RequireAuthorization()
            .RequireAdminPermission("accounts", "reconcile");

        app.MapPost("/v1/admin/accounts/outbox/process", HandleProcessOutboxAsync)
            .RequireAuthorization()
            .RequireAdminPermission("accounts", "manage");
    }

    private static async Task<IResult> HandleGetChartOfAccountsAsync(
        HttpContext httpContext,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, name, type::text, normal_balance::text, accountant_signoff_status
            FROM accounts.account
            ORDER BY code ASC;
            """;

        List<object> accounts = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(new
            {
                id = reader.GetGuid(0),
                code = reader.GetString(1),
                name = reader.GetString(2),
                type = reader.GetString(3),
                normalBalance = reader.GetString(4),
                accountantSignoffStatus = reader.GetString(5)
            });
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<object>>(accounts, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleProfitLossAsync(
        DateOnly? from,
        DateOnly? to,
        Guid? cropCycleId,
        Guid? productionUnitId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid farmerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                a.code,
                a.name,
                a.type::text,
                COALESCE(SUM(CASE WHEN a.type = 'income' THEN jel.credit_amount - jel.debit_amount ELSE jel.debit_amount - jel.credit_amount END), 0) AS net_amount
            FROM accounts.journal_entry je
            JOIN accounts.journal_entry_line jel
              ON jel.journal_entry_id = je.id
             AND jel.journal_entry_date = je.entry_date
            JOIN accounts.account a ON a.id = jel.account_id
            WHERE je.farm_identity_id = @farmId
              AND a.type IN ('income', 'expense')
              AND (@fromDate IS NULL OR je.entry_date >= @fromDate)
              AND (@toDate IS NULL OR je.entry_date <= @toDate)
              AND (@cycleId IS NULL OR jel.crop_cycle_id = @cycleId)
              AND (@unitId IS NULL OR jel.production_unit_id = @unitId)
            GROUP BY a.code, a.name, a.type
            ORDER BY a.code ASC;
            """;
        cmd.Parameters.AddWithValue("farmId", farmerId);
        NpgsqlParameter fP = cmd.Parameters.Add("fromDate", NpgsqlDbType.Date);
        fP.Value = from.HasValue ? from.Value : DBNull.Value;
        NpgsqlParameter tP = cmd.Parameters.Add("toDate", NpgsqlDbType.Date);
        tP.Value = to.HasValue ? to.Value : DBNull.Value;
        NpgsqlParameter cP = cmd.Parameters.Add("cycleId", NpgsqlDbType.Uuid);
        cP.Value = cropCycleId.HasValue ? cropCycleId.Value : DBNull.Value;
        NpgsqlParameter uP = cmd.Parameters.Add("unitId", NpgsqlDbType.Uuid);
        uP.Value = productionUnitId.HasValue ? productionUnitId.Value : DBNull.Value;

        List<object> incomeLines = [];
        List<object> expenseLines = [];
        decimal totalIncome = 0m;
        decimal totalExpenses = 0m;

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string code = reader.GetString(0);
            string name = reader.GetString(1);
            string type = reader.GetString(2);
            decimal netAmount = reader.GetDecimal(3);

            object lineObj = new { accountCode = code, accountName = name, amount = netAmount };
            if (string.Equals(type, "income", StringComparison.Ordinal))
            {
                incomeLines.Add(lineObj);
                totalIncome += netAmount;
            }
            else
            {
                expenseLines.Add(lineObj);
                totalExpenses += netAmount;
            }
        }

        return Results.Ok(new ApiResponse<object>(
            new
            {
                farmIdentityId = farmerId,
                from,
                to,
                cropCycleId,
                productionUnitId,
                incomeLines,
                expenseLines,
                totalIncome,
                totalExpenses,
                netProfitLoss = totalIncome - totalExpenses
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleBalanceSheetAsync(
        DateOnly? asOf,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid farmerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);
        DateOnly effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                a.code,
                a.name,
                a.type::text,
                a.normal_balance::text,
                COALESCE(SUM(jel.debit_amount), 0),
                COALESCE(SUM(jel.credit_amount), 0)
            FROM accounts.journal_entry je
            JOIN accounts.journal_entry_line jel
              ON jel.journal_entry_id = je.id
             AND jel.journal_entry_date = je.entry_date
            JOIN accounts.account a ON a.id = jel.account_id
            WHERE je.farm_identity_id = @farmId
              AND je.entry_date <= @asOf
            GROUP BY a.code, a.name, a.type, a.normal_balance
            ORDER BY a.code ASC;
            """;
        cmd.Parameters.AddWithValue("farmId", farmerId);
        cmd.Parameters.AddWithValue("asOf", effectiveAsOf);

        List<object> assets = [];
        List<object> liabilities = [];
        List<object> equity = [];
        decimal totalAssets = 0m;
        decimal totalLiabilities = 0m;
        decimal recordedEquity = 0m;
        decimal netIncomeToDate = 0m;

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string code = reader.GetString(0);
            string name = reader.GetString(1);
            string type = reader.GetString(2);
            decimal debits = reader.GetDecimal(4);
            decimal credits = reader.GetDecimal(5);

            switch (type)
            {
                case "asset":
                    decimal assetBal = debits - credits;
                    assets.Add(new { accountCode = code, accountName = name, balance = assetBal });
                    totalAssets += assetBal;
                    break;
                case "liability":
                    decimal liabBal = credits - debits;
                    liabilities.Add(new { accountCode = code, accountName = name, balance = liabBal });
                    totalLiabilities += liabBal;
                    break;
                case "equity":
                    decimal eqBal = credits - debits;
                    equity.Add(new { accountCode = code, accountName = name, balance = eqBal });
                    recordedEquity += eqBal;
                    break;
                case "income":
                    netIncomeToDate += credits - debits;
                    break;
                case "expense":
                    netIncomeToDate -= debits - credits;
                    break;
            }
        }

        decimal totalEquity = recordedEquity + netIncomeToDate;
        bool accountingEquationHolds = totalAssets == (totalLiabilities + totalEquity);

        return Results.Ok(new ApiResponse<object>(
            new
            {
                farmIdentityId = farmerId,
                asOf = effectiveAsOf,
                assets,
                liabilities,
                equity,
                netIncomeToDate,
                totalAssets,
                totalLiabilities,
                totalEquity,
                accountingEquationHolds
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleTrialBalanceAsync(
        DateOnly? asOf,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid farmerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);
        DateOnly effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                a.code,
                a.name,
                a.type::text,
                COALESCE(SUM(jel.debit_amount), 0),
                COALESCE(SUM(jel.credit_amount), 0)
            FROM accounts.journal_entry je
            JOIN accounts.journal_entry_line jel
              ON jel.journal_entry_id = je.id
             AND jel.journal_entry_date = je.entry_date
            JOIN accounts.account a ON a.id = jel.account_id
            WHERE je.farm_identity_id = @farmId
              AND je.entry_date <= @asOf
            GROUP BY a.code, a.name, a.type
            ORDER BY a.code ASC;
            """;
        cmd.Parameters.AddWithValue("farmId", farmerId);
        cmd.Parameters.AddWithValue("asOf", effectiveAsOf);

        List<object> rows = [];
        decimal totalDebits = 0m;
        decimal totalCredits = 0m;

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            decimal debits = reader.GetDecimal(3);
            decimal credits = reader.GetDecimal(4);
            totalDebits += debits;
            totalCredits += credits;
            rows.Add(new
            {
                accountCode = reader.GetString(0),
                accountName = reader.GetString(1),
                type = reader.GetString(2),
                totalDebits = debits,
                totalCredits = credits
            });
        }

        return Results.Ok(new ApiResponse<object>(
            new
            {
                farmIdentityId = farmerId,
                asOf = effectiveAsOf,
                rows,
                totalDebits,
                totalCredits,
                isBalanced = totalDebits == totalCredits
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleListLedgerEntriesAsync(
        string? accountCode,
        DateOnly? from,
        DateOnly? to,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid farmerId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated farmer identity is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                je.id, je.entry_date, je.description, je.source_module,
                a.code, a.name, jel.debit_amount, jel.credit_amount,
                jel.crop_cycle_id, jel.production_unit_id
            FROM accounts.journal_entry je
            JOIN accounts.journal_entry_line jel
              ON jel.journal_entry_id = je.id
             AND jel.journal_entry_date = je.entry_date
            JOIN accounts.account a ON a.id = jel.account_id
            WHERE je.farm_identity_id = @farmId
              AND (@code IS NULL OR a.code = @code)
              AND (@fromDate IS NULL OR je.entry_date >= @fromDate)
              AND (@toDate IS NULL OR je.entry_date <= @toDate)
            ORDER BY je.entry_date DESC, je.created_at DESC, a.code ASC
            LIMIT 200;
            """;
        cmd.Parameters.AddWithValue("farmId", farmerId);
        NpgsqlParameter cP = cmd.Parameters.Add("code", NpgsqlDbType.Text);
        cP.Value = string.IsNullOrWhiteSpace(accountCode) ? DBNull.Value : accountCode.Trim();
        NpgsqlParameter fP = cmd.Parameters.Add("fromDate", NpgsqlDbType.Date);
        fP.Value = from.HasValue ? from.Value : DBNull.Value;
        NpgsqlParameter tP = cmd.Parameters.Add("toDate", NpgsqlDbType.Date);
        tP.Value = to.HasValue ? to.Value : DBNull.Value;

        List<object> entries = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new
            {
                journalEntryId = reader.GetGuid(0),
                entryDate = reader.GetFieldValue<DateOnly>(1),
                description = reader.GetString(2),
                sourceModule = reader.GetString(3),
                accountCode = reader.GetString(4),
                accountName = reader.GetString(5),
                debitAmount = reader.GetDecimal(6),
                creditAmount = reader.GetDecimal(7),
                cropCycleId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8),
                productionUnitId = reader.IsDBNull(9) ? (Guid?)null : reader.GetGuid(9)
            });
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<object>>(entries, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleCreateReversingEntryAsync(
        CreateReversingEntryRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated finance administrator is required.");
        }

        if (request.OriginalEntryId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "OriginalEntryId and Reason are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid farmIdentityId;
        DateOnly originalDate;
        await using (NpgsqlCommand origCmd = conn.CreateCommand())
        {
            origCmd.Transaction = tx;
            origCmd.CommandText = "SELECT farm_identity_id, entry_date FROM accounts.journal_entry WHERE id = @id LIMIT 1;";
            origCmd.Parameters.AddWithValue("id", request.OriginalEntryId);
            await using NpgsqlDataReader r = await origCmd.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Original journal entry was not found.");
            }
            farmIdentityId = r.GetGuid(0);
            originalDate = r.GetFieldValue<DateOnly>(1);
        }

        DateOnly reversingDate = request.ReversingDate ?? originalDate;
        if (await OutboxLedgerProcessor.IsDateInClosedPeriodAsync(conn, tx, farmIdentityId, reversingDate, cancellationToken))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status422UnprocessableEntity, "PERIOD_CLOSED", "Cannot post reversing entry inside a closed accounting period.");
        }

        await OutboxLedgerProcessor.EnsureJournalPartitionAsync(dataSource, reversingDate, cancellationToken);

        List<(Guid AccountId, decimal Debit, decimal Credit, Guid? CycleId, Guid? UnitId)> origLines = [];
        await using (NpgsqlCommand lineLoadCmd = conn.CreateCommand())
        {
            lineLoadCmd.Transaction = tx;
            lineLoadCmd.CommandText = """
                SELECT account_id, debit_amount, credit_amount, crop_cycle_id, production_unit_id
                FROM accounts.journal_entry_line
                WHERE journal_entry_id = @id AND journal_entry_date = @origDate;
                """;
            lineLoadCmd.Parameters.AddWithValue("id", request.OriginalEntryId);
            lineLoadCmd.Parameters.AddWithValue("origDate", originalDate);
            await using NpgsqlDataReader lr = await lineLoadCmd.ExecuteReaderAsync(cancellationToken);
            while (await lr.ReadAsync(cancellationToken))
            {
                origLines.Add((
                    lr.GetGuid(0),
                    lr.GetDecimal(1),
                    lr.GetDecimal(2),
                    lr.IsDBNull(3) ? null : lr.GetGuid(3),
                    lr.IsDBNull(4) ? null : lr.GetGuid(4)));
            }
        }

        Guid reversingEntryId = Guid.NewGuid();
        await using (NpgsqlCommand insHdr = conn.CreateCommand())
        {
            insHdr.Transaction = tx;
            insHdr.CommandText = """
                INSERT INTO accounts.journal_entry (
                    id, farm_identity_id, entry_date, description, source_module, source_ref_type, source_ref_id
                )
                VALUES (@id, @farmId, @revDate, @desc, 'accounts', 'reversal_of_journal_entry', @origId);
                """;
            insHdr.Parameters.AddWithValue("id", reversingEntryId);
            insHdr.Parameters.AddWithValue("farmId", farmIdentityId);
            insHdr.Parameters.AddWithValue("revDate", reversingDate);
            insHdr.Parameters.AddWithValue("desc", $"Reversal of {request.OriginalEntryId}: {request.Reason.Trim()}");
            insHdr.Parameters.AddWithValue("origId", request.OriginalEntryId);
            await insHdr.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach ((Guid accountId, decimal debit, decimal credit, Guid? cycleId, Guid? unitId) in origLines)
        {
            await using NpgsqlCommand revLine = conn.CreateCommand();
            revLine.Transaction = tx;
            revLine.CommandText = """
                INSERT INTO accounts.journal_entry_line (
                    id, journal_entry_id, journal_entry_date, account_id,
                    debit_amount, credit_amount, crop_cycle_id, production_unit_id
                )
                VALUES (gen_random_uuid(), @jeId, @jeDate, @acctId, @swappedDebit, @swappedCredit, @cycleId, @unitId);
                """;
            revLine.Parameters.AddWithValue("jeId", reversingEntryId);
            revLine.Parameters.AddWithValue("jeDate", reversingDate);
            revLine.Parameters.AddWithValue("acctId", accountId);
            revLine.Parameters.AddWithValue("swappedDebit", credit);
            revLine.Parameters.AddWithValue("swappedCredit", debit);
            NpgsqlParameter cP = revLine.Parameters.Add("cycleId", NpgsqlDbType.Uuid);
            cP.Value = cycleId.HasValue ? cycleId.Value : DBNull.Value;
            NpgsqlParameter uP = revLine.Parameters.Add("unitId", NpgsqlDbType.Uuid);
            uP.Value = unitId.HasValue ? unitId.Value : DBNull.Value;
            await revLine.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord("accounts", "journal_entry.reversed", "journal_entry", reversingEntryId, adminId, JsonSerializer.Serialize(new { originalEntryId = request.OriginalEntryId, reason = request.Reason.Trim() }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(new { reversingEntryId, originalEntryId = request.OriginalEntryId, reversingDate }, new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleCreatePeriodAsync(
        CreateAccountingPeriodRequest request,
        HttpContext httpContext,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (request.FarmIdentityId == Guid.Empty || request.EndDate <= request.StartDate)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "FarmIdentityId and valid StartDate < EndDate are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        Guid periodId = Guid.NewGuid();
        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO accounts.accounting_period (id, farm_identity_id, start_date, end_date, status)
            VALUES (@id, @farmId, @startDate, @endDate, 'open');
            """;
        cmd.Parameters.AddWithValue("id", periodId);
        cmd.Parameters.AddWithValue("farmId", request.FarmIdentityId);
        cmd.Parameters.AddWithValue("startDate", request.StartDate);
        cmd.Parameters.AddWithValue("endDate", request.EndDate);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        return Results.Json(
            new ApiResponse<object>(new { periodId, status = "open", request.StartDate, request.EndDate }, new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleClosePeriodAsync(
        Guid periodId,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        if (!AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId))
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", "Authenticated finance administrator is required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        Guid farmId;
        DateOnly startDate;
        DateOnly endDate;
        string currentStatus;
        await using (NpgsqlCommand loadCmd = conn.CreateCommand())
        {
            loadCmd.Transaction = tx;
            loadCmd.CommandText = "SELECT farm_identity_id, start_date, end_date, status::text FROM accounts.accounting_period WHERE id = @id FOR UPDATE;";
            loadCmd.Parameters.AddWithValue("id", periodId);
            await using NpgsqlDataReader reader = await loadCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);
                return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", "Accounting period was not found.");
            }
            farmId = reader.GetGuid(0);
            startDate = reader.GetFieldValue<DateOnly>(1);
            endDate = reader.GetFieldValue<DateOnly>(2);
            currentStatus = reader.GetString(3);
        }

        if (string.Equals(currentStatus, "closed", StringComparison.Ordinal))
        {
            await tx.RollbackAsync(cancellationToken);
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status409Conflict, "CONFLICT", "Accounting period is already closed.");
        }

        await LedgerReconciliationService.RefreshAccountBalanceCacheAsync(conn, tx, farmId, periodId, cancellationToken);

        await using (NpgsqlCommand closeCmd = conn.CreateCommand())
        {
            closeCmd.Transaction = tx;
            closeCmd.CommandText = "UPDATE accounts.accounting_period SET status = 'closed', closed_at = now() WHERE id = @id;";
            closeCmd.Parameters.AddWithValue("id", periodId);
            await closeCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AuditWriter.WriteAsync(
            conn,
            tx,
            new AuditRecord("accounts", "accounting_period.closed", "accounting_period", periodId, adminId, JsonSerializer.Serialize(new { farmId, startDate, endDate }, JsonOptions)),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(new { periodId, status = "closed" }, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleReconcileAsync(
        ReconcileBalancesRequest request,
        HttpContext httpContext,
        ICurrentIdentityAccessor identityAccessor,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        AgentOnboardingEndpoints.TryGetCallerGuid(httpContext, identityAccessor, out Guid adminId);
        if (request.FarmIdentityId == Guid.Empty || request.PeriodId == Guid.Empty)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "FarmIdentityId and PeriodId are required.");
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        IReadOnlyList<ReconciliationDriftAlertDto> alerts = await LedgerReconciliationService.RunReconciliationAsync(
            conn,
            tx,
            request.FarmIdentityId,
            request.PeriodId,
            adminId == Guid.Empty ? null : adminId,
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return Results.Ok(new ApiResponse<object>(
            new { periodId = request.PeriodId, driftAlertCount = alerts.Count, alerts },
            new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleProcessOutboxAsync(
        HttpContext httpContext,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);
        OutboxProcessSummary summary = await OutboxLedgerProcessor.ProcessPendingBatchAsync(dataSource, 100, cancellationToken);

        return Results.Ok(new ApiResponse<OutboxProcessSummary>(summary, new ApiMeta(httpContext.TraceIdentifier)));
    }
}
