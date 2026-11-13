namespace PortAtlas.Core.Export;

/// <summary>
/// A file designated by the inputs of a command.
/// </summary>
/// <param name="FullPath">Full path of the file.</param>
/// <param name="IsNamed">True when an input is the path of this very file, false when it only comes from a directory or a wildcard pattern.</param>
internal sealed record ResolvedInputFile(string FullPath, bool IsNamed);
