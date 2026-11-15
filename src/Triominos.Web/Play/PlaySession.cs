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
    /// <param name="difficulty">How hard the game is: its house rules (<see cref="Difficulties.Rules"/>) and the help the UI gives.</param>
    public PlaySession(IReadOnlyList<PlayerSetup> players, Random random, Difficulty difficulty = Difficulty.Normal)
    {
        ArgumentNullException.ThrowIfNull(players);
        _random = random;
        Setup = players;
        Difficulty = difficulty;
        Game = TriominosGame.Start([.. players.Select(p => p.Name)], random, Difficulties.Rules(difficulty));
        Clock = Game.Rules.Clock is { } clock ? new TurnClock(clock, players.Count) : null;
        Clock?.StartTurn(Game.CurrentPlayerIndex);
        Screen = ScreenForTurn();
        Message = Game.Phase == GamePhase.Starting && !IsComputerSeat(Game.CurrentPlayerIndex)
            ? $"{Game.CurrentPlayer.Name} begint met een van de gemarkeerde stenen."
            : $"{Game.CurrentPlayer.Name} is aan de beurt.";
    }

    /// <summary>Gets the game.</summary>
    public TriominosGame Game { get; }

    /// <summary>Gets how hard the game is.</summary>
    public Difficulty Difficulty { get; }

    /// <summary>Gets whether the open cells light up when a tile is picked up; see <see cref="DifficultyChoices.ShowsOpenCells"/>.</summary>
    public bool ShowsOpenCells => DifficultyChoices.ShowsOpenCells(Difficulty);

    /// <summary>Gets whether the UI helps the player; see <see cref="DifficultyChoices.Assists"/>.</summary>
    public bool Assists => DifficultyChoices.Assists(Difficulty);

    /// <summary>Gets the clock of the humans, or null when the game has none. It only runs during a human's turn after the start; see <see cref="Tick"/>.</summary>
    public TurnClock? Clock { get; }

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

    /// <summary>
    /// Gets the tiles to single out in the rack: the allowed starting tiles of a human player and
    /// the tile a hint suggests; when the UI helps the player, every tile that fits somewhere.
    /// </summary>
    public IReadOnlyList<Tile> MarkedTiles
    {
        get
        {
            if (Screen != SessionScreen.Playing || IsComputerTurn)
            {
                return [];
            }

            var marked = new List<Tile>(Game.AllowedStartTiles);
            if (Assists && Game.Phase == GamePhase.Playing)
            {
                marked.AddRange(Game.CurrentPlayer.Rack.Where(t => Game.ValidPlacements(t.Id).Count > 0));
            }

            var hinted = Hint is PlaceMove place ? Game.CurrentPlayer.Rack.FirstOrDefault(t => t.Id == place.TileId) : null;
            if (hinted is not null)
            {
                marked.Add(hinted);
            }

            return [.. marked.Distinct()];
        }
    }

    /// <summary>
    /// Returns the rack of a computer player when the UI helps the player (the open racks of the
    /// easiest level), so they can learn from it; otherwise an empty list.
    /// </summary>
    /// <param name="seat">Index of the player.</param>
    public IReadOnlyList<Tile> OpenRack(int seat) => Assists && IsComputerSeat(seat) ? Game.Players[seat].Rack : [];

    /// <summary>
    /// Gets whether the current player may take back their last placement now: the level allows it,
    /// it is their turn, and they are the only human, because every other player has moved since
    /// and only a computer's moves may be undone with it. See <see cref="TriominosGame.Undo"/>.
    /// </summary>
    public bool CanUndo => HumanCanMove && Game.CanUndo && SoleHumanIndex == Game.CurrentPlayerIndex;

    /// <summary>
    /// Returns the time to show for a player, or null: the turn time of the human whose turn it is,
    /// or the game time a human has left. Computers have no clock.
    /// </summary>
    /// <param name="seat">Index of the player.</param>
    public TimeSpan? TimeLeft(int seat)
    {
        if (Clock is null || IsComputerSeat(seat))
        {
            return null;
        }

        return seat == Game.CurrentPlayerIndex && Game.Phase == GamePhase.Playing && Clock.TurnLeft is { } turn
            ? turn
            : Clock.GameLeft(seat);
    }

    /// <summary>Gets the move the current player paid a hint for, or null. It lasts until the player makes a move or draws.</summary>
    public ComputerMove? Hint { get; private set; }

    /// <summary>Gets the cell a hint points at, or null when there is no hint or it says to draw or pass.</summary>
    public Cell? HintCell => Hint is PlaceMove place ? place.Placement.Cell : null;

    /// <summary>Gets whether the current player may ask for a hint now; not while a hint is showing and never during a computer's turn.</summary>
    public bool CanHint => HumanCanMove && Game.CanHint && Hint is null;

    /// <summary>Gets how many hints the current player has left.</summary>
    public int HintsLeft => Game.CurrentPlayer.HintsLeft;

    /// <summary>Gets whether the current player may draw now; never during a computer's turn.</summary>
    public bool CanDraw => Screen == SessionScreen.Playing && !IsComputerTurn && Game.CanDraw;

    /// <summary>Gets whether the current player may pass now; never during a computer's turn.</summary>
    public bool CanPass => Screen == SessionScreen.Playing && !IsComputerTurn && Game.CanPass;

    /// <summary>Gets the player who went out, or null when the round is not over or ended in a draw.</summary>
    public Player? Winner => Game.Outcome?.Winner is { } index ? Game.Players[index] : null;

    /// <summary>
    /// Returns where a tile of the current player fits. A tile that fits nowhere can still be picked
    /// up, so the player gets no hint; only in the starting phase the message points at the marked tiles.
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
        if (placements.Count == 0 && Game.Phase == GamePhase.Starting)
        {
            Message = "Begin met een van de gemarkeerde stenen.";
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
    /// Charges the current player for a hint (see <see cref="TriominosGame.Hint"/>) and works out
    /// the suggestion: the move the normal computer level would make with these tiles. The player
    /// stays free to ignore it.
    /// </summary>
    public MoveResult RequestHint()
    {
        if (!HumanCanMove || Hint is not null)
        {
            return NotNow();
        }

        var result = Game.Hint();
        if (!result.Succeeded)
        {
            Message = Describe(result.Error);
            return result;
        }

        var advice = ComputerPlayer.Decide(Game);
        Hint = advice;
        Message = HintText(advice);
        return result;
    }

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

    /// <summary>
    /// Takes back the current player's last placement and the computer moves after it; see
    /// <see cref="CanUndo"/>. The turn time starts again.
    /// </summary>
    public MoveResult Undo()
    {
        if (!CanUndo)
        {
            return NotNow();
        }

        var result = Game.Undo();
        if (!result.Succeeded)
        {
            Message = Describe(result.Error);
            return result;
        }

        Hint = null;
        Clock?.StartTurn(Game.CurrentPlayerIndex);
        Message = $"{Game.CurrentPlayer.Name} neemt de laatste zet terug.";
        return result;
    }

    /// <summary>
    /// Lets time pass on the clock of the human whose turn it is. When their time runs out they draw
    /// or pass (<see cref="TriominosGame.TimeOut"/>) and the result is returned; otherwise null. The
    /// clock stands still without a clock, before the first tile lies, on the handover screen and
    /// during a computer's turn; the page also stops it while the scene plays or a menu is open.
    /// </summary>
    /// <param name="elapsed">Time since the last tick.</param>
    public MoveResult? Tick(TimeSpan elapsed)
    {
        if (Clock is null || !HumanCanMove || Game.Phase != GamePhase.Playing)
        {
            return null;
        }

        return Clock.Tick(elapsed) ? Apply(Game.TimeOut()) : null;
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
            if (result.Events.OfType<MistakeMade>().FirstOrDefault() is { } mistake)
            {
                Message += $" Dat kost {mistake.Penalty} punten.";
            }

            return result;
        }

        Hint = null;
        Message = Describe(result.Events);
        if (result.Events.OfType<GameFinished>().Any())
        {
            Screen = SessionScreen.Finished;
        }
        else if (result.Events.OfType<TurnStarted>().Any())
        {
            Screen = ScreenForTurn();
            Clock?.StartTurn(Game.CurrentPlayerIndex);
        }
        else if (result.Events.OfType<TileDrawn>().Any())
        {
            // A drawn tile gives a fresh turn time; the game time goes on.
            Clock?.StartTurn(Game.CurrentPlayerIndex);
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
                case TimeRanOut ran:
                    parts.Add($"De tijd van {Name(ran.Player)} is op.");
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

    private string HintText(ComputerMove move)
    {
        var prefix = $"Hint (-{Game.Hints.Penalty}, nog {Game.CurrentPlayer.HintsLeft} over):";
        switch (move)
        {
            case PlaceMove place when Game.ScoreOf(place.TileId, place.Placement) is { } score:
                var tile = Game.CurrentPlayer.Rack.First(t => t.Id == place.TileId);
                return $"{prefix} leg de gemarkeerde steen {tile} op de gloeiende plek voor {score.Total} punten{BonusText(score)}.{RotationText(place.Placement.Rotation)}";
            case DrawMove:
                return $"{prefix} geen enkele steen past. Trek een steen.";
            case PassMove:
                return $"{prefix} geen enkele steen past en trekken kan niet meer. Pas.";
            default:
                throw new InvalidOperationException("The computer level chose a move that the game does not allow.");
        }
    }

    /// <summary>How to turn a picked-up tile to the rotation of a placement; a tile starts at rotation 0, <c>R</c> adds 2 and <c>Shift+R</c> adds 1.</summary>
    private static string RotationText(int rotation) => rotation switch
    {
        1 => " Draai hem eerst één keer tegen de klok in (Shift+R).",
        2 => " Draai hem eerst één keer met de klok mee (R).",
        _ => string.Empty,
    };

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

    private string Describe(MoveError error)
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
            MoveError.PassNotAllowed => "Passen mag pas als je niet meer kunt trekken.",
            MoveError.HintLimitReached => Game.Hints.Enabled ? $"Je hebt al {Game.Hints.MaxPerPlayer} hints gebruikt." : "Hints staan uit in dit spel.",
            MoveError.NothingToUndo => "Je hebt nog geen steen gelegd.",
            _ => "Dat kan niet.",
        };
    }
}
