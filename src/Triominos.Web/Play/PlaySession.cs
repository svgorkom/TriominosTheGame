using Triominos.Core.Ai;
using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Core.Tiles;

namespace Triominos.Web.Play;

/// <summary>Which screen the table shows.</summary>
public enum SessionScreen
{
    /// <summary>The next human has to take over; their rack is hidden until they are ready. Only used when several humans share the screen.</summary>
    Handover,

    /// <summary>The current player plays. A human's rack is shown; a computer plays by itself, see <see cref="PlaySession.IsComputerTurn"/>.</summary>
    Playing,

    /// <summary>The round is over.</summary>
    Finished,
}

/// <summary>
/// One game on one screen as the UI sees it: the Core game plus what only the UI cares about, which
/// is the players' colours, who is a computer, the handover between humans that hides the rack and
/// the Dutch message about the last move. All rules stay in <see cref="TriominosGame"/>, and the
/// computer's choices in <see cref="ComputerPlayer"/>.
/// </summary>
public sealed class PlaySession
{
    private readonly Random _random;

    /// <summary>Starts a game for 2 to 4 players on one screen.</summary>
    /// <param name="players">The players in turn order; their names must pass <see cref="PlayerNameCheck"/>.</param>
    /// <param name="random">Source of the shuffle.</param>
    public PlaySession(IReadOnlyList<PlayerSetup> players, Random random)
    {
        ArgumentNullException.ThrowIfNull(players);
        _random = random;
        Setup = players;
        Game = TriominosGame.Start([.. players.Select(p => p.Name)], random);
        Screen = ScreenForTurn();
        Message = Game.Phase == GamePhase.Starting && !IsComputerSeat(Game.CurrentPlayerIndex)
            ? $"{Game.CurrentPlayer.Name} begint met een van de gemarkeerde stenen."
            : $"{Game.CurrentPlayer.Name} is aan de beurt.";
    }

    /// <summary>Gets the game.</summary>
    public TriominosGame Game { get; }

    /// <summary>Gets the players as chosen on the setup screen, in turn order (the same order as <see cref="TriominosGame.Players"/>).</summary>
    public IReadOnlyList<PlayerSetup> Setup { get; }

    /// <summary>Gets the screen to show.</summary>
    public SessionScreen Screen { get; private set; }

    /// <summary>Gets the message about the last move or error.</summary>
    public string Message { get; private set; }

    /// <summary>Gets whether it is the turn of a computer player; the UI then asks for <see cref="PlayComputerStep"/> and takes no input.</summary>
    public bool IsComputerTurn => Screen == SessionScreen.Playing && IsComputerSeat(Game.CurrentPlayerIndex);

    /// <summary>
    /// Gets the rack to show: a human's own while they play. During a computer's turn the rack of
    /// the only human stays in view; with several humans it is hidden, because whose it would be is
    /// not known.
    /// </summary>
    public IReadOnlyList<Tile> VisibleRack
    {
        get
        {
            if (Screen != SessionScreen.Playing)
            {
                return [];
            }

            if (!IsComputerTurn)
            {
                return Game.CurrentPlayer.Rack;
            }

            return SoleHumanIndex is { } human ? Game.Players[human].Rack : [];
        }
    }

    /// <summary>Gets the tiles to single out in the rack: the allowed starting tiles of a human player.</summary>
    public IReadOnlyList<Tile> MarkedTiles => Screen == SessionScreen.Playing && !IsComputerTurn ? Game.AllowedStartTiles : [];

    /// <summary>Gets whether the current player may draw now; never during a computer's turn.</summary>
    public bool CanDraw => Screen == SessionScreen.Playing && !IsComputerTurn && Game.CanDraw;

    /// <summary>Gets whether the current player may pass now; never during a computer's turn.</summary>
    public bool CanPass => Screen == SessionScreen.Playing && !IsComputerTurn && Game.CanPass;

    /// <summary>Gets the player who went out, or null when the round is not over or ended in a draw.</summary>
    public Player? Winner => Game.Outcome?.Winner is { } index ? Game.Players[index] : null;

    /// <summary>
    /// Returns where a tile of the current player fits. With no placement, the message says why
    /// the tile cannot be picked up.
    /// </summary>
    public IReadOnlyList<Placement> Pick(int tileId)
    {
        if (IsComputerTurn)
        {
            Message = ComputerTurnMessage;
            return [];
        }

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
        HumanCanMove ? Apply(Game.Place(tileId, placement)) : NotNow();

    /// <summary>Draws a tile; see <see cref="TriominosGame.Draw"/>.</summary>
    public MoveResult Draw() => HumanCanMove ? Apply(Game.Draw()) : NotNow();

    /// <summary>Passes; see <see cref="TriominosGame.Pass"/>.</summary>
    public MoveResult Pass() => HumanCanMove ? Apply(Game.Pass()) : NotNow();

    /// <summary>
    /// Plays one move for the computer whose turn it is: one tile laid, one tile drawn or one pass.
    /// A computer that has to draw takes several steps, so the UI can show each one.
    /// </summary>
    public MoveResult PlayComputerStep()
    {
        if (!IsComputerTurn)
        {
            return NotNow();
        }

        var level = Setup[Game.CurrentPlayerIndex].Level;
        var result = ComputerPlayer.Decide(Game, level, _random) switch
        {
            PlaceMove place => Game.Place(place.TileId, place.Placement),
            DrawMove => Game.Draw(),
            _ => Game.Pass(),
        };
        return Apply(result);
    }

    /// <summary>The next player is ready: shows their rack.</summary>
    public void Continue()
    {
        if (Screen == SessionScreen.Handover)
        {
            Screen = SessionScreen.Playing;
        }
    }

    private const string ComputerTurnMessage = "De computer is aan de beurt.";

    private bool HumanCanMove => Screen == SessionScreen.Playing && !IsComputerTurn;

    /// <summary>Gets the index of the only human player, or null when there are several (or none).</summary>
    private int? SoleHumanIndex => Setup.Count(p => !p.IsComputer) == 1 ? Setup.ToList().FindIndex(p => !p.IsComputer) : null;

    private bool IsComputerSeat(int index) => Setup[index].IsComputer;

    /// <summary>
    /// The screen a new turn starts on. The handover hides a rack from the other humans, so it is
    /// only needed when two or more humans share the screen, and never for a computer.
    /// </summary>
    private SessionScreen ScreenForTurn() =>
        IsComputerSeat(Game.CurrentPlayerIndex) || Setup.Count(p => !p.IsComputer) < 2
            ? SessionScreen.Playing
            : SessionScreen.Handover;

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
            Screen = ScreenForTurn();
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
