using System.Text;
using FarmBasics.BuildingBlocks.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace FarmBasics.UnitTests.Logging;

public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly List<LogEvent> _events = [];
    private readonly Lock _sync = new();

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        lock (_sync)
        {
            _events.Add(logEvent);
        }
    }

    public IReadOnlyList<string> RenderJsonLines()
    {
        RenderedCompactJsonFormatter formatter = new();
        lock (_sync)
        {
            List<string> lines = new(_events.Count);
            foreach (LogEvent evt in _events)
            {
                using StringWriter writer = new();
                formatter.Format(evt, writer);
                lines.Add(writer.ToString());
            }

            return lines;
        }
    }

    public string RenderAllText()
    {
        StringBuilder sb = new();
        foreach (string line in RenderJsonLines())
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    public static Logger CreateTestLogger(InMemoryLogSink sink)
    {
        return new LoggerConfiguration()
            .ConfigureFarmBasicsLogging(serviceName: "FarmBasics.Tests", writeJsonConsole: false, additionalSinks: sink)
            .CreateLogger();
    }
}
