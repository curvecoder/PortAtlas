namespace PortAtlas.Core.Export;

/// <summary>
/// Chooses the audit logs a command reads among the files its inputs designate. A directory of collected logs also
/// holds what the commands wrote there (enriched copies, an NDJSON conversion, a firewall matrix): reading them all
/// counts each observation several times and turns the rows of a matrix into flows.
/// </summary>
public static class AuditLogSelector
{
    /// <summary>
    /// Suffix that <c>resolve</c> adds to the name of a log to name its enriched copy.
    /// </summary>
    internal const string EnrichedCopySuffix = "_enriched";

    /// <summary>
    /// Resolves the inputs to files, then leaves out the files that are not audit logs and the files that hold an
    /// audit already kept. A file whose own path is an input is always kept, whatever it contains.
    /// </summary>
    /// <param name="pathsOrPatterns">File paths, directory paths or wildcard patterns, as for <see cref="AuditInputPathResolver.Resolve"/>.</param>
    /// <param name="directoryExtensions">Extensions kept when an input is a directory.</param>
    /// <param name="duplicatePolicy">Which files are kept when several hold the same audit.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The audit logs to read and the files left out.</returns>
    /// <exception cref="ArgumentNullException">When a reference argument is null.</exception>
    /// <exception cref="IOException">When a file of a directory or of a pattern cannot be opened.</exception>
    /// <exception cref="UnauthorizedAccessException">When a file of a directory or of a pattern is denied to the user.</exception>
    public static async Task<AuditLogSelection> SelectAsync(
        IEnumerable<string> pathsOrPatterns,
        IReadOnlyCollection<string> directoryExtensions,
        AuditDuplicatePolicy duplicatePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathsOrPatterns);
        ArgumentNullException.ThrowIfNull(directoryExtensions);

        var inputs = AuditInputPathResolver.ResolveWithOrigin(pathsOrPatterns, directoryExtensions);
        var skippedByPath = new Dictionary<string, SkippedAuditInput>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<AuditLogCandidate>();

        foreach (var input in inputs)
        {
            if (input.IsNamed || await AuditLogRecognizer.IsAuditLogAsync(input.FullPath, cancellationToken).ConfigureAwait(false))
            {
                candidates.Add(AuditLogCandidate.From(input));
            }
            else
            {
                skippedByPath[input.FullPath] = new SkippedAuditInput(input.FullPath, AuditInputSkipReason.NotAnAuditLog);
            }
        }

        foreach (var sameAudit in candidates.GroupBy(candidate => candidate.AuditKey(duplicatePolicy), StringComparer.OrdinalIgnoreCase))
        {
            var kept = ChooseKeptFile(sameAudit, duplicatePolicy);

            foreach (var duplicate in sameAudit.Where(candidate => !candidate.IsNamed && candidate != kept))
            {
                skippedByPath[duplicate.FullPath] = new SkippedAuditInput(duplicate.FullPath, AuditInputSkipReason.SameAuditAsAnotherFile, kept.FullPath);
            }
        }

        return new AuditLogSelection(
            inputs.Where(input => !skippedByPath.ContainsKey(input.FullPath)).Select(input => input.FullPath).ToList(),
            inputs.Where(input => skippedByPath.ContainsKey(input.FullPath)).Select(input => skippedByPath[input.FullPath]).ToList());
    }

    private static AuditLogCandidate ChooseKeptFile(IEnumerable<AuditLogCandidate> sameAudit, AuditDuplicatePolicy duplicatePolicy)
    {
        var preferred = sameAudit.Order(PreferenceOrder(duplicatePolicy)).First();

        // A command that adds up what it reads takes the named file as the file of its audit, so that a directory
        // given next to it does not bring the same audit a second time. A command that rewrites each log keeps
        // the original even when a copy of it is named: the copy is read as well, not in its place.
        return duplicatePolicy == AuditDuplicatePolicy.OneFilePerAudit
            ? sameAudit.FirstOrDefault(candidate => candidate.IsNamed) ?? preferred
            : preferred;
    }

    private static Comparer<AuditLogCandidate> PreferenceOrder(AuditDuplicatePolicy duplicatePolicy) => duplicatePolicy switch
    {
        // The enriched copy carries the host names that resolve added; the CSV is the log every command reads,
        // while an NDJSON of the same name may be an older conversion of it.
        AuditDuplicatePolicy.OneFilePerAudit => Comparer<AuditLogCandidate>.Create((left, right) => CompareInOrder(
            right.IsEnrichedCopy.CompareTo(left.IsEnrichedCopy),
            left.IsNdjson.CompareTo(right.IsNdjson),
            left.CompareNameTo(right))),

        // The original is the log to enrich: its copy is the output of an earlier run.
        _ => Comparer<AuditLogCandidate>.Create((left, right) => left.CompareNameTo(right))
    };

    private static int CompareInOrder(params int[] comparisons) => comparisons.FirstOrDefault(comparison => comparison != 0);

    /// <summary>
    /// A file kept so far, with what its name says about the audit it holds.
    /// </summary>
    /// <param name="FullPath">Full path of the file.</param>
    /// <param name="IsNamed">True when an input is the path of this very file.</param>
    /// <param name="AuditPath">Path of the file without its extension and without the enriched copy suffix.</param>
    /// <param name="EnrichmentDepth">Number of enriched copy suffixes in the name (a copy of a copy has two).</param>
    private sealed record AuditLogCandidate(string FullPath, bool IsNamed, string AuditPath, int EnrichmentDepth)
    {
        public bool IsEnrichedCopy => EnrichmentDepth > 0;

        public bool IsNdjson => AuditLogRecognizer.IsNdjsonPath(FullPath);

        public static AuditLogCandidate From(ResolvedInputFile input)
        {
            var name = Path.GetFileNameWithoutExtension(input.FullPath);
            var depth = 0;
            while (name.Length > EnrichedCopySuffix.Length && name.EndsWith(EnrichedCopySuffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^EnrichedCopySuffix.Length];
                depth++;
            }

            return new AuditLogCandidate(input.FullPath, input.IsNamed, Path.Combine(Path.GetDirectoryName(input.FullPath) ?? string.Empty, name), depth);
        }

        // Two files hold the same audit when they share a directory and a name; a command that rewrites each log
        // also tells the CSV from the NDJSON.
        public string AuditKey(AuditDuplicatePolicy duplicatePolicy) => duplicatePolicy == AuditDuplicatePolicy.EachOriginalLog
            ? AuditPath + Path.GetExtension(FullPath)
            : AuditPath;

        // The fewest suffixes first, then the name, so that the choice does not depend on the order of the inputs.
        public int CompareNameTo(AuditLogCandidate other) => CompareInOrder(
            EnrichmentDepth.CompareTo(other.EnrichmentDepth),
            StringComparer.OrdinalIgnoreCase.Compare(FullPath, other.FullPath));
    }
}
