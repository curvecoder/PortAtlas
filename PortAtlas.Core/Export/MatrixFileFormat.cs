namespace PortAtlas.Core.Export;

/// <summary>
/// File format of the matrices written by <c>export rules</c>.
/// </summary>
public enum MatrixFileFormat
{
    /// <summary>
    /// Delimited values, one header line then one line per row, for a spreadsheet or a script.
    /// </summary>
    Csv,

    /// <summary>
    /// Markdown document: an explanation of the matrix and of its uses, a column legend, then the rows as a table.
    /// </summary>
    Markdown
}
