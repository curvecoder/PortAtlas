namespace PortAtlas.Core.Engine;

/// <summary>
/// Raised when the audit stops because its scan cycles failed too many times in a row; the inner exception is the
/// error of the last cycle.
/// </summary>
public sealed class ConsecutiveScanFailuresException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    public ConsecutiveScanFailuresException()
    {
    }

    /// <summary>
    /// Creates the exception with a message shown to the operator.
    /// </summary>
    /// <param name="message">Message shown to the operator.</param>
    public ConsecutiveScanFailuresException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message shown to the operator and the error of the last scan cycle.
    /// </summary>
    /// <param name="message">Message shown to the operator.</param>
    /// <param name="innerException">Error of the last scan cycle.</param>
    public ConsecutiveScanFailuresException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
