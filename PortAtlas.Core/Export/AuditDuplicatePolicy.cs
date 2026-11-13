namespace PortAtlas.Core.Export;

/// <summary>
/// What a command does with the files of a directory that hold the same audit: the log, its enriched copy
/// (<c>_enriched</c> suffix) and its twin in the other format (CSV and NDJSON of the same name).
/// </summary>
public enum AuditDuplicatePolicy
{
    /// <summary>
    /// The command adds up what it reads: one file is kept per audit, the enriched copy before the original, then
    /// the CSV before the NDJSON. A file whose own path is an input is the file kept for its audit.
    /// </summary>
    OneFilePerAudit,

    /// <summary>
    /// The command rewrites each log: the CSV and the NDJSON of an audit are both kept, and only an enriched copy is
    /// left out when the log it was made from is present. A copy whose own path is an input is kept next to that log.
    /// </summary>
    EachOriginalLog
}
