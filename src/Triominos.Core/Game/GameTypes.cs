using Triominos.Core.Board;
using Triominos.Core.Tiles;

namespace Triominos.Core.Game;

/// <summary>Phase of a game.</summary>
public enum GamePhase
{
    /// <summary>Tiles are dealt; the starting player must lay one of the allowed starting tiles.</summary>
    Starting,

    /// <summary>Normal turns.</summary>
    Playing,

    /// <summary>The round is over; see <see cref="TriominosGame.Outcome"/>.</summary>
    Finished,
}

/// <summary>Why a move was refused. <see cref="None"/> means it succeeded.</summary>
public enum MoveError
{
    /// <summary>No error.</summary>
    None,

    /// <summary>The move is not allowed in the current phase.</summary>
    WrongPhase,

    /// <summary>The current player does not hold the tile.</summary>
    TileNotInRack,

    /// <summary>The tile is not one of the allowed starting tiles.</summary>
    NotAStartTile,

    /// <summary>The starting tile must be laid on <see cref="Cell.Origin"/>.</summary>
    StartNotAtOrigin,

    /// <summary>The rotation is not 0, 1 or 2.</summary>
    InvalidRotation,

    /// <summary>A tile already lies on the cell.</summary>
    CellOccupied,

    /// <summary>The cell does not share an edge with a placed tile.</summary>
    NotAdjacent,

    /// <summary>A corner value differs from the value already at that vertex.</summary>
    CornerMismatch,

    /// <summary>The player already drew the maximum number of tiles this turn.</summary>
    DrawLimitReached,

    /// <summary>The pool is empty.</summary>
    PoolEmpty,

    /// <summary>Passing is only allowed after three draws or with an empty pool.</summary>
    PassNotAllowed,

    /// <summary>The player already took the maximum number of hints this game.</summary>
    HintLimitReached,
}

/// <summary>How a round ended.</summary>
public enum GameEndReason
{
    /// <summary>A player played their last tile.</summary>
    WentOut,

    /// <summary>The pool is empty and every player passed in a row: a draw.</summary>
    Blocked,
}

/// <summary>Result of a finished round.</summary>
/// <param name="Reason">How the round ended.</param>
/// <param name="Winner">Index of the winning player, or null for a draw.</param>
/// <param name="GoingOutBonus">Bonus the winner received for going out (0 for a draw).</param>
public sealed record GameOutcome(GameEndReason Reason, int? Winner, int GoingOutBonus);

/// <summary>Something that happened during a move; the UI uses these to animate.</summary>
public abstract record GameEvent;

/// <summary>A tile was laid.</summary>
public sealed record TilePlaced(int Player, PlacedTile Tile, ScoreBreakdown Score) : GameEvent;

/// <summary>A tile was drawn from the pool.</summary>
public sealed record TileDrawn(int Player, Tile Tile, int Penalty) : GameEvent;

/// <summary>A player passed.</summary>
public sealed record TurnPassed(int Player, int Penalty) : GameEvent;

/// <summary>A player paid for a hint; the turn goes on.</summary>
public sealed record HintTaken(int Player, int Penalty) : GameEvent;

/// <summary>A new turn started.</summary>
public sealed record TurnStarted(int Player) : GameEvent;

/// <summary>The round ended.</summary>
public sealed record GameFinished(GameOutcome Outcome) : GameEvent;

/// <summary>Result of a move: an error, or the events it caused.</summary>
/// <param name="Error">Why the move failed, or <see cref="MoveError.None"/>.</param>
/// <param name="Events">Events in the order they happened; empty when the move failed.</param>
public sealed record MoveResult(MoveError Error, IReadOnlyList<GameEvent> Events)
{
    /// <summary>Gets whether the move succeeded.</summary>
    public bool Succeeded => Error == MoveError.None;

    internal static MoveResult Fail(MoveError error) => new(error, []);
}
