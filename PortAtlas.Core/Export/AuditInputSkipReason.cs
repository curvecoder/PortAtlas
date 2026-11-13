namespace PortAtlas.Core.Export;

/// <summary>
/// Why a file found in a directory, or matched by a wildcard pattern, is not read.
/// </summary>
public enum AuditInputSkipReason
{
    /// <summary>
    /// The first line of the file is not the header of an audit log (a firewall matrix, a file of another tool, an
    /// empty file).
    /// </summary>
    NotAnAuditLog,

    /// <summary>
    /// Another file kept by the command holds the same audit.
    /// </summary>
    SameAuditAsAnotherFile
}
