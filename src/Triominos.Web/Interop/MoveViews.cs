using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Core.Tiles;
using Triominos.Web.Play;

namespace Triominos.Web.Interop;

/// <summary>One line of a score: what it is for, the points, and whether it earns the gold effect.</summary>
/// <param name="Label">Dutch text, such as "Brugbonus".</param>
/// <param name="Points">The points of this line.</param>
/// <param name="Gold">True for a bridge or hexagon bonus.</param>
public sealed record ScoreLineView(string Label, int Points, bool Gold);

/// <summary>A cell of the board.</summary>
/// <param name="Row">Cell row.</param>
/// <param name="Col">Cell column.</param>
public sealed record CellView(int Row, int Col);

/// <summary>A tile was laid; the scene lets it fall from where the player dropped it.</summary>
/// <param name="Player">Index of the player.</param>
/// <param name="TileId">Tile id.</param>
/// <param name="Row">Cell row.</param>
/// <param name="Col">Cell column.</param>
/// <param name="Values">Corner values as laid, clockwise.</param>
/// <param name="Lines">The score, broken down.</param>
/// <param name="Total">The total score of the move.</param>
/// <param name="Hexagon">The cells of every hexagon the tile completed, each cell once; empty without a hexagon bonus.</param>
public sealed record PlacedEventView(int Player, int TileId, int Row, int Col, int[] Values, ScoreLineView[] Lines, int Total, CellView[] Hexagon)
{
    /// <summary>Gets the kind the scene switches on.</summary>
    public string Kind => "placed";
}

/// <summary>A tile was drawn into the rack of the current player.</summary>
/// <param name="Player">Index of the player.</param>
/// <param name="TileId">Id of the drawn tile; its values are in the rack that comes with the move.</param>
/// <param name="Penalty">Points lost.</param>
public sealed record DrawnEventView(int Player, int TileId, int Penalty)
{
    /// <summary>Gets the kind the scene switches on.</summary>
    public string Kind => "drawn";
}

/// <summary>A player passed.</summary>
/// <param name="Player">Index of the player.</param>
/// <param name="Penalty">Points lost.</param>
public sealed record PassedEventView(int Player, int Penalty)
{
    /// <summary>Gets the kind the scene switches on.</summary>
    public string Kind => "passed";
}

/// <summary>What the tiles left in one rack are worth to the player who went out.</summary>
/// <param name="Name">Name of the player holding them.</param>
/// <param name="Tiles">The tiles, shown face up.</param>
/// <param name="Line">The points they give.</param>
public sealed record SettlementStepView(string Name, RackTileView[] Tiles, ScoreLineView Line);

/// <summary>The round ended. A draw has no winner and nothing to settle.</summary>
/// <param name="Winner">Index of the player who went out, or null.</param>
/// <param name="GoingOut">The bonus for going out, including the other racks.</param>
/// <param name="Rivals">One step per other rack that still holds tiles.</param>
public sealed record FinishedEventView(int? Winner, ScoreLineView? GoingOut, SettlementStepView[] Rivals)
{
    /// <summary>Gets the kind the scene switches on.</summary>
    public string Kind => "finished";
}

/// <summary>Everything the scene needs to show one move.</summary>
/// <param name="Events">What happened, in order: the event views above.</param>
/// <param name="Board">The board after the move.</param>
/// <param name="Rack">The rack to show afterwards; empty when the turn passes on or the round ended.</param>
public sealed record MoveView(object[] Events, TileView[] Board, RackTileView[] Rack);

/// <summary>The deal of a new round.</summary>
/// <param name="Counts">Tiles per player, in turn order.</param>
public sealed record DealView(int[] Counts);

/// <summary>Turns the result of a move in Core into what the scene shows. It only maps; no rules.</summary>
public static class MoveViews
{
    /// <summary>Builds the view of a successful move.</summary>
    /// <param name="game">The game after the move.</param>
    /// <param name="result">The result of the move.</param>
    /// <param name="rack">The rack to show afterwards.</param>
    /// <param name="marked">Tiles of that rack to single out.</param>
    public static MoveView From(TriominosGame game, MoveResult result, IReadOnlyList<Tile> rack, IReadOnlyCollection<Tile> marked)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentNullException.ThrowIfNull(marked);

        var events = new List<object>();
        foreach (var gameEvent in result.Events)
        {
            switch (gameEvent)
            {
                case TilePlaced placed:
                    events.Add(Placed(placed, game.Board));
                    break;
                case TileDrawn drawn:
                    events.Add(new DrawnEventView(drawn.Player, drawn.Tile.Id, drawn.Penalty));
                    break;
                case TurnPassed passed:
                    events.Add(new PassedEventView(passed.Player, passed.Penalty));
                    break;
                case GameFinished finished:
                    events.Add(Finished(game, finished.Outcome));
                    break;
            }
        }

        return new MoveView(
            [.. events],
            [.. game.Board.Tiles.Select(TileView.From)],
            [.. rack.Select(t => RackTileView.Of(t, marked.Contains(t)))]);
    }

    private static PlacedEventView Placed(TilePlaced placed, GameBoard board)
    {
        var cell = placed.Tile.Placement.Cell;
        var score = placed.Score;
        var lines = new List<ScoreLineView> { new("Steen", score.TileValue, false) };
        if (score.StartBonus > 0)
        {
            lines.Add(new ScoreLineView("Startbonus", score.StartBonus, false));
        }

        if (score.BridgeBonus > 0)
        {
            lines.Add(new ScoreLineView("Brugbonus", score.BridgeBonus, true));
        }

        if (score.HexagonBonus > 0)
        {
            var text = PlaySession.HexagonText(score.HexagonBonus);
            lines.Add(new ScoreLineView(char.ToUpperInvariant(text[0]) + text[1..], score.HexagonBonus, true));
        }

        return new PlacedEventView(
            placed.Player,
            placed.Tile.Tile.Id,
            cell.Row,
            cell.Col,
            [placed.Tile.ValueAtCorner(0), placed.Tile.ValueAtCorner(1), placed.Tile.ValueAtCorner(2)],
            [.. lines],
            score.Total,
            HexagonCells(board, cell));
    }

    /// <summary>The cells around every hexagon centre the tile completed, each cell once.</summary>
    private static CellView[] HexagonCells(GameBoard board, Cell cell)
    {
        return [.. board.HexagonCenters(cell)
            .SelectMany(center => center.Cells())
            .Distinct()
            .Select(c => new CellView(c.Row, c.Col))];
    }

    private static FinishedEventView Finished(TriominosGame game, GameOutcome outcome)
    {
        if (outcome.Winner is not { } winner)
        {
            return new FinishedEventView(null, null, []);
        }

        var rivals = new List<SettlementStepView>();
        for (var i = 0; i < game.Players.Count; i++)
        {
            var player = game.Players[i];
            if (i != winner && player.Rack.Count > 0)
            {
                rivals.Add(new SettlementStepView(
                    player.Name,
                    [.. player.Rack.Select(t => RackTileView.Of(t, false))],
                    new ScoreLineView($"Stenen van {player.Name}", player.RackValue, true)));
            }
        }

        return new FinishedEventView(winner, new ScoreLineView("Uitgaan", outcome.GoingOutBonus, true), [.. rivals]);
    }
}
