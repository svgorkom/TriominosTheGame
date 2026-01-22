namespace Triominos.Core.Interfaces;

/// <summary>
/// Represents a triomino piece in the game.
/// This interface is UI-agnostic.
/// </summary>
public interface IPiece
{
    /// <summary>
    /// Unique identifier for this piece
    /// </summary>
    int Id { get; }

    /// <summary>
    /// Value at the first corner
    /// </summary>
    int Value1 { get; }

    /// <summary>
    /// Value at the second corner
    /// </summary>
    int Value2 { get; }

    /// <summary>
    /// Value at the third corner
    /// </summary>
    int Value3 { get; }

    /// <summary>
    /// Whether the piece is pointing up (true) or down (false)
    /// </summary>
    bool IsPointingUp { get; }

    /// <summary>
    /// Whether this is a triple piece (all values equal)
    /// </summary>
    bool IsTriple { get; }

    /// <summary>
    /// The point value of this piece (sum of corners)
    /// </summary>
    int PointValue { get; }

    /// <summary>
    /// Creates a deep copy of this piece
    /// </summary>
    IPiece Clone();

    /// <summary>
    /// Rotates the piece clockwise
    /// </summary>
    void Rotate();
}

/// <summary>
/// Represents a piece that has been placed on the board
/// </summary>
public interface IPlacedPiece
{
    /// <summary>
    /// The piece that was placed
    /// </summary>
    IPiece Piece { get; }

    /// <summary>
    /// Row position on the board
    /// </summary>
    int Row { get; }

    /// <summary>
    /// Column position on the board
    /// </summary>
    int Col { get; }

    /// <summary>
    /// Whether the piece is pointing up
    /// </summary>
    bool IsPointingUp { get; }
}
