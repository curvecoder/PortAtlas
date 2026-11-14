namespace PortAtlas.Core.Resolution;

/// <summary>
/// Thrown when the IP owner registry gave no usable answer (unreachable, timed out, error status, or queries suspended
/// after repeated failures). Unlike a <see langword="null"/> result, which means the registry holds no record, this
/// outcome says nothing about the address and must not be cached.
/// </summary>
public sealed class IpOwnerLookupUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpOwnerLookupUnavailableException"/> class.
    /// </summary>
    public IpOwnerLookupUnavailableException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpOwnerLookupUnavailableException"/> class with a message.
    /// </summary>
    /// <param name="message">Description of the failed lookup.</param>
    public IpOwnerLookupUnavailableException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpOwnerLookupUnavailableException"/> class with a message and the
    /// network failure that caused it.
    /// </summary>
    /// <param name="message">Description of the failed lookup.</param>
    /// <param name="innerException">Network or timeout failure raised by the lookup.</param>
    public IpOwnerLookupUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
