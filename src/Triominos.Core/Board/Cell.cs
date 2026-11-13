namespace Triominos.Core.Board;

/// <summary>
/// A triangular cell on the unbounded board. A cell points up when <c>Row + Col</c> is even.
/// Neighbouring cells in a row alternate between pointing up and down.
/// </summary>
/// <remarks>
/// Corners are numbered clockwise (screen coordinates, y pointing down):
/// pointing up: 0 = apex, 1 = bottom right, 2 = bottom left;
/// pointing down: 0 = bottom apex, 1 = top left, 2 = top right.
/// Edge <c>i</c> runs from corner <c>i</c> to corner <c>(i + 1) % 3</c>, so the corner
/// opposite edge <c>i</c> is <c>(i + 2) % 3</c>.
/// </remarks>
/// <param name="Row">Row index; may be negative.</param>
/// <param name="Col">Column index; may be negative.</param>
public readonly record struct Cell(int Row, int Col)
{
    /// <summary>Gets the cell where the starting tile is placed.</summary>
    public static Cell Origin => new(0, 0);

    /// <summary>Gets whether the triangle points up.</summary>
    public bool IsUp => (Row + Col) % 2 == 0;

    /// <summary>Gets the board vertex at a corner of this cell.</summary>
    /// <param name="corner">Corner index, 0 to 2, clockwise.</param>
    public Vertex Corner(int corner)
    {
        return (IsUp, corner) switch
        {
            (true, 0) => new Vertex(Row, Col + 1),
            (true, 1) => new Vertex(Row + 1, Col + 2),
            (true, 2) => new Vertex(Row + 1, Col),
            (false, 0) => new Vertex(Row + 1, Col + 1),
            (false, 1) => new Vertex(Row, Col),
            (false, 2) => new Vertex(Row, Col + 2),
            _ => throw new ArgumentOutOfRangeException(nameof(corner), corner, "Corner must be 0, 1 or 2."),
        };
    }

    /// <summary>Gets the cell on the other side of an edge.</summary>
    /// <param name="edge">Edge index, 0 to 2; edge <c>i</c> joins corners <c>i</c> and <c>i + 1</c>.</param>
    public Cell Neighbor(int edge)
    {
        return (IsUp, edge) switch
        {
            (true, 0) => new Cell(Row, Col + 1),
            (true, 1) => new Cell(Row + 1, Col),
            (true, 2) => new Cell(Row, Col - 1),
            (false, 0) => new Cell(Row, Col - 1),
            (false, 1) => new Cell(Row - 1, Col),
            (false, 2) => new Cell(Row, Col + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Edge must be 0, 1 or 2."),
        };
    }
}
