using System.Collections;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace FarmBasics.BuildingBlocks.Logging;

/// <summary>
/// Destructures structured objects, records, and dictionaries while recursively masking
/// sensitive property names defined in <see cref="SensitiveFieldDenylist"/>.
/// </summary>
public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    private const int MaxDepth = 6;

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        if (value is null || IsSimpleType(value.GetType()))
        {
            result = null!;
            return false;
        }

        result = DestructureValue(value, propertyValueFactory, depth: 0);
        return true;
    }

    private static LogEventPropertyValue DestructureValue(object? value, ILogEventPropertyValueFactory factory, int depth)
    {
        if (value is null)
        {
            return new ScalarValue(null);
        }

        Type type = value.GetType();
        if (depth >= MaxDepth || IsSimpleType(type))
        {
            return value is string text
                ? new ScalarValue(SensitiveFieldDenylist.ScrubSensitivePatterns(text))
                : factory.CreatePropertyValue(value, destructureObjects: false);
        }

        if (value is IDictionary dictionary)
        {
            List<KeyValuePair<ScalarValue, LogEventPropertyValue>> dictEntries = [];
            foreach (DictionaryEntry entry in dictionary)
            {
                string keyText = entry.Key?.ToString() ?? string.Empty;
                LogEventPropertyValue maskedValue = SensitiveFieldDenylist.IsSensitivePropertyName(keyText)
                    ? new ScalarValue(SensitiveFieldDenylist.RedactedMarker)
                    : DestructureValue(entry.Value, factory, depth + 1);
                dictEntries.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue(keyText), maskedValue));
            }

            return new DictionaryValue(dictEntries);
        }

        if (value is IEnumerable enumerable)
        {
            List<LogEventPropertyValue> elements = [];
            foreach (object? item in enumerable)
            {
                elements.Add(DestructureValue(item, factory, depth + 1));
            }

            return new SequenceValue(elements);
        }

        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        List<LogEventProperty> logProperties = new(properties.Length);

        foreach (PropertyInfo property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? rawPropValue = property.GetValue(value);
            LogEventPropertyValue propValue = SensitiveFieldDenylist.IsSensitivePropertyName(property.Name)
                ? new ScalarValue(SensitiveFieldDenylist.RedactedMarker)
                : DestructureValue(rawPropValue, factory, depth + 1);

            logProperties.Add(new LogEventProperty(property.Name, propValue));
        }

        return new StructureValue(logProperties, type.Name);
    }

    private static bool IsSimpleType(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
               || underlying.IsEnum
               || underlying == typeof(string)
               || underlying == typeof(decimal)
               || underlying == typeof(DateTime)
               || underlying == typeof(DateTimeOffset)
               || underlying == typeof(TimeSpan)
               || underlying == typeof(Guid)
               || underlying == typeof(Uri);
    }
}
