using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Core.Tiles;

namespace Triominos.Web.Play;

/// <summary>Which screen the table shows.</summary>
public enum SessionScreen
{
    /// <summary>The next player has to take over; their rack is hidden until they are ready.</summary>
    Handover,

    /// <summary>The current player plays; their rack is shown.</summary>
    Playing,

    /// <summary>The round is over.</summary>
    Finished,
}

/// <summary>
/// One hot-seat game as the UI sees it: the Core game plus what only the UI cares about, which is
/// the handover between turns that hides the rack and the Dutch message about the last move. All
/// rules stay in <see cref="TriominosGame"/>.
/// </summary>
public sealed class PlaySession
{
    private static readonly string[] PlayerNames = ["Speler 1", "Speler 2"];

    /// <summary>Starts a game for two players on one screen.</summary>
    /// <param name="random">Source of the shuffle.</param>
    public PlaySession(Random random)
    {
        Game = TriominosGame.Start(PlayerNames, random);
        Screen = SessionScreen.Handover;
        Message = Game.Phase == GamePhase.Starting
            ? $"{Game.CurrentPlayer.Name} begint met een van de gemarkeerde stenen."
            : $"{Game.CurrentPlayer.Name} is aan de beurt.";
    }

    /// <summary>Gets the game.</summary>
    public TriominosGame Game { get; }

    /// <summary>Gets the screen to show.</summary>
    public SessionScreen Screen { get; private set; }

    /// <summary>Gets the message about the last move or error.</summary>
    public string Message { get; private set; }

    /// <summary>Gets the rack to show: the current player's while they play, otherwise none.</summary>
    public IReadOnlyList<Tile> VisibleRack => Screen == SessionScreen.Playing ? Game.CurrentPlayer.Rack : [];

    /// <summary>Gets the tiles to single out in the rack: the allowed starting tiles.</summary>
    public IReadOnlyList<Tile> MarkedTiles => Screen == SessionScreen.Playing ? Game.AllowedStartTiles : [];

    /// <summary>Gets whether the current player may draw now.</summary>
    public bool CanDraw => Screen == SessionScreen.Playing && Game.CanDraw;

    /// <summary>Gets whether the current player may pass now.</summary>
    public bool CanPass => Screen == SessionScreen.Playing && Game.CanPass;

    /// <summary>Gets the player who went out, or null when the round is not over or ended in a draw.</summary>
    public Player? Winner => Game.Outcome?.Winner is { } index ? Game.Players[index] : null;

    /// <summary>
    /// Returns where a tile of the current player fits. With no placement, the message says why
    /// the tile cannot be picked up.
    /// </summary>
    public IReadOnlyList<Placement> Pick(int tileId)
    {
        if (Screen != SessionScreen.Playing)
        {
            return [];
        }

        var placements = Game.ValidPlacements(tileId);
        if (placements.Count == 0)
        {
            Message = Game.Phase == GamePhase.Starting
                ? "Begin met een van de gemarkeerde stenen."
                : "Die steen past nergens.";
        }

        return placements;
    }

    /// <summary>Lays a tile; see <see cref="TriominosGame.Place"/>.</summary>
    public MoveResult Place(int tileId, Placement placement) =>
        Screen == SessionScreen.Playing ? Apply(Game.Place(tileId, placement)) : NotNow();

    /// <summary>Draws a tile; see <see cref="TriominosGame.Draw"/>.</summary>
    public MoveResult Draw() => Screen == SessionScreen.Playing ? Apply(Game.Draw()) : NotNow();

    /// <summary>Passes; see <see cref="TriominosGame.Pass"/>.</summary>
    public MoveResult Pass() => Screen == SessionScreen.Playing ? Apply(Game.Pass()) : NotNow();

    /// <summary>The next player is ready: shows their rack.</summary>
    public void Continue()
    {
        if (Screen == SessionScreen.Handover)
        {
            Screen = SessionScreen.Playing;
        }
    }

    private static MoveResult NotNow() => new(MoveError.WrongPhase, []);

    private MoveResult Apply(MoveResult result)
    {
        if (!result.Succeeded)
        {
            Message = Describe(result.Error);
            return result;
        }

        Message = Describe(result.Events);
        if (result.Events.OfType<GameFinished>().Any())
        {
            Screen = SessionScreen.Finished;
        }
        else if (result.Events.OfType<TurnStarted>().Any())
        {
            Screen = SessionScreen.Handover;
        }

        return result;
    }

    private string Describe(IReadOnlyList<GameEvent> events)
    {
        var parts = new List<string>();
        foreach (var gameEvent in events)
        {
            switch (gameEvent)
            {
                case TilePlaced placed:
                    var verb = Game.Board.Tiles.Count == 1 ? "begint met" : "legt";
                    parts.Add($"{Name(placed.Player)} {verb} {placed.Tile.Tile} voor {placed.Score.Total} punten{BonusText(placed.Score)}.");
                    break;
                case TileDrawn drawn:
                    parts.Add($"{Name(drawn.Player)} trekt een steen (-{drawn.Penalty}).");
                    break;
                case TurnPassed passed:
                    parts.Add($"{Name(passed.Player)} past (-{passed.Penalty}).");
                    break;
                case GameFinished { Outcome: { Reason: GameEndReason.WentOut } outcome }:
                    parts.Add($"{Name(outcome.Winner!.Value)} heeft geen stenen meer en krijgt {outcome.GoingOutBonus} bonuspunten.");
                    break;
                case GameFinished:
                    parts.Add("Niemand kan meer leggen: gelijkspel.");
                    break;
            }
        }

        return string.Join(' ', parts);
    }

    private string Name(int player) => Game.Players[player].Name;

    private static string BonusText(ScoreBreakdown score)
    {
        var bonuses = new List<string>();
        if (score.StartBonus > 0)
        {
            bonuses.Add("startbonus");
        }

        if (score.BridgeBonus > 0)
        {
            bonuses.Add("brugbonus");
        }

        if (score.HexagonBonus > 0)
        {
            bonuses.Add(HexagonText(score.HexagonBonus));
        }

        return bonuses.Count == 0 ? string.Empty : $" met {string.Join(" en ", bonuses)}";
    }

    internal static string HexagonText(int bonus)
    {
        if (bonus == Scoring.HexagonBonus(2))
        {
            return "dubbele hexagonbonus";
        }

        return bonus == Scoring.HexagonBonus(3) ? "drievoudige hexagonbonus" : "hexagonbonus";
    }

    private static string Describe(MoveError error)
    {
        return error switch
        {
            MoveError.WrongPhase => "Dat kan nu niet.",
            MoveError.TileNotInRack => "Die steen zit niet in je rek.",
            MoveError.NotAStartTile => "Begin met een van de gemarkeerde stenen.",
            MoveError.StartNotAtOrigin => "De eerste steen komt in het midden.",
            MoveError.InvalidRotation => "Die draaiing bestaat niet.",
            MoveError.CellOccupied => "Daar ligt al een steen.",
            MoveError.NotAdjacent => "Een steen moet een zijde delen met een liggende steen.",
            MoveError.CornerMismatch => "De hoeken passen daar niet.",
            MoveError.DrawLimitReached => $"Je hebt al {Scoring.MaxDrawsPerTurn} stenen getrokken.",
            MoveError.PoolEmpty => "De pot is leeg.",
            MoveError.PassNotAllowed => "Passen mag pas als je niets kunt leggen en niet meer kunt trekken.",
            _ => "Dat kan niet.",
        };
    }
}
