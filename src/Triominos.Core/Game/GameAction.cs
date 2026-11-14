using Triominos.Core.Board;

namespace Triominos.Core.Game;

/// <summary>
/// A command a player gives to the game, as a value that can be sent and kept. Because the game is
/// deterministic, the same actions applied to the same deal always give the same game; online
/// players stay in step by sending each other their actions and nothing else.
/// </summary>
public abstract record GameAction
{
    private protected GameAction()
    {
    }

    /// <summary>Gives the command to the game, which refuses it when the rules do.</summary>
    /// <param name="game">The game to give it to.</param>
    public abstract MoveResult ApplyTo(TriominosGame game);
}

/// <summary>Lays a tile of the current player; see <see cref="TriominosGame.Place"/>.</summary>
/// <param name="TileId">Id of the tile in the rack.</param>
/// <param name="Placement">Where and how it is laid.</param>
public sealed record PlaceAction(int TileId, Placement Placement) : GameAction
{
    /// <inheritdoc />
    public override MoveResult ApplyTo(TriominosGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Place(TileId, Placement);
    }
}

/// <summary>Draws a tile; see <see cref="TriominosGame.Draw"/>.</summary>
public sealed record DrawAction : GameAction
{
    /// <inheritdoc />
    public override MoveResult ApplyTo(TriominosGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Draw();
    }
}

/// <summary>Passes the turn; see <see cref="TriominosGame.Pass"/>.</summary>
public sealed record PassAction : GameAction
{
    /// <inheritdoc />
    public override MoveResult ApplyTo(TriominosGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Pass();
    }
}

/// <summary>Takes a hint, which costs points; see <see cref="TriominosGame.Hint"/>. The advice itself is not part of the action.</summary>
public sealed record HintAction : GameAction
{
    /// <inheritdoc />
    public override MoveResult ApplyTo(TriominosGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Hint();
    }
}
