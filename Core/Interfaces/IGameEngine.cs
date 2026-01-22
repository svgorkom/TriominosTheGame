namespace Triominos.Core.Interfaces;

using Triominos.Core.Events;
using Triominos.Core.Models;

/// <summary>
/// Core game engine interface that defines all game operations.
/// This interface is UI-agnostic and can be implemented for any presentation layer.
/// </summary>
public interface IGameEngine
{
    #region Game State Properties

    /// <summary>
    /// Gets the current game phase
    /// </summary>
    GamePhase CurrentPhase { get; }

    /// <summary>
    /// Gets all players in the game
    /// </summary>
    IReadOnlyList<IPlayer> Players { get; }

    /// <summary>
    /// Gets the current player
    /// </summary>
    IPlayer? CurrentPlayer { get; }

    /// <summary>
    /// Gets the game board
    /// </summary>
    IGameBoard Board { get; }

    /// <summary>
    /// Gets the draw pile
    /// </summary>
    IDrawPile DrawPile { get; }

    /// <summary>
    /// Gets the currently selected piece (if any)
    /// </summary>
    IPiece? SelectedPiece { get; }

    /// <summary>
    /// Gets whether the selected piece is from the pool
    /// </summary>
    bool IsSelectingFromPool { get; }

    /// <summary>
    /// Gets whether this is the first move of the game
    /// </summary>
    bool IsFirstMove { get; }

    /// <summary>
    /// Gets the total score across all placements
    /// </summary>
    int TotalScore { get; }

    #endregion

    #region Game Flow Operations

    /// <summary>
    /// Starts a new game with the specified number of players
    /// </summary>
    GameResult StartGame(int numberOfPlayers);

    /// <summary>
    /// Resets the game to initial state
    /// </summary>
    void ResetGame();

    /// <summary>
    /// Ends the current player's turn
    /// </summary>
    GameResult EndTurn();

    #endregion

    #region Piece Operations

    /// <summary>
    /// Selects a piece from the current player's rack
    /// </summary>
    GameResult SelectPieceFromRack(IPiece piece);

    /// <summary>
    /// Selects a piece from the draw pile
    /// </summary>
    GameResult SelectPieceFromPool(IPiece piece);

    /// <summary>
    /// Deselects the currently selected piece
    /// </summary>
    void DeselectPiece();

    /// <summary>
    /// Rotates the currently selected piece
    /// </summary>
    GameResult RotatePiece();

    /// <summary>
    /// Attempts to place the selected piece at the specified position
    /// </summary>
    GameResult PlacePiece(int row, int col);

    /// <summary>
    /// Adds the selected pool piece to the current player's rack
    /// </summary>
    GameResult AddSelectedPieceToRack();

    #endregion

    #region Query Operations

    /// <summary>
    /// Gets all valid placement positions for the currently selected piece
    /// </summary>
    IEnumerable<BoardPosition> GetValidPlacements();

    /// <summary>
    /// Checks if a piece can be placed at the specified position
    /// </summary>
    bool CanPlaceAt(int row, int col);

    #endregion

    #region Events

    /// <summary>
    /// Raised when the game state changes
    /// </summary>
    event EventHandler<GameStateChangedEventArgs>? GameStateChanged;

    /// <summary>
    /// Raised when a piece is placed on the board
    /// </summary>
    event EventHandler<PiecePlacedEventArgs>? PiecePlaced;

    /// <summary>
    /// Raised when the current player changes
    /// </summary>
    event EventHandler<PlayerChangedEventArgs>? PlayerChanged;

    /// <summary>
    /// Raised when the game ends
    /// </summary>
    event EventHandler<GameEndedEventArgs>? GameEnded;

    /// <summary>
    /// Raised when a piece is selected or deselected
    /// </summary>
    event EventHandler<PieceSelectionChangedEventArgs>? PieceSelectionChanged;

    #endregion
}

/// <summary>
/// Represents the result of a game operation
/// </summary>
public record GameResult(bool Success, string Message, int PointsScored = 0);

/// <summary>
/// Represents a position on the game board
/// </summary>
public record BoardPosition(int Row, int Col);

/// <summary>
/// Game phase enumeration
/// </summary>
public enum GamePhase
{
    Setup,
    Playing,
    GameOver
}
