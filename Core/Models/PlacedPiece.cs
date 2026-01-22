namespace Triominos.Core.Models;

using Triominos.Core.Interfaces;

/// <summary>
/// Represents a piece that has been placed on the game board.
/// </summary>
public class PlacedPiece : IPlacedPiece
{
    public IPiece Piece { get; }
    public int Row { get; }
    public int Col { get; }
    public bool IsPointingUp => (Row + Col) % 2 == 0;

    public PlacedPiece(IPiece piece, int row, int col)
    {
        Piece = piece.Clone();
        Row = row;
        Col = col;
        
        // Ensure the piece orientation matches its position
        if (Piece is Piece mutablePiece)
        {
            mutablePiece.IsPointingUp = IsPointingUp;
        }
    }

    /// <summary>
    /// Gets the adjacent positions for this placed piece
    /// </summary>
    public IEnumerable<(int row, int col)> GetAdjacentPositions()
    {
        if (IsPointingUp)
        {
            // Pointing up triangle: adjacent on left, right, and bottom
            yield return (Row, Col - 1);     // Left
            yield return (Row, Col + 1);     // Right
            yield return (Row + 1, Col);     // Bottom
        }
        else
        {
            // Pointing down triangle: adjacent on left, right, and top
            yield return (Row, Col - 1);     // Left
            yield return (Row, Col + 1);     // Right
            yield return (Row - 1, Col);     // Top
        }
    }

    /// <summary>
    /// Gets the edge that faces the specified adjacent position
    /// </summary>
    public (int val1, int val2) GetEdgeFacing(int adjRow, int adjCol)
    {
        if (Piece is not Piece concretePiece)
            throw new InvalidOperationException("Piece must be a Piece type");

        // Determine which edge faces the adjacent position
        if (adjCol < Col)
            return concretePiece.GetEdge(PieceEdge.Left);
        if (adjCol > Col)
            return concretePiece.GetEdge(PieceEdge.Right);
        if (adjRow > Row && IsPointingUp)
            return concretePiece.GetEdge(PieceEdge.Bottom);
        if (adjRow < Row && !IsPointingUp)
            return concretePiece.GetEdge(PieceEdge.Top);

        throw new InvalidOperationException("Invalid adjacent position");
    }
}
