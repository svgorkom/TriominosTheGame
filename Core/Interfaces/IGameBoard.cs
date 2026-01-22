namespace Triominos.Core.Interfaces;

/// <summary>
/// Represents the game board.
/// This interface is UI-agnostic.
/// </summary>
public interface IGameBoard
{
    /// <summary>
    /// Number of rows on the board
    /// </summary>
    int Rows { get; }

    /// <summary>
    /// Number of columns on the board
    /// </summary>
    int Cols { get; }

    /// <summary>
    /// Gets all placed pieces on the board
    /// </summary>
    IReadOnlyList<IPlacedPiece> PlacedPieces { get; }

    /// <summary>
    /// Gets the number of pieces placed on the board
    /// </summary>
    int PiecesPlaced { get; }

    /// <summary>
    /// Gets the piece at the specified position, or null if empty
    /// </summary>
    IPlacedPiece? GetPieceAt(int row, int col);

    /// <summary>
    /// Checks if a position is occupied
    /// </summary>
    bool IsOccupied(int row, int col);

    /// <summary>
    /// Checks if a position is within board bounds
    /// </summary>
    bool IsValidPosition(int row, int col);
}
