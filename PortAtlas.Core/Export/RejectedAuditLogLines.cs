using System.Globalization;

namespace PortAtlas.Core.Export;

/// <summary>
/// Lines of an audit log left out of an output, such as the lines of a topology map without a valid local and remote port.
/// </summary>
/// <param name="Path">Path of the audit log.</param>
/// <param name="LineNumbers">One-based line numbers of the lines left out, in file order.</param>
/// <param name="Reason">Why the lines were left out, completing "Skipped N line(s) of &lt;path&gt;".</param>
public sealed record RejectedAuditLogLines(string Path, IReadOnlyList<int> LineNumbers, string Reason = "without a valid local and remote port")
{
    private const int ListedLineLimit = 10;

    /// <summary>
    /// Describes the lines left out in one console line, listing the first line numbers.
    /// </summary>
    /// <returns>The description, naming the audit log.</returns>
    public string Describe()
    {
        var listed = string.Join(", ", LineNumbers.Take(ListedLineLimit).Select(line => line.ToString(CultureInfo.InvariantCulture)));
        var more = LineNumbers.Count > ListedLineLimit
            ? string.Create(CultureInfo.InvariantCulture, $" and {LineNumbers.Count - ListedLineLimit} more")
            : string.Empty;
        return $"Skipped {LineNumbers.Count} line(s) of {Path} {Reason}: {(LineNumbers.Count == 1 ? "line" : "lines")} {listed}{more}.";
    }
}
