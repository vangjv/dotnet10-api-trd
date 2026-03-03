namespace Acme.TaskHub.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant is violated.
/// Mapped to HTTP 409/422 by the global exception handler.
/// </summary>
public sealed class DomainException : Exception
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
