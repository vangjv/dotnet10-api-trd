using System.Diagnostics;
using System.Text.Json;
using Company.Product.Service.Api.Observability;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Company.Product.Service.Tests.Integration;

public sealed class LoggingContractTests
{
    [Fact]
    public void Canonical_json_preserves_safe_correlation_and_omits_sensitive_fields()
    {
        using var output = new StringWriter();
        using var logger = new LoggerConfiguration()
            .WriteTo.Sink(new CaptureSink(output))
            .CreateLogger();
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();

        logger.ForContext("EventName", "request.completed")
            .ForContext("Elapsed", 12.4d)
            .ForContext("dd.trace_id", "1234567890123456789")
            .ForContext("dd.span_id", "123456789012345678")
            .ForContext("RouteTemplate", "/v1/customers/{customerId}")
            .ForContext("RequestPath", "/v1/customers/sensitive-id")
            .ForContext("Authorization", "sensitive-key")
            .Error(new InvalidOperationException("sensitive-exception"), "Completed HTTP request");

        var line = Assert.Single(output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("sensitive", line);
        using var document = JsonDocument.Parse(line);
        var fields = document.RootElement;
        Assert.Equal("error", fields.GetProperty("status").GetString());
        Assert.Equal("request.completed", fields.GetProperty("event.name").GetString());
        Assert.Equal(activity.TraceId.ToHexString(), fields.GetProperty("traceId").GetString());
        Assert.Equal(activity.SpanId.ToHexString(), fields.GetProperty("spanId").GetString());
        Assert.Equal("1234567890123456789", fields.GetProperty("dd_trace_id").GetString());
        Assert.Equal("123456789012345678", fields.GetProperty("dd_span_id").GetString());
        Assert.Equal(12_400_000, fields.GetProperty("duration").GetInt64());
        Assert.Equal("/v1/customers/{customerId}", fields.GetProperty("http.route").GetString());
    }

    private sealed class CaptureSink(TextWriter output) : ILogEventSink
    {
        private readonly CanonicalLogFormatter _formatter = new(new ConfigurationBuilder().Build());

        public void Emit(LogEvent logEvent) => _formatter.Format(logEvent, output);
    }
}
