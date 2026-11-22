using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FarmBasics.UnitTests.Auth;

public sealed class JwtIdentityAndFailClosedTests : IDisposable
{
    private const string TestIssuer = "https://auth.test.farmbasics.local/realms/farm-basics";
    private static readonly string[] SpoofedRoles = ["SuperAdmin", "FinanceAdmin"];
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;
    private readonly WebApplicationFactory<Program> _factory;

    public JwtIdentityAndFailClosedTests()
    {
        _rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = "test-key-1" };

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
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

    [Fact]
    public async Task UnauthenticatedProtectedRequestsReturn401WithUniformErrorEnvelope()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage meResponse = await client.GetAsync(new Uri("/v1/auth/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);

        string body = await meResponse.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(body);
        Assert.Equal("AUTHENTICATION_REQUIRED", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("meta").GetProperty("requestId").GetString()));
    }

    [Fact]
    public async Task ValidJwtWithSpoofedAdminRoleClaimsStillFailsClosedOnAdminEndpoint()
    {
        using HttpClient client = _factory.CreateClient();
        string jwt = CreateSignedToken(
            subject: "farmer-subject-001",
            extraClaims: new Dictionary<string, object>
            {
                ["roles"] = SpoofedRoles,
                ["scope"] = "admin:all"
            });

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        HttpResponseMessage meResponse = await client.GetAsync(new Uri("/v1/auth/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        string meBody = await meResponse.Content.ReadAsStringAsync();
        using JsonDocument meDoc = JsonDocument.Parse(meBody);
        Assert.Equal("farmer-subject-001", meDoc.RootElement.GetProperty("data").GetProperty("subject").GetString());
        Assert.Equal("live_database_recheck_required", meDoc.RootElement.GetProperty("data").GetProperty("permissionsSource").GetString());

        HttpResponseMessage adminResponse = await client.GetAsync(new Uri("/v1/admin/ping", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);

        string adminBody = await adminResponse.Content.ReadAsStringAsync();
        using JsonDocument adminDoc = JsonDocument.Parse(adminBody);
        Assert.Equal("PERMISSION_DENIED", adminDoc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task LiveHealthEndpointReturns200AndMetricsExposesRealCounters()
    {
        using HttpClient client = _factory.CreateClient();

        HttpResponseMessage live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        HttpResponseMessage readyWithoutDb = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyWithoutDb.StatusCode);

        HttpResponseMessage metrics = await client.GetAsync(new Uri("/metrics", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);

        string metricsText = await metrics.Content.ReadAsStringAsync();
        Assert.Contains("farmbasics_http_request_duration_seconds", metricsText, StringComparison.Ordinal);
        Assert.Contains("farmbasics_http_requests_total", metricsText, StringComparison.Ordinal);
    }

    private string CreateSignedToken(string subject, IDictionary<string, object> extraClaims)
    {
        SigningCredentials credentials = new(_signingKey, SecurityAlgorithms.RsaSha256);
        DateTime now = DateTime.UtcNow;

        Dictionary<string, object> claims = new(extraClaims)
        {
            [JwtRegisteredClaimNames.Sub] = subject,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
        };

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = TestIssuer,
            Audience = "farm-basics-api",
            Subject = new ClaimsIdentity([new Claim("sub", subject)]),
            Claims = claims,
            NotBefore = now.AddMinutes(-1),
            Expires = now.AddMinutes(10),
            SigningCredentials = credentials
        };

        JsonWebTokenHandler handler = new();
        return handler.CreateToken(descriptor);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _rsa.Dispose();
    }
}
