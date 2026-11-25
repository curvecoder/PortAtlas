namespace PortAtlas.Core.Export;

/// <summary>
/// Explanation written at the top of a Markdown matrix: what the matrix holds and what it can be used for.
/// </summary>
/// <param name="Title">Title of the document.</param>
/// <param name="Summary">What one row of the matrix stands for, in Markdown.</param>
/// <param name="Uses">What the matrix can be used for, one Markdown item each.</param>
/// <param name="RowsHeading">Heading of the table of rows (for example <c>Rules</c>).</param>
/// <param name="Columns">Legend of each column, in the order of the matrix.</param>
public sealed record MatrixDescription(
    string Title,
    string Summary,
    IReadOnlyList<string> Uses,
    string RowsHeading,
    IReadOnlyList<MatrixColumnDescription> Columns);
