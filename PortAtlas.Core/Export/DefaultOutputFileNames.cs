namespace PortAtlas.Core.Export;

/// <summary>
/// Names of the files written when no path is configured, relative to the working directory.
/// </summary>
/// <remarks>Versions before 5.0 wrote the same files under <c>netflow_*</c> names.</remarks>
public static class DefaultOutputFileNames
{
    /// <summary>CSV audit log written by <c>audit</c>.</summary>
    public const string AuditCsv = "portatlas_audit.csv";

    /// <summary>NDJSON log written by <c>export ndjson</c>.</summary>
    public const string AuditNdjson = "portatlas_audit.ndjson";

    /// <summary>Flow matrix written by <c>export rules</c>; versions before 6.0 named it <c>firewall_rules.csv</c>.</summary>
    public const string RulesCsv = "portatlas_rules.csv";

    /// <summary>Flow matrix written by <c>export rules --format markdown</c>.</summary>
    public const string RulesMarkdown = "portatlas_rules.md";

    /// <summary>HTML topology map written by <c>map</c> and by <c>audit</c>.</summary>
    public const string TopologyHtml = "portatlas_topology.html";

    /// <summary>Cache of reverse DNS and RDAP answers, read and written by <c>audit</c>, <c>resolve</c> and <c>lookup</c>.</summary>
    public const string DnsCache = "portatlas_dns_cache.json";

    /// <summary>
    /// Returns the name of the flow matrix written by <c>export rules</c> in a file format.
    /// </summary>
    /// <param name="format">File format of the matrix.</param>
    /// <returns><see cref="RulesMarkdown"/> for Markdown, otherwise <see cref="RulesCsv"/>.</returns>
    public static string RulesFor(MatrixFileFormat format) =>
        format == MatrixFileFormat.Markdown ? RulesMarkdown : RulesCsv;
}
