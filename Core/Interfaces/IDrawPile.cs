namespace Triominos.Core.Interfaces;

/// <summary>
/// Represents the draw pile (pool) of pieces.
/// This interface is UI-agnostic.
/// </summary>
public interface IDrawPile
{
    /// <summary>
    /// Gets all pieces currently in the draw pile
    /// </summary>
    IReadOnlyList<IPiece> Pieces { get; }

    /// <summary>
    /// Gets the number of pieces remaining in the draw pile
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets whether the draw pile has any pieces
    /// </summary>
    bool HasPieces { get; }
}
