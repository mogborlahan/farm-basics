using System.Net;
using System.Net.Http.Headers;

namespace FarmBasics.Api.Integrations;

public enum CircuitState
{
    Closed = 0,
    Open = 1,
    HalfOpen = 2
}

public sealed record ResilienceOptions
{
    public int FailureThreshold { get; init; } = 5;
    public TimeSpan FailureWindow { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan OpenDuration { get; init; } = TimeSpan.FromSeconds(30);
    public int HalfOpenSuccessThreshold { get; init; } = 3;
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public Func<TimeSpan, TimeSpan>? JitterStrategy { get; init; }
    public Func<TimeSpan, TimeProvider, CancellationToken, Task>? DelayAsync { get; init; }
}

public sealed class CircuitBrokenException : HttpRequestException
{
    public CircuitBrokenException()
        : base("Integration circuit breaker is open.")
    {
    }

    public CircuitBrokenException(string integrationName)
        : base($"Circuit breaker is open for integration '{integrationName}'.")
    {
        IntegrationName = integrationName;
    }

    public CircuitBrokenException(string integrationName, Exception? innerException)
        : base($"Circuit breaker is open for integration '{integrationName}'.", innerException)
    {
        IntegrationName = integrationName;
    }

    public string? IntegrationName { get; }
}

/// <summary>
/// Thread-safe per-integration circuit breaker state matching Doc/05-ENG §1.1 defaults
/// (5 consecutive failures within 60s -> open 30s -> half-open -> closed after 3 successes).
/// </summary>
public sealed class CircuitBreakerState
{
    private readonly string _integrationName;
    private readonly ResilienceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _syncRoot = new();
    private readonly Queue<DateTimeOffset> _failureTimestamps = new();

    private CircuitState _state = CircuitState.Closed;
    private DateTimeOffset _openedAtUtc;
    private int _halfOpenSuccesses;

    public CircuitBreakerState(string integrationName, ResilienceOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(integrationName);
        _integrationName = integrationName;
        _options = options ?? new ResilienceOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string IntegrationName => _integrationName;

    public CircuitState CurrentState
    {
        get
        {
            lock (_syncRoot)
            {
                if (_state == CircuitState.Open
                    && _timeProvider.GetUtcNow() - _openedAtUtc >= _options.OpenDuration)
                {
                    _state = CircuitState.HalfOpen;
                    _halfOpenSuccesses = 0;
                }

                return _state;
            }
        }
    }

    public bool TryBeginExecution()
    {
        lock (_syncRoot)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (_state == CircuitState.Open)
            {
                if (now - _openedAtUtc >= _options.OpenDuration)
                {
                    _state = CircuitState.HalfOpen;
                    _halfOpenSuccesses = 0;
                    return true;
                }

                return false;
            }

            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (_syncRoot)
        {
            _failureTimestamps.Clear();

            if (_state == CircuitState.HalfOpen)
            {
                _halfOpenSuccesses++;
                if (_halfOpenSuccesses >= _options.HalfOpenSuccessThreshold)
                {
                    _state = CircuitState.Closed;
                    _halfOpenSuccesses = 0;
                }
            }
        }
    }

    public void RecordFailure()
    {
        lock (_syncRoot)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Open;
                _openedAtUtc = now;
                _halfOpenSuccesses = 0;
                _failureTimestamps.Clear();
                return;
            }

            while (_failureTimestamps.Count > 0 && now - _failureTimestamps.Peek() > _options.FailureWindow)
            {
                _failureTimestamps.Dequeue();
            }

            _failureTimestamps.Enqueue(now);
            if (_failureTimestamps.Count >= _options.FailureThreshold)
            {
                _state = CircuitState.Open;
                _openedAtUtc = now;
                _failureTimestamps.Clear();
            }
        }
    }
}

