namespace Triominos.Models;

/// <summary>
/// Represents a triomino piece placed on the game board at a specific grid position
/// </summary>
public class PlacedTriomino(TriominoPiece piece, int row, int column)
{
    public TriominoPiece Piece { get; } = piece;
    public int GridRow { get; } = row;
    public int GridColumn { get; } = column;

    /// <summary>
    /// Gets the edge values that face an adjacent cell
    /// </summary>
    public (int val1, int val2) GetEdgeFacing(int adjacentRow, int adjacentCol)
    {
        int rowDiff = adjacentRow - GridRow;
        int colDiff = adjacentCol - GridColumn;

        return (colDiff, rowDiff, Piece.IsPointingUp) switch
        {
            (-1, 0, _) => Piece.GetEdge(TriominoEdge.Left),
            (1, 0, _) => Piece.GetEdge(TriominoEdge.Right),
            (0, 1, true) => Piece.GetEdge(TriominoEdge.Bottom),
            (0, -1, false) => Piece.GetEdge(TriominoEdge.Top),
            _ => throw new ArgumentException($"Position ({adjacentRow}, {adjacentCol}) is not adjacent to ({GridRow}, {GridColumn})")
        };
    }

    /// <summary>
    /// Gets the positions of all adjacent cells for this triangle
    /// </summary>
    public IEnumerable<(int row, int col)> GetAdjacentPositions()
    {
        yield return (GridRow, GridColumn - 1); // Left
        yield return (GridRow, GridColumn + 1); // Right
        
        // Vertical neighbor depends on orientation
        yield return Piece.IsPointingUp
            ? (GridRow + 1, GridColumn)  // Bottom
            : (GridRow - 1, GridColumn); // Top
    }
}
