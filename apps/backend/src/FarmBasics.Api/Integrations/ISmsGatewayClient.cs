using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace FarmBasics.Api.Integrations;

public sealed record SmsSendRequest(string ToPhoneE164, string Body, string Type = "sms");

public sealed record SmsSendResult(bool Succeeded, string? MessageId, string? ErrorCode = null);

public interface ISmsGatewayClient
{
    Task<SmsSendResult> SendSmsAsync(SmsSendRequest request, CancellationToken cancellationToken = default);
}

public sealed class SmsIntegrationOptions
{
    public const string SectionName = "Integrations:Sms";
    public string Mode { get; set; } = "Sandbox";
    public string BaseUrl { get; set; } = "https://sms.sandbox.farmbasics.local/";
    public string EndpointPath { get; set; } = "messages";
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Typed HTTP client for Nigerian SMS/IVR gateway per Doc/05-ENG §4 &amp; Doc/01A-ENG §10.1.
/// </summary>
internal sealed partial class SmsGatewayHttpClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<SmsGatewayHttpClient> logger) : ISmsGatewayClient
{
    public async Task<SmsSendResult> SendSmsAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        SmsIntegrationOptions options = new();
        configuration.GetSection(SmsIntegrationOptions.SectionName).Bind(options);

        string endpointPath = string.IsNullOrWhiteSpace(options.EndpointPath)
            ? "messages"
            : options.EndpointPath.TrimStart('/');

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(endpointPath, UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                to = request.ToPhoneE164,
                body = request.Body,
                type = request.Type
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
                SmsClientLog.GatewayHttpFailure(logger, (int)response.StatusCode);
                return new SmsSendResult(false, null, $"SMS_HTTP_{(int)response.StatusCode}");
            }

            string rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
            string? messageId = null;
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                using JsonDocument doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("messageId", out JsonElement msgIdProp))
                {
                    messageId = msgIdProp.GetString();
                }
            }

            return new SmsSendResult(true, messageId ?? Guid.NewGuid().ToString("N"));
        }
        catch (CircuitBrokenException)
        {
            SmsClientLog.CircuitOpen(logger);
            return new SmsSendResult(false, null, "SMS_PROVIDER_UNAVAILABLE");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            SmsClientLog.GatewayTransportFailure(logger, ex.GetType().Name);
            return new SmsSendResult(false, null, "SMS_PROVIDER_UNAVAILABLE");
        }
    }
}

internal static partial class SmsClientLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "SMS gateway returned HTTP {StatusCode}")]
    public static partial void GatewayHttpFailure(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMS gateway circuit breaker is open")]
    public static partial void CircuitOpen(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMS gateway call failed with {ErrorType}")]
    public static partial void GatewayTransportFailure(ILogger logger, string errorType);
}

/// <summary>
/// Offline deterministic sandbox SMS capture per PRECEDENCE P13-P15 and Sprint 2 S2.1.M3.
/// Never logs OTP message bodies (Doc/AGENTS.md Rule 1).
/// </summary>
public sealed class SandboxSmsGatewayClient : ISmsGatewayClient
{
    private readonly ConcurrentQueue<SmsSendRequest> _sentMessages = new();

    public IReadOnlyCollection<SmsSendRequest> SentMessages => _sentMessages.ToArray();

    public bool SimulateUnavailable { get; set; }

    public Task<SmsSendResult> SendSmsAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (SimulateUnavailable)
        {
            return Task.FromResult(new SmsSendResult(false, null, "SMS_SANDBOX_UNAVAILABLE"));
        }

        _sentMessages.Enqueue(request);
        return Task.FromResult(new SmsSendResult(true, $"sbx-sms-{Guid.NewGuid():N}"));
    }

    public void Clear()
    {
        while (_sentMessages.TryDequeue(out _))
        {
        }
    }
}
