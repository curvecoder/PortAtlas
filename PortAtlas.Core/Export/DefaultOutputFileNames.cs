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

    /// <summary>HTML topology map written by <c>map</c> and by <c>audit</c>.</summary>
    public const string TopologyHtml = "portatlas_topology.html";

    /// <summary>Cache of reverse DNS and RDAP answers, read and written by <c>audit</c>, <c>resolve</c> and <c>lookup</c>.</summary>
    public const string DnsCache = "portatlas_dns_cache.json";
}
