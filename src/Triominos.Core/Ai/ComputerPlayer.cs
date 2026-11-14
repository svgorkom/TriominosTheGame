using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Core.Tiles;

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
/// The computer opponent, at three levels (see <see cref="ComputerLevel"/>).
/// </summary>
/// <remarks>
/// <see cref="Decide(TriominosGame, ComputerLevel, Random)"/> uses only what the player in turn
/// may see: their own rack, the board and the pool. It never changes the game: the caller applies
/// the move with the normal commands, which check it again. Apart from the random choice of the
/// easy level it has no randomness, so the same state always gives the same move.
/// </remarks>
public static class ComputerPlayer
{
    /// <summary>How many of its own moves the strong level plans.</summary>
    private const int LookaheadDepth = 3;

    /// <summary>How many lines of play the strong level keeps after every step.</summary>
    private const int BeamWidth = 8;

    /// <summary>How many of the best-scoring lines are looked at closely (for stuck tiles) before the beam is cut.</summary>
    private const int PreselectCount = 24;

    /// <summary>
    /// How much less a later move counts: the opponent moves in between and may spoil the plan, so
    /// the second move counts half and the third a quarter.
    /// </summary>
    private const double FutureDiscount = 0.5;

    /// <summary>What a tile that fits nowhere is expected to cost: usually a draw.</summary>
    private const int StuckTilePenalty = Scoring.DrawPenalty;

    /// <summary>Chooses a move at the normal level; see <see cref="Decide(TriominosGame, ComputerLevel, Random)"/>.</summary>
    /// <param name="game">A game that is not finished.</param>
    public static ComputerMove Decide(TriominosGame game) => Decide(game, ComputerLevel.Normal, null);

    /// <summary>
    /// Chooses the move of the current player. A tile is laid whenever one fits. Without a valid
    /// move the computer draws while it may and then passes.
    /// </summary>
    /// <param name="game">A game that is not finished.</param>
    /// <param name="level">
    /// Easy: a random valid placement. Normal: the placement with the highest score; on a tie the
    /// heavier tile (it empties the rack of more points), then the lowest tile id, cell and rotation.
    /// Strong: the first move of the best plan of up to <c>3</c> moves with its own tiles, where a plan
    /// is worth its points (later moves count less) minus a penalty for tiles left that fit nowhere.
    /// The starting move is the same as for Normal.
    /// </param>
    /// <param name="random">Source of the random choice; required for <see cref="ComputerLevel.Easy"/>.</param>
    public static ComputerMove Decide(TriominosGame game, ComputerLevel level, Random? random)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Phase == GamePhase.Finished)
        {
            throw new InvalidOperationException("The round is over; there is no move to choose.");
        }

        PlaceMove? move = level switch
        {
            ComputerLevel.Easy => ChooseRandom(game, random ?? throw new ArgumentNullException(nameof(random), "The easy level needs a random source.")),
            ComputerLevel.Strong when game.Phase == GamePhase.Playing => ChooseByLookahead(game),
            ComputerLevel.Normal or ComputerLevel.Strong => ChooseGreedy(game),
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
        };
        if (move is not null)
        {
            return move;
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

    // --- The levels ---------------------------------------------------------------------------

    private static PlaceMove? ChooseRandom(TriominosGame game, Random random)
    {
        var moves = Moves(game).ToList();
        if (moves.Count == 0)
        {
            return null;
        }

        var (tile, placement) = moves[random.Next(moves.Count)];
        return new PlaceMove(tile.Id, placement);
    }

    private static PlaceMove? ChooseGreedy(TriominosGame game)
    {
        PlaceMove? best = null;
        var bestScore = 0;
        var bestTileValue = 0;

        // Moves come in a fixed order and only a strict improvement replaces the best, so the
        // lowest tile id, cell and rotation win a tie.
        foreach (var (tile, placement) in Moves(game))
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

        return best;
    }

    /// <summary>
    /// A beam search over the player's own moves. The opponent's tiles are unknown, so the board is
    /// assumed to change only by these moves; the discount makes later moves count less because of that.
    /// </summary>
    private static PlaceMove? ChooseByLookahead(TriominosGame game)
    {
        var beam = new List<Line> { new(game.Board.Clone(), [.. game.CurrentPlayer.Rack], 0, null) };
        for (var depth = 1; depth <= LookaheadDepth; depth++)
        {
            var children = new List<Line>();
            foreach (var line in beam)
            {
                var before = children.Count;
                children.AddRange(Expand(line, depth));
                if (children.Count == before)
                {
                    // Nothing more fits: this line of play ends here.
                    children.Add(line);
                }
            }

            beam =
            [
                .. children
                    .OrderByDescending(line => line.Gained)
                    .ThenBy(FirstMoveKey)
                    .Take(PreselectCount)
                    .Select(line => (Line: line, Value: line.Gained - (StuckTilePenalty * StuckTiles(line))))
                    .OrderByDescending(scored => scored.Value)
                    .ThenBy(scored => FirstMoveKey(scored.Line))
                    .Take(BeamWidth)
                    .Select(scored => scored.Line),
            ];
        }

        return beam[0].First;
    }

    // --- Helpers ------------------------------------------------------------------------------

    /// <summary>Every valid placement of the current player's tiles, in a fixed order.</summary>
    private static IEnumerable<(Tile Tile, Placement Placement)> Moves(TriominosGame game)
    {
        foreach (var tile in game.CurrentPlayer.Rack.OrderBy(t => t.Id))
        {
            foreach (var placement in InFixedOrder(game.ValidPlacements(tile.Id)))
            {
                yield return (tile, placement);
            }
        }
    }

    private static IEnumerable<Placement> InFixedOrder(IEnumerable<Placement> placements) =>
        placements.OrderBy(p => p.Cell.Row).ThenBy(p => p.Cell.Col).ThenBy(p => p.Rotation);

    /// <summary>The lines of play that follow from one more move of a tile of the rack.</summary>
    private static IEnumerable<Line> Expand(Line line, int depth)
    {
        var weight = Math.Pow(FutureDiscount, depth - 1);
        foreach (var tile in line.Rack.OrderBy(t => t.Id))
        {
            foreach (var placement in InFixedOrder(line.Board.ValidPlacements(tile)))
            {
                var points = TriominosGame.PlayScore(line.Board, tile, placement.Cell).Total;
                var rack = line.Rack.Where(t => t.Id != tile.Id).ToList();
                var wentOut = rack.Count == 0 ? Scoring.GoingOutBonus : 0;

                var board = line.Board.Clone();
                board.Place(tile, placement);

                yield return new Line(board, rack, line.Gained + (weight * (points + wentOut)), line.First ?? new PlaceMove(tile.Id, placement));
            }
        }
    }

    private static int StuckTiles(Line line) => line.Rack.Count(tile => line.Board.ValidPlacements(tile).Count == 0);

    /// <summary>Orders lines with the same value by their first move, so the choice is the same every time.</summary>
    private static (int TileId, int Row, int Col, int Rotation) FirstMoveKey(Line line) =>
        line.First is { } first ? (first.TileId, first.Placement.Cell.Row, first.Placement.Cell.Col, first.Placement.Rotation) : (-1, 0, 0, 0);

    /// <summary>A line of play: the board and rack after its moves, its discounted points and its first move.</summary>
    private sealed record Line(GameBoard Board, IReadOnlyList<Tile> Rack, double Gained, PlaceMove? First);
}
