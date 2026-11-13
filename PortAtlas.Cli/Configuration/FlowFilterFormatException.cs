namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Raised when a port or an address given to a flow filter, on the command line or in the configuration file,
/// cannot be read. The command stops: a filter left out without a word would export flows the operator asked to
/// remove, or remove all of them.
/// </summary>
public sealed class FlowFilterFormatException : FormatException
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    public FlowFilterFormatException()
    {
    }

    /// <summary>
    /// Creates the exception with a message naming the option, the configuration key and the value.
    /// </summary>
    /// <param name="message">Message shown to the operator.</param>
    public FlowFilterFormatException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message and the underlying cause.
    /// </summary>
    /// <param name="message">Message shown to the operator.</param>
    /// <param name="innerException">Underlying cause.</param>
    public FlowFilterFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
