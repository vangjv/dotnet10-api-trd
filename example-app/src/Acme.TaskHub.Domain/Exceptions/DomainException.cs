namespace Acme.TaskHub.Domain.Exceptions;

/// <summary>
/// Thrown when a domain business rule or lifecycle rule is violated.
/// Mapped to HTTP 409 Conflict by the global exception handler.
/// Use the <see cref="DomainValidationException"/> subclass for field-level
/// validation violations, which map to HTTP 422 Unprocessable Entity.
/// </summary>
public class DomainException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class.
    /// </summary>
    /// <param name="errorCode">A stable, machine-readable error code.</param>
    /// <param name="message">A human-readable description of the violation.</param>
    public DomainException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Gets a stable, machine-readable error code for the violation.
    /// </summary>
    public string ErrorCode { get; }
}
