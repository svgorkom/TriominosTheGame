namespace Triominos.Core.Board;

/// <summary>
/// A point of the triangle lattice where cell corners meet. <c>Row</c> is the horizontal grid
/// line; <c>X</c> counts half cell widths. Six cells share each vertex.
/// </summary>
/// <param name="Row">Grid line index.</param>
/// <param name="X">Horizontal position in half cell widths.</param>
public readonly record struct Vertex(int Row, int X)
{
    /// <summary>Gets the six cells that share this vertex: three above it and three below.</summary>
    public IEnumerable<Cell> Cells()
    {
        yield return new Cell(Row - 1, X - 2);
        yield return new Cell(Row - 1, X - 1);
        yield return new Cell(Row - 1, X);
        yield return new Cell(Row, X - 2);
        yield return new Cell(Row, X - 1);
        yield return new Cell(Row, X);
    }
}
