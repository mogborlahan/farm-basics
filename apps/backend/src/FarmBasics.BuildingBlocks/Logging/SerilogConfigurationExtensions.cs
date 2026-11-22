using Serilog;
using Serilog.Core;
using Serilog.Formatting.Compact;

namespace FarmBasics.BuildingBlocks.Logging;

public static class SerilogConfigurationExtensions
{
    public static LoggerConfiguration ConfigureFarmBasicsLogging(
        this LoggerConfiguration loggerConfiguration,
        string serviceName = "FarmBasics.Api",
        bool writeJsonConsole = true,
        params ILogEventSink[] additionalSinks)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);

        loggerConfiguration
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", serviceName)
            .Destructure.With<SensitiveDataDestructuringPolicy>()
            .Enrich.With<SensitiveDataLogEventEnricher>();

        if (writeJsonConsole)
        {
            loggerConfiguration.WriteTo.Console(new RenderedCompactJsonFormatter());
        }

        foreach (ILogEventSink sink in additionalSinks)
        {
            loggerConfiguration.WriteTo.Sink(sink);
        }

        return loggerConfiguration;
    }
}
