using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace FarmBasics.Api.Integrations;

public sealed record VoiceOtpCallRequest(string ToPhoneE164, string OtpCode, string Script = "voice_otp_delivery");

public sealed record VoiceOtpCallResult(bool Initiated, string? CallId, string? ErrorCode = null);

public interface IVoiceOtpGatewayClient
{
    Task<VoiceOtpCallResult> SendVoiceOtpAsync(VoiceOtpCallRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed HTTP client for Nigerian VoiceOTP / IVR calls per Doc/05-ENG §4.4 &amp; Doc/01A-ENG §10.1.
/// </summary>
internal sealed partial class VoiceOtpHttpClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<VoiceOtpHttpClient> logger) : IVoiceOtpGatewayClient
{
    public async Task<VoiceOtpCallResult> SendVoiceOtpAsync(VoiceOtpCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SmsIntegrationOptions options = new();
        configuration.GetSection(SmsIntegrationOptions.SectionName).Bind(options);

        string callPath = configuration["Integrations:Voice:EndpointPath"]?.TrimStart('/') ?? "calls";

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(callPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                to = request.ToPhoneE164,
                script = request.Script,
                variables = new { otp = request.OtpCode }
            })
        };

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {options.ApiKey}");
        }

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                VoiceOtpLog.CallHttpFailure(logger, (int)response.StatusCode);
                return new VoiceOtpCallResult(false, null, $"VOICE_HTTP_{(int)response.StatusCode}");
            }

            string rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
            string? callId = null;
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                using JsonDocument doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("callId", out JsonElement callIdProp))
                {
                    callId = callIdProp.GetString();
                }
            }

            return new VoiceOtpCallResult(true, callId ?? Guid.NewGuid().ToString("N"));
        }
        catch (CircuitBrokenException)
        {
            VoiceOtpLog.CircuitOpen(logger);
            return new VoiceOtpCallResult(false, null, "VOICE_PROVIDER_UNAVAILABLE");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            VoiceOtpLog.CallTransportFailure(logger, ex.GetType().Name);
            return new VoiceOtpCallResult(false, null, "VOICE_PROVIDER_UNAVAILABLE");
        }
    }
}

internal static partial class VoiceOtpLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "VoiceOTP gateway returned HTTP {StatusCode}")]
    public static partial void CallHttpFailure(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "VoiceOTP circuit breaker is open")]
    public static partial void CircuitOpen(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "VoiceOTP gateway call failed with {ErrorType}")]
    public static partial void CallTransportFailure(ILogger logger, string errorType);
}

/// <summary>
/// Offline deterministic sandbox VoiceOTP capture per PRECEDENCE P13-P15 and Sprint 3 S3.1.M2.
/// </summary>
public sealed class SandboxVoiceOtpGatewayClient : IVoiceOtpGatewayClient
{
    private readonly ConcurrentQueue<VoiceOtpCallRequest> _sentCalls = new();

    public IReadOnlyCollection<VoiceOtpCallRequest> SentCalls => _sentCalls.ToArray();

    public bool SimulateUnavailable { get; set; }

    public Task<VoiceOtpCallResult> SendVoiceOtpAsync(VoiceOtpCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (SimulateUnavailable)
        {
            return Task.FromResult(new VoiceOtpCallResult(false, null, "VOICE_SANDBOX_UNAVAILABLE"));
        }

        _sentCalls.Enqueue(request);
        return Task.FromResult(new VoiceOtpCallResult(true, $"sbx-call-{Guid.NewGuid():N}"));
    }

    public void Clear()
    {
        while (_sentCalls.TryDequeue(out _))
        {
        }
    }
}
