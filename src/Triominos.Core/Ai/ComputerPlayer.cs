using Triominos.Core.Board;
using Triominos.Core.Game;

namespace Triominos.Core.Ai;

/// <summary>A move chosen by the computer; apply it with the matching command of <see cref="TriominosGame"/>.</summary>
public abstract record ComputerMove;

/// <summary>Lay a tile; apply with <see cref="TriominosGame.Place"/>.</summary>
/// <param name="TileId">Id of a tile in the rack.</param>
/// <param name="Placement">Where and how to lay it.</param>
public sealed record PlaceMove(int TileId, Placement Placement) : ComputerMove;

/// <summary>Draw a tile; apply with <see cref="TriominosGame.Draw"/>.</summary>
public sealed record DrawMove : ComputerMove;

/// <summary>Pass; apply with <see cref="TriominosGame.Pass"/>.</summary>
public sealed record PassMove : ComputerMove;

/// <summary>
/// The computer opponent. It plays the move that scores most right now and does not look ahead.
/// </summary>
/// <remarks>
/// <see cref="Decide"/> uses only what the player in turn may see: their own rack, the board and
/// the pool. It has no randomness, so the same state always gives the same move, and it never
/// changes the game: the caller applies the move with the normal commands, which check it again.
/// </remarks>
public static class ComputerPlayer
{
    /// <summary>
    /// Chooses the move of the current player. A tile is laid whenever one fits: the placement with
    /// the highest score, and on a tie the heavier tile (it empties the rack of more points), then
    /// the lowest tile id, cell and rotation. Without a valid move the computer draws while it may
    /// and then passes.
    /// </summary>
    /// <param name="game">A game that is not finished.</param>
    public static ComputerMove Decide(TriominosGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Phase == GamePhase.Finished)
        {
            throw new InvalidOperationException("The round is over; there is no move to choose.");
        }

        PlaceMove? best = null;
        var bestScore = 0;
        var bestTileValue = 0;

        // Walking the candidates in a fixed order and replacing only on a strict improvement makes
        // the lowest tile id, cell and rotation win a tie.
        foreach (var tile in game.CurrentPlayer.Rack.OrderBy(t => t.Id))
        {
            var placements = game.ValidPlacements(tile.Id)
                .OrderBy(p => p.Cell.Row)
                .ThenBy(p => p.Cell.Col)
                .ThenBy(p => p.Rotation);

            foreach (var placement in placements)
            {
                if (game.ScoreOf(tile.Id, placement) is not { } breakdown)
                {
                    continue;
                }

                var better = best is null
                    || breakdown.Total > bestScore
                    || (breakdown.Total == bestScore && tile.Value > bestTileValue);
                if (better)
                {
                    best = new PlaceMove(tile.Id, placement);
                    bestScore = breakdown.Total;
                    bestTileValue = tile.Value;
                }
            }
        }

        if (best is not null)
        {
            return best;
        }

        if (game.CanDraw)
        {
            return new DrawMove();
        }

        if (game.CanPass)
        {
            return new PassMove();
        }

        // Without a valid move, a player may always draw or pass; reaching this means the rules changed.
        throw new InvalidOperationException("The player has no valid move, cannot draw and cannot pass.");
    }
}
