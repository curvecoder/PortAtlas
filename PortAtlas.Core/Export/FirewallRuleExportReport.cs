namespace PortAtlas.Core.Export;

/// <summary>
/// Metric report detailing the outcome of a firewall rule matrix export.
/// </summary>
/// <param name="SourceFiles">List of source audit log files ingested.</param>
/// <param name="DestinationCsvPath">Target output CSV matrix path on disk.</param>
/// <param name="TotalRulesExported">Number of consolidated firewall rules generated.</param>
/// <param name="TotalRawFlowsScanned">Number of raw flow connection events analyzed.</param>
/// <param name="PairsCsvPath">Full path of the pair matrix, or <see langword="null"/> when it was not requested.</param>
/// <param name="TotalPairsExported">Number of source and destination pairs written to the pair matrix.</param>
public sealed record FirewallRuleExportReport(
    IReadOnlyList<string> SourceFiles,
    string DestinationCsvPath,
    int TotalRulesExported,
    int TotalRawFlowsScanned,
    string? PairsCsvPath = null,
    int TotalPairsExported = 0)
{
    /// <summary>
    /// Flow matrix written, or <see langword="null"/> when it was not requested.
    /// </summary>
    public ExportedMatrixFile? Flows { get; init; }

    /// <summary>
    /// Host rule matrix written, or <see langword="null"/> when it was not requested.
    /// </summary>
    public ExportedMatrixFile? HostRules { get; init; }

    /// <summary>
    /// Listening socket matrix written, or <see langword="null"/> when it was not requested.
    /// </summary>
    public ExportedMatrixFile? Listeners { get; init; }

    /// <summary>
    /// Group rule matrix written, or <see langword="null"/> when it was not requested.
    /// </summary>
    public ExportedMatrixFile? GroupRules { get; init; }
}
