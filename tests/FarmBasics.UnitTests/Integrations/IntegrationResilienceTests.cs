using System.Net;
using FarmBasics.Api.Integrations;
using Xunit;

namespace FarmBasics.UnitTests.Integrations;

public sealed class IntegrationResilienceTests
{
    [Fact]
    public async Task ExponentialBackoffRetriesThreeTimesBeforeReturningLast5xxAndRecordsOneBreakerFailure()
    {
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
        List<TimeSpan> recordedDelays = [];
        ResilienceOptions options = new()
        {
            MaxAttempts = 3,
            BaseRetryDelay = TimeSpan.FromMilliseconds(500),
            JitterStrategy = delay => delay,
            DelayAsync = (delay, _, _) =>
            {
                recordedDelays.Add(delay);
                return Task.CompletedTask;
            }
        };

        CircuitBreakerState breaker = new("nin-verification", options, clock);
        StubHttpHandler inner = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using IntegrationResilienceHandler handler = new(breaker, options, clock) { InnerHandler = inner };
        using HttpClient client = new(handler);

        using HttpRequestMessage req = new(HttpMethod.Post, new Uri("https://sandbox.test/vnin"))
        {
            Content = new StringContent("{\"number\":\"12345678901\"}")
        };

        using HttpResponseMessage response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.AttemptCount);
        Assert.Equal([TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000)], recordedDelays);
        Assert.Equal(CircuitState.Closed, breaker.CurrentState);
    }

    [Fact]
    public async Task CircuitTripsOn5ConsecutiveFailuresWithin60sFastFailsDuring30sAndResetsAfter3HalfOpenSuccesses()
    {
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
        ResilienceOptions options = new()
        {
            FailureThreshold = 5,
            FailureWindow = TimeSpan.FromSeconds(60),
            OpenDuration = TimeSpan.FromSeconds(30),
            HalfOpenSuccessThreshold = 3,
            MaxAttempts = 1
        };

        CircuitBreakerState breaker = new("nin-verification", options, clock);
        HttpStatusCode nextStatus = HttpStatusCode.InternalServerError;
        StubHttpHandler inner = new(_ => Task.FromResult(new HttpResponseMessage(nextStatus)));

        using IntegrationResilienceHandler handler = new(breaker, options, clock) { InnerHandler = inner };
        using HttpClient client = new(handler);

        // 5 consecutive failures inside 60s window trip circuit to Open
        for (int i = 0; i < 5; i++)
        {
            using HttpResponseMessage resp = await client.PostAsync(new Uri("https://sandbox.test/vnin"), new StringContent("{}"));
            Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(CircuitState.Open, breaker.CurrentState);
        Assert.Equal(5, inner.AttemptCount);

        // Fast-fails immediately without calling inner handler during 30s open period
        await Assert.ThrowsAsync<CircuitBrokenException>(() =>
            client.PostAsync(new Uri("https://sandbox.test/vnin"), new StringContent("{}")));
        Assert.Equal(5, inner.AttemptCount);

        // Advance past 30s open duration -> HalfOpen -> requires 3 consecutive successes for Closed
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(CircuitState.HalfOpen, breaker.CurrentState);

        nextStatus = HttpStatusCode.OK;
        for (int s = 1; s <= 2; s++)
        {
            using HttpResponseMessage okResp = await client.PostAsync(new Uri("https://sandbox.test/vnin"), new StringContent("{}"));
            Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
            Assert.Equal(CircuitState.HalfOpen, breaker.CurrentState);
        }

        using HttpResponseMessage thirdOk = await client.PostAsync(new Uri("https://sandbox.test/vnin"), new StringContent("{}"));
        Assert.Equal(HttpStatusCode.OK, thirdOk.StatusCode);
        Assert.Equal(CircuitState.Closed, breaker.CurrentState);
        Assert.Equal(8, inner.AttemptCount);
    }

    [Fact]
    public async Task AttemptTimeoutTripsBreakerIndependentlyWithoutAffectingSmsCircuit()
    {
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
        ResilienceOptions fastTimeoutOptions = new()
        {
            FailureThreshold = 1,
            MaxAttempts = 1,
            AttemptTimeout = TimeSpan.FromMilliseconds(25)
        };

        CircuitBreakerState ninBreaker = new("nin-verification", fastTimeoutOptions, clock);
        CircuitBreakerState smsBreaker = new("sms-gateway", fastTimeoutOptions, clock);

        StubHttpHandler hangingNinHandler = new(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        StubHttpHandler healthySmsHandler = new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        using IntegrationResilienceHandler ninResilience = new(ninBreaker, fastTimeoutOptions, clock) { InnerHandler = hangingNinHandler };
        using IntegrationResilienceHandler smsResilience = new(smsBreaker, fastTimeoutOptions, clock) { InnerHandler = healthySmsHandler };
        using HttpClient ninHttp = new(ninResilience);
        using HttpClient smsHttp = new(smsResilience);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            ninHttp.PostAsync(new Uri("https://sandbox.test/vnin"), new StringContent("{}")));

        Assert.Equal(CircuitState.Open, ninBreaker.CurrentState);
        Assert.Equal(CircuitState.Closed, smsBreaker.CurrentState);

        using HttpResponseMessage smsResponse = await smsHttp.PostAsync(new Uri("https://sms.test/messages"), new StringContent("{}"));
        Assert.Equal(HttpStatusCode.OK, smsResponse.StatusCode);
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class StubHttpHandler(Func<CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public int AttemptCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AttemptCount++;
            return responder(cancellationToken);
        }
    }
}
