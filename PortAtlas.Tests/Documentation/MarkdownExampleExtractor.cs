using System.Text.RegularExpressions;

namespace PortAtlas.Tests.Documentation;

/// <summary>
/// A <c>PortAtlas</c> command line written in a documentation page.
/// </summary>
/// <param name="LineNumber">One-based line of the page.</param>
/// <param name="Arguments">Text after the executable name.</param>
internal sealed record DocumentedCommandLine(int LineNumber, string Arguments);

/// <summary>
/// A fenced <c>json</c> block of a documentation page.
/// </summary>
/// <param name="LineNumber">One-based line of the opening fence.</param>
/// <param name="Json">Content of the block, without the indentation of the fence.</param>
internal sealed record DocumentedJsonBlock(int LineNumber, string Json);

/// <summary>
/// Reads the command lines and the JSON examples of a Markdown page.
/// </summary>
internal static partial class MarkdownExampleExtractor
{
    // A "text" block holds a synopsis with placeholders such as <path> and [options], which is not a command to run.
    private const string SynopsisLanguage = "text";

    public static IReadOnlyList<DocumentedCommandLine> ExtractCommandLines(string markdown)
    {
        var commandLines = new List<DocumentedCommandLine>();
        foreach (var line in ReadLines(markdown))
        {
            if (line.Fence is not null)
            {
                if (line.Fence.Language != SynopsisLanguage && CommandLinePattern().Match(line.Text.Trim()) is { Success: true } match)
                {
                    commandLines.Add(new DocumentedCommandLine(line.Number, match.Groups["arguments"].Value.Trim()));
                }

                continue;
            }

            commandLines.AddRange(InlineCodePattern().Matches(line.Text)
                .Select(span => CommandLinePattern().Match(span.Groups["code"].Value))
                .Where(match => match.Success && !IsPlaceholderOrMessage(match.Groups["arguments"].Value))
                .Select(match => new DocumentedCommandLine(line.Number, match.Groups["arguments"].Value.Trim())));
        }

        return commandLines;
    }

    public static IReadOnlyList<DocumentedJsonBlock> ExtractJsonBlocks(string markdown) =>
        [.. ReadLines(markdown)
            .Where(line => line.Fence is { Language: "json" })
            .GroupBy(line => line.Fence)
            .Select(block => new DocumentedJsonBlock(
                block.Key!.LineNumber,
                string.Join('\n', block.Select(line => RemoveIndentation(line.Text, block.Key.Indentation)))))];

    // An inline span such as `PortAtlas was not found …`, `PortAtlas.exe was not found … path.` or
    // `PortAtlas v<version>` quotes a message, not a command: a command line has no placeholder and ends without a period.
    private static bool IsPlaceholderOrMessage(string arguments) =>
        arguments.Contains('<', StringComparison.Ordinal)
        || arguments.Contains('…', StringComparison.Ordinal)
        || arguments.TrimEnd().EndsWith('.');

    private static string RemoveIndentation(string text, int indentation) =>
        text.Length >= indentation && string.IsNullOrWhiteSpace(text[..indentation]) ? text[indentation..] : text.TrimStart();

    private static IEnumerable<MarkdownLine> ReadLines(string markdown)
    {
        FencedBlock? fence = null;
        var number = 0;
        foreach (var text in markdown.Split('\n').Select(text => text.TrimEnd('\r')))
        {
            number++;
            if (FencePattern().Match(text) is { Success: true } fenceMatch)
            {
                fence = fence is null
                    ? new FencedBlock(number, fenceMatch.Groups["indentation"].Length, fenceMatch.Groups["language"].Value)
                    : null;
                continue;
            }

            yield return new MarkdownLine(number, text, fence);
        }
    }

    [GeneratedRegex(@"^(?<indentation>\s*)```(?<language>\w*)\s*$")]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"`(?<code>[^`]+)`")]
    private static partial Regex InlineCodePattern();

    // The executable is named alone, after sudo, with a path (./, .\, C:\Tools\PortAtlas\) or in a systemd ExecStart line.
    [GeneratedRegex(@"^(?:sudo\s+|ExecStart=)?(?:\S*[/\\])?PortAtlas(?:\.exe)?\s+(?<arguments>\S.*)$")]
    private static partial Regex CommandLinePattern();

    private sealed record FencedBlock(int LineNumber, int Indentation, string Language);

    private sealed record MarkdownLine(int Number, string Text, FencedBlock? Fence);
}
