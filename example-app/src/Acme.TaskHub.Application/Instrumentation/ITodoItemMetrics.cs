namespace Acme.TaskHub.Application.Instrumentation;

/// <summary>
/// Application-level business metrics for <c>TodoItem</c> operations (TRD §9.2).
/// Inject this interface into application services so metrics can be verified in unit tests
/// without requiring a real <see cref="System.Diagnostics.Metrics.Meter"/> instance.
/// </summary>
public interface ITodoItemMetrics
{
    /// <summary>Records that a to-do item was created.</summary>
    void RecordCreated();

    /// <summary>
    /// Records a status transition.
    /// </summary>
    /// <param name="toStatus">The target status label (used as a dimension on the counter).</param>
    void RecordStatusTransition(string toStatus);

    /// <summary>Records that a to-do item was deleted.</summary>
    void RecordDeleted();
}
