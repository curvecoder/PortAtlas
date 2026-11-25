namespace PortAtlas.Core.Export;

/// <summary>
/// Legend of one column of a matrix.
/// </summary>
/// <param name="Name">Column name, as in the header of the matrix.</param>
/// <param name="Content">What the column holds, in Markdown.</param>
public sealed record MatrixColumnDescription(string Name, string Content);
