using System.Text.Json;

namespace PortAtlas.Tests.Documentation;

/// <summary>
/// A command of the documentation run on the homelab example data set.
/// </summary>
/// <param name="Name">Folder of <c>docs/examples/homelab/output</c> that holds the files the command writes.</param>
/// <param name="Command">Arguments typed after <c>PortAtlas</c>, with paths relative to <c>docs/examples/homelab</c>.</param>
internal sealed record ExampleScenario(string Name, string Command);

/// <summary>
/// Reads the scenarios listed in <c>docs/examples/homelab/scenarios.json</c>.
/// </summary>
internal static class ExampleScenarioCatalog
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Folder that holds one subfolder of committed output files per scenario.
    /// </summary>
    public static string OutputDirectory { get; } = Path.Combine(DocumentationPageProvider.HomelabExampleDirectory, "output");

    /// <summary>
    /// Reads every scenario of the manifest.
    /// </summary>
    /// <returns>The scenarios, in the order of the manifest.</returns>
    /// <exception cref="InvalidDataException">When the manifest holds no scenario list.</exception>
    public static IReadOnlyList<ExampleScenario> Load()
    {
        var path = Path.Combine(DocumentationPageProvider.HomelabExampleDirectory, "scenarios.json");
        return JsonSerializer.Deserialize<List<ExampleScenario>>(File.ReadAllText(path), SerializerOptions)
            ?? throw new InvalidDataException($"{path} holds no scenario list.");
    }

    /// <summary>
    /// Finds a scenario by name.
    /// </summary>
    /// <param name="name">Name of the scenario.</param>
    /// <returns>The scenario, or null when the manifest does not list it.</returns>
    public static ExampleScenario? Find(string name) =>
        Load().FirstOrDefault(scenario => string.Equals(scenario.Name, name, StringComparison.Ordinal));
}
