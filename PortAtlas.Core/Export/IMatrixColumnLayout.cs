namespace PortAtlas.Core.Export;

/// <summary>
/// Columns of a matrix and the values of one row, before any file format is applied.
/// </summary>
/// <typeparam name="T">Row item of the matrix.</typeparam>
public interface IMatrixColumnLayout<in T>
{
    /// <summary>
    /// Gets the column names, in the order of the values returned by <see cref="FieldsOf"/>.
    /// </summary>
    IReadOnlyList<string> ColumnNames { get; }

    /// <summary>
    /// Returns the values of one row, unescaped, in the order of <see cref="ColumnNames"/>.
    /// </summary>
    /// <param name="item">Row item.</param>
    /// <returns>One value per column.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="item"/> is null.</exception>
    IReadOnlyList<string> FieldsOf(T item);
}
