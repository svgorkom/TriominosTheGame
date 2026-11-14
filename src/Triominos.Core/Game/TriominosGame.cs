using Triominos.Core.Board;
using Triominos.Core.Tiles;

namespace Triominos.Core.Game;

/// <summary>
/// One round of Triominos. The game is the single source of truth: the UI reads its read-only
/// state and changes it only through <see cref="Place"/>, <see cref="Draw"/> and <see cref="Pass"/>.
/// Every move returns the events it caused. All randomness comes from the injected
/// <see cref="Random"/>, so a seed reproduces a game.
/// </summary>
public sealed class TriominosGame
{
    private readonly List<Player> _players;
    private readonly Queue<Tile> _pool;
    private readonly List<Tile> _allowedStartTiles;
    private int _consecutivePasses;

    private TriominosGame(IReadOnlyList<string> playerNames, IReadOnlyList<IReadOnlyList<Tile>> racks, IEnumerable<Tile> pool)
    {
        _players = [.. playerNames.Select((name, i) => new Player(name, racks[i]))];
        _pool = new Queue<Tile>(pool);
        (CurrentPlayerIndex, _allowedStartTiles) = DetermineStart(_players);
        Phase = GamePhase.Starting;
    }

    /// <summary>Gets the board.</summary>
    public GameBoard Board { get; } = new();

    /// <summary>Gets the players in turn order.</summary>
    public IReadOnlyList<Player> Players => _players;

    /// <summary>Gets the index of the player whose turn it is.</summary>
    public int CurrentPlayerIndex { get; private set; }

    /// <summary>Gets the player whose turn it is.</summary>
    public Player CurrentPlayer => _players[CurrentPlayerIndex];

    /// <summary>Gets the phase.</summary>
    public GamePhase Phase { get; private set; }

    /// <summary>Gets the number of face-down tiles left to draw.</summary>
    public int PoolCount => _pool.Count;

    /// <summary>Gets how many tiles the current player drew this turn.</summary>
    public int DrawsThisTurn { get; private set; }

    /// <summary>Gets the tiles the starting player may lay; empty after the start.</summary>
    public IReadOnlyList<Tile> AllowedStartTiles => _allowedStartTiles;

    /// <summary>Gets the result once the round is finished; otherwise null.</summary>
    public GameOutcome? Outcome { get; private set; }

    /// <summary>Gets whether the current player may draw a tile now.</summary>
    public bool CanDraw => Phase == GamePhase.Playing && DrawsThisTurn < Scoring.MaxDrawsPerTurn && _pool.Count > 0;

    /// <summary>
    /// Gets whether the current player may pass: no valid move, and either three tiles drawn this
    /// turn or an empty pool.
    /// </summary>
    public bool CanPass =>
        Phase == GamePhase.Playing
        && (DrawsThisTurn >= Scoring.MaxDrawsPerTurn || _pool.Count == 0)
        && !HasValidMove(CurrentPlayer);

