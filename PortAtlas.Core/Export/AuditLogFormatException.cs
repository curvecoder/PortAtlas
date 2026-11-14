namespace PortAtlas.Core.Export;

/// <summary>
/// Raised when an audit log cannot be read as records; the message names the log and the line.
/// </summary>
public sealed class AuditLogFormatException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    public AuditLogFormatException()
    {
    }

    /// <summary>
    /// Creates the exception with a message naming the log and the line.
    /// </summary>
    /// <param name="message">Message shown to the operator.</param>
    public AuditLogFormatException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message naming the log and the line, and the error of the record reader.
    /// </summary>
    /// <param name="message">Message shown to the operator.</param>
    /// <param name="innerException">Error of the record reader.</param>
    public AuditLogFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
