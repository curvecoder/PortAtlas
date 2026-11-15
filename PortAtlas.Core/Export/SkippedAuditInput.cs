namespace PortAtlas.Core.Export;

/// <summary>
/// A file designated by a directory or a wildcard pattern and left out of the files a command reads.
/// </summary>
/// <param name="Path">Full path of the file left out.</param>
/// <param name="Reason">Why the file is left out.</param>
/// <param name="KeptPath">Full path of the file kept for the same audit; null when the file is not an audit log.</param>
public sealed record SkippedAuditInput(string Path, AuditInputSkipReason Reason, string? KeptPath = null)
{
    /// <summary>
    /// Builds the sentence shown to the user for this file.
    /// </summary>
    /// <returns>One line naming the file and the reason.</returns>
    public string Describe() => Reason switch
    {
        AuditInputSkipReason.SameAuditAsAnotherFile => $"Skipped {Path}: same audit as {KeptPath}, the file kept.",
        AuditInputSkipReason.Unreadable => $"Skipped {Path}: the file cannot be read (it may be in use by another program, or access is denied).",
        _ => $"Skipped {Path}: its first line is not the header of an audit log."
    };
}
