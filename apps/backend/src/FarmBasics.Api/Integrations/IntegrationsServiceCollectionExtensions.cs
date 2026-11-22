using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FarmBasics.Api.Integrations;

internal static class IntegrationsServiceCollectionExtensions
{
    public static IServiceCollection AddFarmBasicsIntegrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        ResilienceOptions defaultResilience = new();
        CircuitBreakerState ninBreaker = new("nin-verification", defaultResilience);
        CircuitBreakerState smsBreaker = new("sms-gateway", defaultResilience);

        services.AddKeyedSingleton("nin-verification", ninBreaker);
        services.AddKeyedSingleton("sms-gateway", smsBreaker);

        services.AddSingleton<SandboxNinVerificationClient>();
        services.AddSingleton<SandboxSmsGatewayClient>();
        services.AddSingleton<SandboxVoiceOtpGatewayClient>();
        services.AddSingleton<ILivenessVerificationClient, SandboxLivenessVerificationClient>();

        NinIntegrationOptions ninOptions = new();
        configuration.GetSection(NinIntegrationOptions.SectionName).Bind(ninOptions);

        services.AddHttpClient<NinVerificationHttpClient>(client =>
        {
            client.BaseAddress = new Uri(ninOptions.BaseUrl, UriKind.Absolute);
        })
        .AddHttpMessageHandler(sp => new IntegrationResilienceHandler(
            sp.GetRequiredKeyedService<CircuitBreakerState>("nin-verification"),
            defaultResilience,
            sp.GetRequiredService<TimeProvider>()));

        SmsIntegrationOptions smsOptions = new();
        configuration.GetSection(SmsIntegrationOptions.SectionName).Bind(smsOptions);

        services.AddHttpClient<SmsGatewayHttpClient>(client =>
        {
            client.BaseAddress = new Uri(smsOptions.BaseUrl, UriKind.Absolute);
        })
        .AddHttpMessageHandler(sp => new IntegrationResilienceHandler(
            sp.GetRequiredKeyedService<CircuitBreakerState>("sms-gateway"),
            defaultResilience,
            sp.GetRequiredService<TimeProvider>()));

        services.AddHttpClient<VoiceOtpHttpClient>(client =>
        {
            client.BaseAddress = new Uri(smsOptions.BaseUrl, UriKind.Absolute);
        })
        .AddHttpMessageHandler(sp => new IntegrationResilienceHandler(
            sp.GetRequiredKeyedService<CircuitBreakerState>("sms-gateway"),
            defaultResilience,
            sp.GetRequiredService<TimeProvider>()));

        bool useHttpNin = string.Equals(ninOptions.Mode, "Http", StringComparison.OrdinalIgnoreCase);
        if (useHttpNin)
        {
            services.AddTransient<INinVerificationClient>(sp => sp.GetRequiredService<NinVerificationHttpClient>());
        }
        else
        {
            services.AddSingleton<INinVerificationClient>(sp => sp.GetRequiredService<SandboxNinVerificationClient>());
        }

        bool useHttpSms = string.Equals(smsOptions.Mode, "Http", StringComparison.OrdinalIgnoreCase);
        if (useHttpSms)
        {
            services.AddTransient<ISmsGatewayClient>(sp => sp.GetRequiredService<SmsGatewayHttpClient>());
        }
        else
        {
            services.AddSingleton<ISmsGatewayClient>(sp => sp.GetRequiredService<SandboxSmsGatewayClient>());
        }

        string voiceMode = configuration["Integrations:Voice:Mode"] ?? smsOptions.Mode;
        bool useHttpVoice = string.Equals(voiceMode, "Http", StringComparison.OrdinalIgnoreCase);
        if (useHttpVoice)
        {
            services.AddTransient<IVoiceOtpGatewayClient>(sp => sp.GetRequiredService<VoiceOtpHttpClient>());
        }
        else
        {
            services.AddSingleton<IVoiceOtpGatewayClient>(sp => sp.GetRequiredService<SandboxVoiceOtpGatewayClient>());
        }

        return services;
    }
}
