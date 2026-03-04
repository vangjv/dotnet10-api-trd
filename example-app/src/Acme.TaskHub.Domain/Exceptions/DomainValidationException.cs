namespace Acme.TaskHub.Domain.Exceptions;

/// <summary>
/// Thrown when a field-level domain validation constraint is violated
/// (e.g., a value exceeds its maximum length or is in an invalid format).
/// Mapped to HTTP 422 Unprocessable Entity by the global exception handler.
/// </summary>
/// <remarks>
/// Use this for validation errors that originate inside domain entity methods but represent
/// input constraint violations rather than business-rule / lifecycle violations.
/// Use the base <see cref="DomainException"/> directly for lifecycle or business-rule violations
/// that map to HTTP 409 Conflict (e.g., invalid status transitions, mutating a terminal item).
/// </remarks>
public sealed class DomainValidationException : DomainException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DomainValidationException"/> class.
    /// </summary>
    /// <param name="errorCode">A stable, machine-readable error code (e.g. <c>TITLE_TOO_LONG</c>).</param>
    /// <param name="message">A human-readable description of the constraint violation.</param>
    public DomainValidationException(string errorCode, string message)
        : base(errorCode, message)
    {
    }
}
