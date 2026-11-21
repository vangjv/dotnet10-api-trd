using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace Company.Product.Service.Api.Observability;

internal sealed class CanonicalLogFormatter(IConfiguration configuration) : ITextFormatter
{
    private readonly string _service = configuration["DD_SERVICE"] ?? "company-product-service";
    private readonly string _environment = configuration["DD_ENV"] ?? "local";
    private readonly string _version = configuration["DD_VERSION"] ?? "local";

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var request = Read(logEvent, "EventName") is "request.completed";
        var fields = new Dictionary<string, object?>
        {
            ["timestamp"] = logEvent.Timestamp.UtcDateTime,
            ["status"] = logEvent.Level switch
            {
                LogEventLevel.Verbose or LogEventLevel.Debug => "debug",
                LogEventLevel.Warning => "warn",
                LogEventLevel.Error => "error",
                LogEventLevel.Fatal => "critical",
                _ => "info",
            },
            ["source"] = "csharp",
            ["service"] = _service,
            ["env"] = _environment,
            ["version"] = _version,
            ["event.category"] = request ? "http" : "application",
            ["event.name"] = request ? "request.completed" : "application.log",
            // Only the allowlisted request event carries rendered text. Never serialize arbitrary
            // framework templates/properties or exception messages that can contain sensitive data.
            ["message"] = request ? "Completed HTTP request" : "Application diagnostic",
        };

        Copy("SourceContext", "logger.name");
        Copy("TraceId", "traceId");
        Copy("SpanId", "spanId");
        if (logEvent.TraceId is { } traceId)
            fields.TryAdd("traceId", traceId.ToHexString());
        if (logEvent.SpanId is { } spanId)
            fields.TryAdd("spanId", spanId.ToHexString());
        CopyCorrelation("trace_id");
        CopyCorrelation("span_id");
        if (logEvent.Exception is { } exception)
            fields["error.kind"] = exception.GetType().FullName;
        if (request)
        {
            Copy("RequestId", "http.request_id");
            Copy("RequestMethod", "http.method");
            Copy("RouteTemplate", "http.route");
            Copy("StatusCode", "http.status_code");
            if (Read(logEvent, "Elapsed") is double milliseconds)
                fields["duration"] = (long)(milliseconds * 1_000_000);
        }

        output.WriteLine(JsonSerializer.Serialize(fields));
        return;

        void Copy(string source, string target)
        {
            if (Read(logEvent, source) is { } value)
                fields[target] = value;
        }

        void CopyCorrelation(string id)
        {
            if ((Read(logEvent, "dd_" + id) ?? Read(logEvent, "dd." + id)) is string { Length: > 0 } value
                && value != "0")
                fields["dd_" + id] = value;
        }
    }

    private static object? Read(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar
            ? scalar.Value
            : null;
}
