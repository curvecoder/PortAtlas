namespace PortAtlas.Core.Resolution;

/// <summary>
/// Raised when a DNS cache file that is about to be rewritten cannot be read; the message names the file and the error.
/// </summary>
public sealed class DnsCacheUnreadableException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    public DnsCacheUnreadableException()
    {
    }

    /// <summary>
    /// Creates the exception with a message naming the cache file.
    /// </summary>
    /// <param name="message">Message shown to the operator.</param>
    public DnsCacheUnreadableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message naming the cache file, and the error met while reading it.
    /// </summary>
    /// <param name="message">Message shown to the operator.</param>
    /// <param name="innerException">Error met while reading or parsing the file.</param>
    public DnsCacheUnreadableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
