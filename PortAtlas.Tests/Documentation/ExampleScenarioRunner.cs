using System.CommandLine.Parsing;
using System.Text.Json.Nodes;
using PortAtlas.Cli;
using PortAtlas.Cli.Configuration;
using PortAtlas.Core.Export;

namespace PortAtlas.Tests.Documentation;

/// <summary>
/// Outcome of an example scenario.
/// </summary>
/// <param name="ExitCode">Exit code of the command.</param>
/// <param name="Console">Text the command wrote to the console.</param>
/// <param name="WrittenFiles">Files the command created or changed, relative to the data set, with forward slashes.</param>
internal sealed record ExampleRun(int ExitCode, string Console, IReadOnlyList<string> WrittenFiles);

/// <summary>
/// Runs an example scenario through <see cref="Program.Main"/> on a copy of the homelab data set.
/// </summary>
internal static class ExampleScenarioRunner
{
    // A scenario is written as a reader types it in docs/examples/homelab. The run works on a copy of that folder, so
    // that a command writing next to its input (resolve) or to a default path never writes into the repository.
    private static readonly HashSet<string> PathOptions = new(StringComparer.Ordinal)
    {
        "-i", "--input", "--cache", "--config", "-o", "--output", "--output-cache", "--html", "--drawio"
    };

    // The data set also holds the scenarios and their committed outputs, which no command reads.
    private static readonly HashSet<string> NotCopied = new(StringComparer.Ordinal) { "output", "scenarios.json", "README.md", "README.fr.md" };

    /// <summary>
    /// Copies the data set into <paramref name="runDirectory"/>, runs the scenario there and lists the files it wrote.
    /// </summary>
    /// <param name="scenario">Scenario to run.</param>
    /// <param name="runDirectory">Empty folder that receives the copy of the data set.</param>
    /// <returns>The exit code, the console text and the files the command created or changed.</returns>
    public static async Task<ExampleRun> RunAsync(ExampleScenario scenario, string runDirectory)
    {
        CopyDataSet(runDirectory);
        var before = ReadFiles(runDirectory);
        var arguments = RewritePaths(CommandLineParser.SplitCommandLine(scenario.Command).ToArray(), runDirectory);

        var consoleOut = Console.Out;
        var consoleError = Console.Error;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        Console.SetError(captured);
        int exitCode;
        try
        {
            exitCode = await Program.Main(arguments);
        }
        finally
        {
            Console.SetOut(consoleOut);
            Console.SetError(consoleError);
        }

        var written = ReadFiles(runDirectory)
            .Where(file => !before.TryGetValue(file.Key, out var content) || !content.AsSpan().SequenceEqual(file.Value))
            .Select(file => file.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        return new ExampleRun(exitCode, captured.ToString(), written);
    }

    /// <summary>
    /// Lists the files of a folder and of its subfolders.
    /// </summary>
    /// <param name="directory">Folder to list; a missing folder holds no file.</param>
    /// <returns>The paths relative to <paramref name="directory"/>, with forward slashes, in ordinal order.</returns>
    public static IReadOnlyList<string> ListFiles(string directory) =>
        Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Select(path => Relative(directory, path)).Order(StringComparer.Ordinal)]
            : [];

    private static void CopyDataSet(string runDirectory)
    {
        var source = DocumentationPageProvider.HomelabExampleDirectory;
        foreach (var relative in ListFiles(source).Where(file => !NotCopied.Contains(file.Split('/')[0])))
        {
            var target = Path.Combine(runDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? runDirectory);
            File.Copy(Path.Combine(source, relative), target);
        }

        AnchorDefaultPaths(Path.Combine(runDirectory, AppConfigurationFile.DefaultPath), runDirectory);
    }

    // The default audit log and DNS cache are relative to the working directory, which is the folder of the test
    // binaries during a test run: they are anchored in the copy, where a reader running the command finds them.
    private static void AnchorDefaultPaths(string configurationPath, string runDirectory)
    {
        var configuration = JsonNode.Parse(File.ReadAllText(configurationPath))?.AsObject()
            ?? throw new InvalidDataException($"{configurationPath} holds no JSON object.");
        configuration[nameof(AppConfiguration.OutputCsvPath)] ??= Path.Combine(runDirectory, DefaultOutputFileNames.AuditCsv);
        configuration[nameof(AppConfiguration.DnsCacheFilePath)] ??= Path.Combine(runDirectory, DefaultOutputFileNames.DnsCache);
        File.WriteAllText(configurationPath, configuration.ToJsonString());
    }

    private static Dictionary<string, byte[]> ReadFiles(string directory) =>
        ListFiles(directory).ToDictionary(file => file, file => File.ReadAllBytes(Path.Combine(directory, file)), StringComparer.Ordinal);

    private static string Relative(string directory, string path) => Path.GetRelativePath(directory, path).Replace('\\', '/');

    private static string[] RewritePaths(string[] arguments, string runDirectory)
    {
        var rewritten = new List<string>(arguments.Length + 2);
        for (var index = 0; index < arguments.Length; index++)
        {
            rewritten.Add(arguments[index]);
            if (index + 1 < arguments.Length && IsPathOption(arguments[index]))
            {
                rewritten.Add(Path.Combine(runDirectory, arguments[++index]));
            }
        }

        // A reader running the command in the data set folder reads its portatlas.json by default.
        if (!arguments.Contains("--config", StringComparer.Ordinal))
        {
            rewritten.AddRange(["--config", Path.Combine(runDirectory, AppConfigurationFile.DefaultPath)]);
        }

        return [.. rewritten];
    }

    // Every extra matrix of export rules is named --<matrix>-output.
    private static bool IsPathOption(string argument) =>
        PathOptions.Contains(argument) || argument.EndsWith("-output", StringComparison.Ordinal);
}
