namespace Triominos.Core.Interfaces;

/// <summary>
/// Represents a player in the game.
/// This interface is UI-agnostic.
/// </summary>
public interface IPlayer
{
    /// <summary>
    /// Unique identifier for this player
    /// </summary>
    int Id { get; }

    /// <summary>
    /// Player's display name
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Current score
    /// </summary>
    int Score { get; }

    /// <summary>
    /// Pieces in the player's rack
    /// </summary>
    IReadOnlyList<IPiece> Rack { get; }

    /// <summary>
    /// Number of pieces remaining in the rack
    /// </summary>
    int PiecesRemaining { get; }

    /// <summary>
    /// Whether this player is currently taking their turn
    /// </summary>
    bool IsCurrentPlayer { get; }
}
