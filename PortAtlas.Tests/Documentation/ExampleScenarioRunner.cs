using System.CommandLine.Parsing;
using PortAtlas.Cli;

namespace PortAtlas.Tests.Documentation;

/// <summary>
/// Runs an example scenario through <see cref="Program.Main"/>, reading the homelab data set and writing to a folder
/// of the caller.
/// </summary>
internal static class ExampleScenarioRunner
{
    // A scenario is written as a reader types it in docs/examples/homelab: its relative inputs are found there, and its
    // relative outputs go to the folder of the run, so that a test never writes into the repository by accident.
    private static readonly HashSet<string> InputOptions = new(StringComparer.Ordinal) { "-i", "--input", "--cache", "--config" };
    private static readonly HashSet<string> OutputOptions = new(StringComparer.Ordinal) { "-o", "--output", "--output-cache", "--html", "--drawio" };

    /// <summary>
    /// Runs the scenario.
    /// </summary>
    /// <param name="scenario">Scenario to run.</param>
    /// <param name="outputDirectory">Folder that receives the files written by the command.</param>
    /// <returns>The exit code and the text written to the console.</returns>
    public static async Task<(int ExitCode, string Console)> RunAsync(ExampleScenario scenario, string outputDirectory)
    {
        var arguments = RewritePaths(CommandLineParser.SplitCommandLine(scenario.Command).ToArray(), outputDirectory);
        var consoleOut = Console.Out;
        var consoleError = Console.Error;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        Console.SetError(captured);
        try
        {
            var exitCode = await Program.Main(arguments);
            return (exitCode, captured.ToString());
        }
        finally
        {
            Console.SetOut(consoleOut);
            Console.SetError(consoleError);
        }
    }

    private static string[] RewritePaths(string[] arguments, string outputDirectory)
    {
        var rewritten = new List<string>(arguments.Length + 2);
        for (var index = 0; index < arguments.Length; index++)
        {
            rewritten.Add(arguments[index]);
            if (index + 1 >= arguments.Length)
            {
                continue;
            }

            if (InputOptions.Contains(arguments[index]))
            {
                rewritten.Add(Path.Combine(DocumentationPageProvider.HomelabExampleDirectory, arguments[++index]));
            }
            else if (IsOutputOption(arguments[index]))
            {
                rewritten.Add(Path.Combine(outputDirectory, arguments[++index]));
            }
        }

        // The data set ships its portatlas.json, which a reader running the command in that folder reads by default.
        if (!arguments.Contains("--config", StringComparer.Ordinal))
        {
            rewritten.AddRange(["--config", Path.Combine(DocumentationPageProvider.HomelabExampleDirectory, "portatlas.json")]);
        }

        return [.. rewritten];
    }

    // Every extra matrix of export rules is named --<matrix>-output.
    private static bool IsOutputOption(string argument) =>
        OutputOptions.Contains(argument) || argument.EndsWith("-output", StringComparison.Ordinal);
}
