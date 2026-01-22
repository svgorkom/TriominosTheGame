namespace Triominos.Core.Models;

using Triominos.Core.Interfaces;

/// <summary>
/// Represents the game board.
/// </summary>
public class GameBoard : IGameBoard
{
    private readonly Dictionary<(int row, int col), PlacedPiece> _board = new();
    private readonly List<IPlacedPiece> _placedPiecesList = [];

    public int Rows { get; }
    public int Cols { get; }
    public IReadOnlyList<IPlacedPiece> PlacedPieces => _placedPiecesList;
    public int PiecesPlaced => _board.Count;

    public GameBoard(int rows = 12, int cols = 24)
    {
        Rows = rows;
        Cols = cols;
    }

    public IPlacedPiece? GetPieceAt(int row, int col)
    {
        return _board.GetValueOrDefault((row, col));
    }

    public bool IsOccupied(int row, int col)
    {
        return _board.ContainsKey((row, col));
    }

    public bool IsValidPosition(int row, int col)
    {
        return row >= 0 && row < Rows && col >= 0 && col < Cols;
    }

    /// <summary>
    /// Places a piece on the board
    /// </summary>
    internal PlacedPiece PlacePiece(IPiece piece, int row, int col)
    {
        var placedPiece = new PlacedPiece(piece, row, col);
        _board[(row, col)] = placedPiece;
        _placedPiecesList.Add(placedPiece);
        return placedPiece;
    }

    /// <summary>
    /// Gets the internal placed piece for validation
    /// </summary>
    internal PlacedPiece? GetPlacedPieceAt(int row, int col)
    {
        return _board.GetValueOrDefault((row, col));
    }

    /// <summary>
    /// Gets all board positions that have pieces
    /// </summary>
    internal IEnumerable<(int row, int col)> GetOccupiedPositions()
    {
        return _board.Keys;
    }

    /// <summary>
    /// Clears the board
    /// </summary>
    internal void Clear()
    {
        _board.Clear();
        _placedPiecesList.Clear();
    }
}
