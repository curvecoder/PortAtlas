namespace PortAtlas.Core.Export;

/// <summary>
/// A matrix file written by <c>export rules</c>.
/// </summary>
/// <param name="Path">Full path of the file.</param>
/// <param name="RowCount">Number of rows written below the header.</param>
public sealed record ExportedMatrixFile(string Path, int RowCount);
