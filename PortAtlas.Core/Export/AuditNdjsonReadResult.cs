using PortAtlas.Core.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Entries read from an NDJSON audit log, with the lines that held none.
/// </summary>
/// <param name="Entries">Entries in file order.</param>
/// <param name="UnreadableLineNumbers">One-based numbers of the non-blank lines that are not a JSON audit record, in file order.</param>
public sealed record AuditNdjsonReadResult(IReadOnlyList<NetworkAuditEntry> Entries, IReadOnlyList<int> UnreadableLineNumbers);
