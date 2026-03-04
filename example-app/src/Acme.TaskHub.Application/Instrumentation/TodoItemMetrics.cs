using System.Diagnostics.Metrics;

namespace Acme.TaskHub.Application.Instrumentation;

/// <summary>
/// Default <see cref="ITodoItemMetrics"/> implementation backed by a .NET
/// <see cref="System.Diagnostics.Metrics.Meter"/> (TRD §9.2).
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="IMeterFactory"/> (BCL, .NET 8+) so the meter is properly shared with the
/// OpenTelemetry SDK and respects its lifecycle. Register this class as a singleton via
/// <c>AddApplication()</c>. The meter name must be referenced in
/// <c>Program.cs</c> via <c>metrics.AddMeter(<see cref="MeterName"/>)</c>.
/// </para>
/// <para>
/// In unit tests, mock <see cref="ITodoItemMetrics"/> with NSubstitute instead of constructing
/// this implementation — avoids pulling in a real <see cref="IMeterFactory"/>.
/// </para>
/// </remarks>
public sealed class TodoItemMetrics : ITodoItemMetrics
{
    /// <summary>
    /// The OpenTelemetry Meter name. Pass this to <c>metrics.AddMeter()</c> in the OTel
    /// setup so the SDK collects and exports these measurements.
    /// </summary>
    public const string MeterName = "Acme.TaskHub";

    private readonly Counter<long> _created;
    private readonly Counter<long> _statusTransitions;
    private readonly Counter<long> _deleted;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoItemMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">
    /// Factory injected by the host after <c>services.AddMetrics()</c> (or OpenTelemetry SDK) is
    /// called. Ensures the meter participates in graceful shutdown.
    /// </param>
    public TodoItemMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _created = meter.CreateCounter<long>(
            "todoitems.created",
            description: "Number of to-do items created.");

        _statusTransitions = meter.CreateCounter<long>(
            "todoitems.status_transitions",
            description: "Number of to-do item status transitions performed.");

        _deleted = meter.CreateCounter<long>(
            "todoitems.deleted",
            description: "Number of to-do items deleted.");
    }

    /// <inheritdoc />
    public void RecordCreated() => _created.Add(1);

    /// <inheritdoc />
    public void RecordStatusTransition(string toStatus) =>
        _statusTransitions.Add(1, new KeyValuePair<string, object?>("to_status", toStatus));

    /// <inheritdoc />
    public void RecordDeleted() => _deleted.Add(1);
}
