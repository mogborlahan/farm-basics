namespace FarmBasics.BuildingBlocks.Api;

/// <summary>
/// Uniform response and error envelopes specified in Document 03 §1.4.
/// </summary>
public sealed record ApiMeta(string RequestId);

public sealed record ApiResponse<T>(T Data, ApiMeta Meta);

public sealed record ApiErrorDetail(string Code, string Message, object? Details = null);

public sealed record ApiErrorResponse(ApiErrorDetail Error, ApiMeta Meta);
