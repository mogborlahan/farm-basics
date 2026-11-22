using System.Net;
using System.Security.Claims;
using FarmBasics.Api.Platform;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FarmBasics.UnitTests.Platform;

public sealed class RateLimitPolicyTests
{
    [Fact]
    public void PartitionKeyPrefersAuthenticatedSubOverIpAndFallsBackSafely()
    {
        DefaultHttpContext ctx = new();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");

        Assert.Equal("192.0.2.10", RateLimitPolicies.ResolvePartitionKey(ctx));

        ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "farmer-123")], "Bearer"));
        Assert.Equal("farmer-123", RateLimitPolicies.ResolvePartitionKey(ctx));

        DefaultHttpContext anonymousCtx = new();
        Assert.Equal("anonymous", RateLimitPolicies.ResolvePartitionKey(anonymousCtx));
    }

    [Fact]
    public void NamedPolicyThresholdsMatchDocument03Section15()
    {
        Assert.Equal(100, RateLimitPolicies.GlobalPermitLimitPerMinute);
        Assert.Equal(5, RateLimitPolicies.PestReportLimitPer24Hours);
        Assert.Equal(5, RateLimitPolicies.OtpVerifyLimitPer15Minutes);
        Assert.Equal(30, RateLimitPolicies.AuditSearchLimitPerMinute);
    }
}
