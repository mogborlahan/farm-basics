using FarmBasics.Api.Registration;
using Npgsql;

namespace FarmBasics.Api.Reviews;

public sealed record ReviewVerdictContext(
    Guid ReviewQueueItemId,
    string CategoryCode,
    string SubjectRefType,
    Guid SubjectRefId,
    string VerdictCode,
    string? VerdictNotes,
    Guid ReviewerIdentityId);

public interface IReviewVerdictHandler
{
    string CategoryCode { get; }

    Task<object?> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReviewVerdictContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Configurable verdict vocabulary + registered write-back handlers per PRECEDENCE P16 and Doc/01 §9.
/// </summary>
internal sealed class ReviewVerdictWriteBackRegistry
{
    private static readonly Dictionary<string, HashSet<string>> ConfiguredVerdictsByCategory =
        new(StringComparer.Ordinal)
        {
            ["IDENTITY_NIN_MISMATCH"] = ["APPROVE_IDENTITY", "REQUEST_CORRECTION", "REJECT_IDENTITY"],
            ["ORG_VERIFICATION"] = ["APPROVE_ORG", "REQUEST_CORRECTION", "REJECT_ORG"],
            ["KB_CHEMICAL_PUBLISH"] = ["APPROVE_PUBLISH", "REQUEST_REVISION", "REJECT_PUBLISH"]
        };

    private static readonly HashSet<string> DefaultVerdicts = ["APPROVE", "REQUEST_CORRECTION", "REJECT"];

    private readonly Dictionary<string, IReviewVerdictHandler> _handlers;

    public ReviewVerdictWriteBackRegistry(IEnumerable<IReviewVerdictHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToDictionary(h => h.CategoryCode, StringComparer.Ordinal);
    }

    public static bool IsAllowedVerdict(string categoryCode, string verdictCode)
    {
        if (string.IsNullOrWhiteSpace(verdictCode))
        {
            return false;
        }

        string normalizedVerdict = verdictCode.Trim();
        if (ConfiguredVerdictsByCategory.TryGetValue(categoryCode, out HashSet<string>? categoryCodes))
        {
            return categoryCodes.Contains(normalizedVerdict);
        }

        return DefaultVerdicts.Contains(normalizedVerdict) || categoryCodes is not null;
    }

    public Task<object?> ExecuteWriteBackAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReviewVerdictContext context,
        CancellationToken cancellationToken)
    {
        if (_handlers.TryGetValue(context.CategoryCode, out IReviewVerdictHandler? handler))
        {
            return handler.ExecuteAsync(connection, transaction, context, cancellationToken);
        }

        return Task.FromResult<object?>(new { status = "recorded", context.VerdictCode });
    }
}

internal sealed class IdentityNinMismatchVerdictHandler : IReviewVerdictHandler
{
    public string CategoryCode => "IDENTITY_NIN_MISMATCH";

    public async Task<object?> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ReviewVerdictContext context,
        CancellationToken cancellationToken)
    {
        if (string.Equals(context.VerdictCode, "APPROVE_IDENTITY", StringComparison.Ordinal))
        {
            await using NpgsqlCommand existsCmd = connection.CreateCommand();
            existsCmd.Transaction = transaction;
            existsCmd.CommandText = "SELECT status::text FROM identity.identity WHERE id = @id;";
            existsCmd.Parameters.AddWithValue("id", context.SubjectRefId);
            string? currentStatus = await existsCmd.ExecuteScalarAsync(cancellationToken) as string;
            if (currentStatus is null)
            {
                return new { activated = false, reason = "subject_not_found" };
            }

            string farmerId = await SelfServiceRegistrationEndpoints.ActivateIdentityAndCreateFarmerProfileAsync(
                connection,
                transaction,
                context.SubjectRefId,
                cancellationToken);

            return new { activated = true, farmerId };
        }

        if (string.Equals(context.VerdictCode, "REJECT_IDENTITY", StringComparison.Ordinal))
        {
            await using NpgsqlCommand rejectCmd = connection.CreateCommand();
            rejectCmd.Transaction = transaction;
            rejectCmd.CommandText = """
                UPDATE identity.identity
                SET status = 'deactivated',
                    updated_at = now()
                WHERE id = @id;
                """;
            rejectCmd.Parameters.AddWithValue("id", context.SubjectRefId);
            await rejectCmd.ExecuteNonQueryAsync(cancellationToken);
            return new { activated = false, identityStatus = "deactivated" };
        }

        return new { activated = false, identityStatus = "pending_verification" };
    }
}
