using System.CommandLine;
using PortAtlas.Core.Export;

namespace PortAtlas.Cli.CommandLine.OptionSets;

/// <summary>
/// <c>-i/--input</c>: audit logs read by the <c>map</c>, <c>export</c> and <c>resolve</c> commands.
/// </summary>
internal sealed class AuditInputOptionSet
{
    private readonly Option<string[]> _input = new("--input", "-i")
    {
        Description = "Audit log to read: a file, a directory (the audit logs it contains, each audit once) or a wildcard pattern such as audits/nas-*.csv. Repeatable. Default: the audit output file of the configuration.",
        HelpName = "path"
    };

    public void AddTo(Command command) => command.Options.Add(_input);

    /// <summary>
    /// Tells whether the user named audit logs, as opposed to the configuration default.
    /// </summary>
    /// <param name="parseResult">Parsed command line.</param>
    public bool HasExplicitInput(ParseResult parseResult) => parseResult.GetValue(_input) is { Length: > 0 };

    /// <summary>
    /// Tells whether a file is given by its own path to the inputs, rather than found in a directory or a pattern.
    /// </summary>
    /// <param name="parseResult">Parsed command line.</param>
    /// <param name="defaultInput">Input used when <c>--input</c> is absent.</param>
    /// <param name="fullPath">Full path of the file.</param>
    public bool NamesFile(ParseResult parseResult, string defaultInput, string fullPath)
    {
        var requested = parseResult.GetValue(_input) is { Length: > 0 } given ? given : [defaultInput];
        return requested.Any(item => File.Exists(item) && string.Equals(Path.GetFullPath(item), fullPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Expands the inputs into the audit logs to read, and names on the console each file of a directory or of a
    /// pattern that is left out.
    /// </summary>
    /// <param name="parseResult">Parsed command line.</param>
    /// <param name="defaultInput">Input used when <c>--input</c> is absent.</param>
    /// <param name="directoryExtensions">Extensions read when an input is a directory.</param>
    /// <param name="duplicatePolicy">Which files are kept when several hold the same audit.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Full paths of the audit logs to read, in input order; empty when the inputs designate none.</returns>
    public async Task<IReadOnlyList<string>> ResolveFilesAsync(
        ParseResult parseResult,
        string defaultInput,
        IReadOnlyCollection<string> directoryExtensions,
        AuditDuplicatePolicy duplicatePolicy,
        CancellationToken cancellationToken)
    {
        var requested = parseResult.GetValue(_input) is { Length: > 0 } given ? given : [defaultInput];
        var selection = await AuditLogSelector.SelectAsync(requested, directoryExtensions, duplicatePolicy, cancellationToken).ConfigureAwait(false);

        foreach (var skipped in selection.Skipped)
        {
            Console.WriteLine(skipped.Describe());
        }

        if (selection.Skipped.Count > 0)
        {
            Console.WriteLine("A file whose own path is given to --input is always read.");
            Console.WriteLine();
        }

        return selection.Files;
    }

    /// <summary>
    /// Describes the inputs for the "no audit file found" error message.
    /// </summary>
    public string Describe(ParseResult parseResult, string defaultInput) =>
        parseResult.GetValue(_input) is { Length: > 0 } given ? string.Join(", ", given) : defaultInput;
}
