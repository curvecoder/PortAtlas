namespace PortAtlas.Core.Export;

/// <summary>
/// Outcome of <see cref="AuditLogSelector.SelectAsync"/>: the audit logs to read and the files left out.
/// </summary>
/// <param name="Files">Distinct full paths of the audit logs to read, in input order.</param>
/// <param name="Skipped">Files left out, in input order, each with its reason.</param>
public sealed record AuditLogSelection(IReadOnlyList<string> Files, IReadOnlyList<SkippedAuditInput> Skipped);
