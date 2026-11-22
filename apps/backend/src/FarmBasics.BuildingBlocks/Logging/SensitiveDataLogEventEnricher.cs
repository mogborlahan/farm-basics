using Serilog.Core;
using Serilog.Events;

namespace FarmBasics.BuildingBlocks.Logging;

/// <summary>
/// Enricher applied globally to all Serilog events so scalar template properties
/// matching sensitive names or inline secret patterns are masked before reaching any sink.
/// </summary>
public sealed class SensitiveDataLogEventEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        List<string> propertyNames = [.. logEvent.Properties.Keys];
        foreach (string name in propertyNames)
        {
            LogEventPropertyValue current = logEvent.Properties[name];
            if (SensitiveFieldDenylist.IsSensitivePropertyName(name))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(SensitiveFieldDenylist.RedactedMarker)));
                continue;
            }

            if (TrySanitizeValue(current, out LogEventPropertyValue sanitized))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, sanitized));
            }
        }
    }

    private static bool TrySanitizeValue(LogEventPropertyValue value, out LogEventPropertyValue sanitized)
    {
        switch (value)
        {
            case ScalarValue { Value: string str }:
                string scrubbed = SensitiveFieldDenylist.ScrubSensitivePatterns(str);
                if (!string.Equals(str, scrubbed, StringComparison.Ordinal))
                {
                    sanitized = new ScalarValue(scrubbed);
                    return true;
                }
                break;

            case StructureValue structure:
                bool changedStruct = false;
                List<LogEventProperty> updatedProps = new(structure.Properties.Count);
                foreach (LogEventProperty prop in structure.Properties)
                {
                    if (SensitiveFieldDenylist.IsSensitivePropertyName(prop.Name))
                    {
                        updatedProps.Add(new LogEventProperty(prop.Name, new ScalarValue(SensitiveFieldDenylist.RedactedMarker)));
                        changedStruct = true;
                    }
                    else if (TrySanitizeValue(prop.Value, out LogEventPropertyValue inner))
                    {
                        updatedProps.Add(new LogEventProperty(prop.Name, inner));
                        changedStruct = true;
                    }
                    else
                    {
                        updatedProps.Add(prop);
                    }
                }
                if (changedStruct)
                {
                    sanitized = new StructureValue(updatedProps, structure.TypeTag);
                    return true;
                }
                break;

            case DictionaryValue dictionary:
                bool changedDict = false;
                List<KeyValuePair<ScalarValue, LogEventPropertyValue>> updatedEntries = new(dictionary.Elements.Count);
                foreach (KeyValuePair<ScalarValue, LogEventPropertyValue> kvp in dictionary.Elements)
                {
                    string keyName = kvp.Key.Value?.ToString() ?? string.Empty;
                    if (SensitiveFieldDenylist.IsSensitivePropertyName(keyName))
                    {
                        updatedEntries.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(kvp.Key, new ScalarValue(SensitiveFieldDenylist.RedactedMarker)));
                        changedDict = true;
                    }
                    else if (TrySanitizeValue(kvp.Value, out LogEventPropertyValue inner))
                    {
                        updatedEntries.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(kvp.Key, inner));
                        changedDict = true;
                    }
                    else
                    {
                        updatedEntries.Add(kvp);
                    }
                }
                if (changedDict)
                {
                    sanitized = new DictionaryValue(updatedEntries);
                    return true;
                }
                break;

            case SequenceValue sequence:
                bool changedSeq = false;
                List<LogEventPropertyValue> updatedItems = new(sequence.Elements.Count);
                foreach (LogEventPropertyValue item in sequence.Elements)
                {
                    if (TrySanitizeValue(item, out LogEventPropertyValue inner))
                    {
                        updatedItems.Add(inner);
                        changedSeq = true;
                    }
                    else
                    {
                        updatedItems.Add(item);
                    }
                }
                if (changedSeq)
                {
                    sanitized = new SequenceValue(updatedItems);
                    return true;
                }
                break;
        }

        sanitized = value;
        return false;
    }
}