/// <summary>
/// Outbound HTTP resilience pipeline implementing Doc/05-ENG §1.1 &amp; Doc/01A-ENG §10.1:
/// isolated circuit breaker, exponential backoff retry (max 3 attempts), and 10s per-attempt timeout.
/// </summary>
internal sealed class IntegrationResilienceHandler(
    CircuitBreakerState circuitBreaker,
    ResilienceOptions? options = null,
    TimeProvider? timeProvider = null) : DelegatingHandler
{
    private readonly CircuitBreakerState _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
    private readonly ResilienceOptions _options = options ?? new ResilienceOptions();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_circuitBreaker.TryBeginExecution())
        {
            throw new CircuitBrokenException(_circuitBreaker.IntegrationName);
        }

        byte[]? bufferedBody = null;
        HttpContentHeaders? bufferedContentHeaders = null;
        if (request.Content is not null)
        {
            bufferedBody = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            bufferedContentHeaders = request.Content.Headers;
        }

        int maxAttempts = Math.Max(1, _options.MaxAttempts);
        Exception? lastException = null;
        HttpResponseMessage? lastTransientResponse = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using HttpRequestMessage attemptRequest = CloneRequest(request, bufferedBody, bufferedContentHeaders);
            CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ITimer? timeoutTimer = CreateAttemptTimeoutTimer(timeoutCts);

            try
            {
                HttpResponseMessage response = await base.SendAsync(attemptRequest, timeoutCts.Token);
                if (!IsTransientStatusCode(response.StatusCode))
                {
                    _circuitBreaker.RecordSuccess();
                    return response;
                }

                lastTransientResponse?.Dispose();
                lastTransientResponse = response;
                lastException = null;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new TimeoutException($"Integration '{_circuitBreaker.IntegrationName}' timed out.", ex);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
            }
            finally
            {
                if (timeoutTimer is not null)
                {
                    await timeoutTimer.DisposeAsync();
                }

                timeoutCts.Dispose();
            }

            if (attempt < maxAttempts)
            {
                TimeSpan delay = ComputeBackoffDelay(attempt);
                if (_options.DelayAsync is not null)
                {
                    await _options.DelayAsync(delay, _timeProvider, cancellationToken);
                }
                else
                {
                    await Task.Delay(delay, _timeProvider, cancellationToken);
                }
            }
        }

        _circuitBreaker.RecordFailure();

        if (lastTransientResponse is not null)
        {
            return lastTransientResponse;
        }

        throw lastException ?? new HttpRequestException($"Integration '{_circuitBreaker.IntegrationName}' failed after {maxAttempts} attempts.");
    }

    private ITimer? CreateAttemptTimeoutTimer(CancellationTokenSource timeoutCts)
    {
        if (_options.AttemptTimeout <= TimeSpan.Zero || _options.AttemptTimeout == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        return _timeProvider.CreateTimer(
            static state =>
            {
                try
                {
                    ((CancellationTokenSource)state!).Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            },
            timeoutCts,
            _options.AttemptTimeout,
            Timeout.InfiniteTimeSpan);
    }

    private TimeSpan ComputeBackoffDelay(int failedAttemptNumber)
    {
        double exponent = Math.Pow(2, failedAttemptNumber - 1);
        TimeSpan baseDelay = TimeSpan.FromMilliseconds(_options.BaseRetryDelay.TotalMilliseconds * exponent);

        if (_options.JitterStrategy is not null)
        {
            return _options.JitterStrategy(baseDelay);
        }

        // Apply symmetric +/- 20% jitter to avoid thundering-herd retries (Doc/05-ENG §1.1).
        double factor = 0.8 + (Random.Shared.NextDouble() * 0.4);
        return TimeSpan.FromMilliseconds(Math.Max(0, baseDelay.TotalMilliseconds * factor));
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout
           || statusCode == HttpStatusCode.TooManyRequests
           || (int)statusCode >= 500;

    private static HttpRequestMessage CloneRequest(
        HttpRequestMessage original,
        byte[]? bufferedBody,
        HttpContentHeaders? contentHeaders)
    {
        HttpRequestMessage clone = new(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };

        foreach (KeyValuePair<string, IEnumerable<string>> header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (bufferedBody is not null)
        {
            ByteArrayContent content = new(bufferedBody);
            if (contentHeaders is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in contentHeaders)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            clone.Content = content;
        }

        return clone;
    }
}