    /// <summary>
    /// Deals a new round. Two players get 9 tiles each, three or four players 7 each; the rest
    /// form the face-down pool.
    /// </summary>
    /// <param name="playerNames">Names in turn order; 2 to 4, none blank and none the same (see <see cref="PlayerNameCheck"/>).</param>
    /// <param name="random">Source of the shuffle.</param>
    public static TriominosGame Start(IReadOnlyList<string> playerNames, Random random)
    {
        ArgumentNullException.ThrowIfNull(playerNames);
        ArgumentNullException.ThrowIfNull(random);
        if (playerNames.Count is < Scoring.MinPlayers or > Scoring.MaxPlayers)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerNames),
                playerNames.Count,
                $"A game needs {Scoring.MinPlayers} to {Scoring.MaxPlayers} players.");
        }

        if (PlayerNameCheck.Find(playerNames) is { } issue)
        {
            throw new ArgumentException(
                $"The name of player {issue.Index + 1} is unusable: {issue.Problem}.",
                nameof(playerNames));
        }

        var tiles = TileSet.Standard().ToArray();
        random.Shuffle(tiles);

        var perPlayer = Scoring.TilesPerPlayer(playerNames.Count);
        var racks = Enumerable.Range(0, playerNames.Count)
            .Select(p => (IReadOnlyList<Tile>)tiles.Skip(p * perPlayer).Take(perPlayer).ToList())
            .ToList();
        var pool = tiles.Skip(playerNames.Count * perPlayer);

        return new TriominosGame(playerNames, racks, pool);
    }

    /// <summary>Creates a game from a fixed deal; for tests.</summary>
    /// <param name="playerNames">Names in turn order.</param>
    /// <param name="racks">The rack of each player.</param>
    /// <param name="pool">Pool tiles in the order they will be drawn.</param>
    internal static TriominosGame FromDeal(IReadOnlyList<string> playerNames, IReadOnlyList<IReadOnlyList<Tile>> racks, IEnumerable<Tile> pool)
    {
        if (playerNames.Count != racks.Count)
        {
            throw new ArgumentException("Every player needs a rack.", nameof(racks));
        }

        return new TriominosGame(playerNames, racks, pool);
    }

    /// <summary>
    /// Gets the valid placements of one of the current player's tiles. At the start these are the
    /// three rotations on <see cref="Cell.Origin"/> for an allowed starting tile.
    /// </summary>
    public IReadOnlyList<Placement> ValidPlacements(int tileId)
    {
        var tile = CurrentPlayer.FindTile(tileId);
        if (tile is null)
        {
            return [];
        }

        return Phase switch
        {
            GamePhase.Starting when _allowedStartTiles.Contains(tile) =>
                [new Placement(Cell.Origin, 0), new Placement(Cell.Origin, 1), new Placement(Cell.Origin, 2)],
            GamePhase.Playing => Board.ValidPlacements(tile),
            _ => [],
        };
    }

    /// <summary>
    /// Gets the points <see cref="Place"/> would give for a move, without making it: the tile value
    /// plus any start, bridge or hexagon bonus. Returns null when <see cref="Place"/> would refuse
    /// the move.
    /// </summary>
    /// <param name="tileId">Id of a tile in the current player's rack.</param>
    /// <param name="placement">Where and how the tile would be laid.</param>
    public ScoreBreakdown? ScoreOf(int tileId, Placement placement)
    {
        if (Phase is GamePhase.Finished)
        {
            return null;
        }

        var tile = CurrentPlayer.FindTile(tileId);
        if (tile is null || ValidateMove(tile, placement) != MoveError.None)
        {
            return null;
        }

        return Phase == GamePhase.Starting ? StartScore(tile) : PlayScore(tile, placement.Cell);
    }

    /// <summary>
    /// Lays a tile from the current player's rack. Scores the tile value plus any start, bridge or
    /// hexagon bonus. Ends the round when the rack is empty; otherwise the turn passes on.
    /// </summary>
    public MoveResult Place(int tileId, Placement placement)
    {
        if (Phase is GamePhase.Finished)
        {
            return MoveResult.Fail(MoveError.WrongPhase);
        }

        var player = CurrentPlayer;
        var tile = player.FindTile(tileId);
        if (tile is null)
        {
            return MoveResult.Fail(MoveError.TileNotInRack);
        }

        var error = ValidateMove(tile, placement);
        if (error != MoveError.None)
        {
            return MoveResult.Fail(error);
        }

        var score = Phase == GamePhase.Starting ? StartScore(tile) : PlayScore(tile, placement.Cell);
        var placed = Board.Place(tile, placement);
        player.RemoveTile(tile);
        player.Score += score.Total;
        _consecutivePasses = 0;
        _allowedStartTiles.Clear();
        Phase = GamePhase.Playing;

        var events = new List<GameEvent> { new TilePlaced(CurrentPlayerIndex, placed, score) };
        if (player.Rack.Count == 0)
        {
            FinishWentOut(events);
        }
        else
        {
            AdvanceTurn(events);
        }

        return new MoveResult(MoveError.None, events);
    }

    /// <summary>Draws a face-down tile into the current player's rack, for a penalty of 5 points.</summary>
    public MoveResult Draw()
    {
        if (Phase != GamePhase.Playing)
        {
            return MoveResult.Fail(MoveError.WrongPhase);
        }

        if (DrawsThisTurn >= Scoring.MaxDrawsPerTurn)
        {
            return MoveResult.Fail(MoveError.DrawLimitReached);
        }

        if (!_pool.TryDequeue(out var tile))
        {
            return MoveResult.Fail(MoveError.PoolEmpty);
        }

        CurrentPlayer.AddTile(tile);
        CurrentPlayer.Score -= Scoring.DrawPenalty;
        DrawsThisTurn++;

        return new MoveResult(MoveError.None, [new TileDrawn(CurrentPlayerIndex, tile, Scoring.DrawPenalty)]);
    }

    /// <summary>
    /// Passes the turn for a penalty of 10 points; see <see cref="CanPass"/>. When the pool is empty
    /// and every player has passed in a row, the round ends as a draw.
    /// </summary>
    public MoveResult Pass()
    {
        if (Phase != GamePhase.Playing)
        {
            return MoveResult.Fail(MoveError.WrongPhase);
        }

        if (!CanPass)
        {
            return MoveResult.Fail(MoveError.PassNotAllowed);
        }

        CurrentPlayer.Score -= Scoring.PassPenalty;
        _consecutivePasses++;

        var events = new List<GameEvent> { new TurnPassed(CurrentPlayerIndex, Scoring.PassPenalty) };

        // No tile was placed since the earliest of these passes, so no player can move any more.
        if (_pool.Count == 0 && _consecutivePasses >= _players.Count)
        {
            Finish(new GameOutcome(GameEndReason.Blocked, null, 0), events);
        }
        else
        {
            AdvanceTurn(events);
        }

        return new MoveResult(MoveError.None, events);
    }

    /// <summary>
    /// Decides who starts: the player with the highest triple, who may also choose 0-0-0 if they
    /// hold it. Without triples, the player with the highest tile value starts (ties are broken by
    /// the next-highest tile), choosing any of their tiles with that value. If two racks are
    /// identical in value, the earlier player starts.
    /// </summary>
    private static (int Player, List<Tile> Allowed) DetermineStart(IReadOnlyList<Player> players)
    {
        var starter = -1;
        Tile? bestTriple = null;
        for (var i = 0; i < players.Count; i++)
        {
            foreach (var tile in players[i].Rack.Where(t => t.IsTriple))
            {
                if (bestTriple is null || tile.A > bestTriple.A)
                {
                    bestTriple = tile;
                    starter = i;
                }
            }
        }

        if (bestTriple is not null)
        {
            var allowed = new List<Tile> { bestTriple };
            var tripleZero = players[starter].Rack.FirstOrDefault(t => t.IsTriple && t.A == 0);
            if (tripleZero is not null && tripleZero != bestTriple)
            {
                allowed.Add(tripleZero);
            }

            return (starter, allowed);
        }

        starter = 0;
        for (var i = 1; i < players.Count; i++)
        {
            if (CompareRackValues(players[i], players[starter]) > 0)
            {
                starter = i;
            }
        }

        var rack = players[starter].Rack;
        var highest = rack.Count == 0 ? 0 : rack.Max(t => t.Value);
        return (starter, [.. rack.Where(t => t.Value == highest)]);
    }

    private static int CompareRackValues(Player a, Player b)
    {
        var valuesA = a.Rack.Select(t => t.Value).OrderDescending().ToList();
        var valuesB = b.Rack.Select(t => t.Value).OrderDescending().ToList();
        for (var i = 0; i < Math.Min(valuesA.Count, valuesB.Count); i++)
        {
            var comparison = valuesA[i].CompareTo(valuesB[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return valuesA.Count.CompareTo(valuesB.Count);
    }

    private static ScoreBreakdown StartScore(Tile tile)
    {
        var bonus = 0;
        if (tile.IsTriple)
        {
            bonus = Scoring.StartTripleBonus + (tile.A == 0 ? Scoring.StartTripleZeroBonus : 0);
        }

        return new ScoreBreakdown(tile.Value, bonus, 0, 0);
    }

    private MoveError ValidateMove(Tile tile, Placement placement)
    {
        if (Phase != GamePhase.Starting)
        {
            return Board.Validate(tile, placement);
        }

        if (!_allowedStartTiles.Contains(tile))
        {
            return MoveError.NotAStartTile;
        }

        if (!placement.HasValidRotation)
        {
            return MoveError.InvalidRotation;
        }

        return placement.Cell == Cell.Origin ? MoveError.None : MoveError.StartNotAtOrigin;
    }

    private ScoreBreakdown PlayScore(Tile tile, Cell cell) => PlayScore(Board, tile, cell);

    /// <summary>The points a tile earns on a cell of a board that already has tiles, before it is placed.</summary>
    internal static ScoreBreakdown PlayScore(GameBoard board, Tile tile, Cell cell)
    {
        var bonus = board.EvaluateBonus(cell);
        return new ScoreBreakdown(
            tile.Value,
            0,
            bonus.Bridge ? Scoring.BridgeBonus : 0,
            Scoring.HexagonBonus(bonus.Hexagons));
    }

    private bool HasValidMove(Player player) => player.Rack.Any(t => Board.ValidPlacements(t).Count > 0);

    private void AdvanceTurn(List<GameEvent> events)
    {
        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % _players.Count;
        DrawsThisTurn = 0;
        events.Add(new TurnStarted(CurrentPlayerIndex));
    }

    private void FinishWentOut(List<GameEvent> events)
    {
        var bonus = Scoring.GoingOutBonus + _players.Where(p => p != CurrentPlayer).Sum(p => p.RackValue);
        CurrentPlayer.Score += bonus;
        Finish(new GameOutcome(GameEndReason.WentOut, CurrentPlayerIndex, bonus), events);
    }

    private void Finish(GameOutcome outcome, List<GameEvent> events)
    {
        Outcome = outcome;
        Phase = GamePhase.Finished;
        events.Add(new GameFinished(outcome));
    }
}
