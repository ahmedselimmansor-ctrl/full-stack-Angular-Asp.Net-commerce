using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace Sama.Shared.Infrastructure.Observability;

/// <summary>
/// Writes log events as the JSON shape Cloud Logging understands natively.
/// <para>
/// The Cloud Logging agent picks up stdout and promotes a handful of well-known fields:
/// <c>severity</c> drives the level filter in the console, and
/// <c>logging.googleapis.com/trace</c> links each entry to its trace so a request's logs and
/// its spans appear together. Serilog's stock compact format uses <c>@t</c>/<c>@m</c>/<c>@l</c>,
/// none of which the agent recognises — every entry would land as untyped DEFAULT severity.
/// </para>
/// </summary>
public sealed class GoogleCloudJsonFormatter(string projectId) : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        using var buffer = new MemoryStream(512);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("severity", ToSeverity(logEvent.Level));
            writer.WriteString("time", logEvent.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("message", logEvent.RenderMessage(CultureInfo.InvariantCulture));

            var activity = Activity.Current;
            if (activity is not null && !string.IsNullOrEmpty(projectId))
            {
                writer.WriteString(
                    "logging.googleapis.com/trace",
                    $"projects/{projectId}/traces/{activity.TraceId}");
                writer.WriteString("logging.googleapis.com/spanId", activity.SpanId.ToString());
                writer.WriteBoolean("logging.googleapis.com/trace_sampled", activity.Recorded);
            }

            if (logEvent.Exception is not null)
            {
                // Error Reporting groups on a field literally named "stack_trace".
                writer.WriteString("stack_trace", logEvent.Exception.ToString());
                writer.WriteString("exception_type", logEvent.Exception.GetType().FullName);
            }

            foreach (var (name, value) in logEvent.Properties)
            {
                writer.WritePropertyName(name);
                WriteValue(writer, value);
            }

            writer.WriteEndObject();
        }

        output.WriteLine(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static void WriteValue(Utf8JsonWriter writer, LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: null }:
                writer.WriteNullValue();
                break;
            case ScalarValue { Value: bool flag }:
                writer.WriteBooleanValue(flag);
                break;
            case ScalarValue { Value: int number }:
                writer.WriteNumberValue(number);
                break;
            case ScalarValue { Value: long number }:
                writer.WriteNumberValue(number);
                break;
            case ScalarValue { Value: double number }:
                writer.WriteNumberValue(number);
                break;
            case ScalarValue { Value: decimal number }:
                writer.WriteNumberValue(number);
                break;
            case ScalarValue scalar:
                writer.WriteStringValue(Convert.ToString(scalar.Value, CultureInfo.InvariantCulture));
                break;
            case SequenceValue sequence:
                writer.WriteStartArray();
                foreach (var element in sequence.Elements)
                {
                    WriteValue(writer, element);
                }

                writer.WriteEndArray();
                break;
            case StructureValue structure:
                writer.WriteStartObject();
                foreach (var property in structure.Properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static string ToSeverity(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "DEBUG",
        LogEventLevel.Debug => "DEBUG",
        LogEventLevel.Information => "INFO",
        LogEventLevel.Warning => "WARNING",
        LogEventLevel.Error => "ERROR",
        LogEventLevel.Fatal => "CRITICAL",
        _ => "DEFAULT",
    };
}
