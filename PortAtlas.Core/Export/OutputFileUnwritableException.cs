namespace PortAtlas.Core.Export;

/// <summary>
/// Raised when an output file of a command, or its folder, cannot be created or written; the message names the file
/// and the error.
/// </summary>
public sealed class OutputFileUnwritableException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    public OutputFileUnwritableException()
    {
    }

    /// <summary>
    /// Creates the exception with a message naming the output file.
    /// </summary>
    /// <param name="message">Message shown to the operator.</param>
    public OutputFileUnwritableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message naming the output file, and the error met while writing it.
    /// </summary>
    /// <param name="message">Message shown to the operator.</param>
    /// <param name="innerException">Error met while creating the folder or writing the file.</param>
    public OutputFileUnwritableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Runs the writing of an output file, and reports a file system error as this exception naming the file.
    /// </summary>
    /// <param name="outputPath">Path of the output file, as given by the operator.</param>
    /// <param name="write">Creates the folder and writes the file; it reads no other file.</param>
    /// <exception cref="OutputFileUnwritableException">Thrown when the folder or the file cannot be created or written.</exception>
    public static async Task GuardAsync(string outputPath, Func<Task> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(write);

        try
        {
            await write().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw Naming(outputPath, ex);
        }
    }

    /// <summary>
    /// Creates the exception for an output file that cannot be written.
    /// </summary>
    /// <param name="outputPath">Path of the output file, as given by the operator.</param>
    /// <param name="cause">Error met while creating the folder or writing the file.</param>
    /// <returns>The exception, whose message names the full path of the file and the error.</returns>
    public static OutputFileUnwritableException Naming(string outputPath, Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return new OutputFileUnwritableException($"The output '{Path.GetFullPath(outputPath)}' cannot be written: {cause.Message}", cause);
    }
}
