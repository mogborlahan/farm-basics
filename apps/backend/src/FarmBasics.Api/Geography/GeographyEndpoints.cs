using FarmBasics.BuildingBlocks.Api;
using Npgsql;

namespace FarmBasics.Api.Geography;

public sealed record StateDto(Guid Id, string Name, string Code);

public sealed record LgaDto(Guid Id, Guid StateId, string Name, string Code);

internal static class GeographyEndpoints
{
    public static void MapGeographyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/geography/states", HandleListStatesAsync);
        app.MapGet("/v1/geography/states/{stateId:guid}/lgas", HandleListLgasAsync);
    }

    private static async Task<IResult> HandleListStatesAsync(
        HttpContext httpContext,
        DatabaseConnectionSource dbSource,
        CancellationToken cancellationToken)
    {
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return ServiceUnavailable(httpContext);
        }

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, code FROM identity.state ORDER BY name ASC;";

        List<StateDto> states = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            states.Add(new StateDto(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<StateDto>>(states, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static async Task<IResult> HandleListLgasAsync(
        Guid stateId,
        HttpContext httpContext,
        DatabaseConnectionSource dbSource,
        CancellationToken cancellationToken)
    {
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return ServiceUnavailable(httpContext);
        }

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, state_id, name, code FROM identity.lga WHERE state_id = @stateId ORDER BY name ASC;";
        cmd.Parameters.AddWithValue("stateId", stateId);

        List<LgaDto> lgas = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lgas.Add(new LgaDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3)));
        }

        return Results.Ok(new ApiResponse<IReadOnlyList<LgaDto>>(lgas, new ApiMeta(httpContext.TraceIdentifier)));
    }

    private static IResult ServiceUnavailable(HttpContext httpContext)
        => Results.Json(
            new ApiErrorResponse(
                new ApiErrorDetail("SERVICE_UNAVAILABLE", "Geography database is temporarily unavailable."),
                new ApiMeta(httpContext.TraceIdentifier)),
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
