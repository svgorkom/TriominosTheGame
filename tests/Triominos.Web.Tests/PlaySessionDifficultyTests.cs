using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Web.Play;

namespace Triominos.Web.Tests;

/// <summary>The difficulty levels in a game on one screen: the clock, the help of the easiest level and taking a move back.</summary>
public class PlaySessionDifficultyTests
{
    private static readonly PlayerSetup[] TwoHumans =
    [
        new("Ann", PlayerColors.All[0]),
        new("Bob", PlayerColors.All[1]),
    ];

    private static readonly PlayerSetup[] HumanAndComputer =
    [
        new("Ann", PlayerColors.All[0]),
        new("Computer", PlayerColors.All[1], IsComputer: true),
    ];

    /// <summary>Starts two humans and lays the first tile, so the second player is on the handover screen.</summary>
    private static PlaySession StartedByHumans(Difficulty difficulty)
    {
        var session = new PlaySession(TwoHumans, new Random(7), difficulty);
        session.Continue();
        var tile = session.Game.AllowedStartTiles[0];
        Assert.True(session.Place(tile.Id, new Placement(Cell.Origin, 0)).Succeeded);
        return session;
    }

    /// <summary>A game where Ann starts against the computer, after her first tile and the computer's answer.</summary>
    private static PlaySession AnnAgainstTheComputer(Difficulty difficulty)
    {
        var seed = Enumerable.Range(0, 500).First(s => new PlaySession(HumanAndComputer, new Random(s)).Game.CurrentPlayerIndex == 0);
        var session = new PlaySession(HumanAndComputer, new Random(seed), difficulty);
        var tile = session.Game.AllowedStartTiles[0];
        Assert.True(session.Place(tile.Id, new Placement(Cell.Origin, 0)).Succeeded);
        while (session.IsComputerTurn)
        {
            Assert.True(session.PlayComputerStep().Succeeded);
        }

        return session;
    }

    // --- The clock ------------------------------------------------------------------------------

    [Fact]
    public void Without_a_clock_time_changes_nothing()
    {
        var session = StartedByHumans(Difficulty.Normal);
        session.Continue();

        Assert.Null(session.Clock);
        Assert.Null(session.Tick(TimeSpan.FromHours(1)));
        Assert.Null(session.TimeLeft(session.Game.CurrentPlayerIndex));
    }

    [Fact]
    public void The_clock_stands_still_at_the_start_and_on_the_handover_screen()
    {
        var session = new PlaySession(TwoHumans, new Random(7), Difficulty.Hard);
        session.Continue();
        Assert.Null(session.Tick(TimeSpan.FromMinutes(5)));

        var tile = session.Game.AllowedStartTiles[0];
        session.Place(tile.Id, new Placement(Cell.Origin, 0));
        Assert.Equal(SessionScreen.Handover, session.Screen);
        Assert.Null(session.Tick(TimeSpan.FromMinutes(5)));

        session.Continue();
        Assert.Equal(TimeSpan.FromSeconds(60), session.TimeLeft(session.Game.CurrentPlayerIndex));
    }

    [Fact]
    public void When_the_turn_time_runs_out_the_player_draws_and_gets_a_fresh_minute()
    {
        var session = StartedByHumans(Difficulty.Hard);
        session.Continue();
        var player = session.Game.CurrentPlayerIndex;

        Assert.Null(session.Tick(TimeSpan.FromSeconds(59)));
        Assert.Equal("0:01", TurnClock.Format(session.TimeLeft(player)!.Value));
        var result = session.Tick(TimeSpan.FromSeconds(2));

        Assert.NotNull(result);
        Assert.IsType<TimeRanOut>(result.Events[0]);
        Assert.IsType<TileDrawn>(result.Events[1]);
        Assert.Contains("De tijd van", session.Message, StringComparison.Ordinal);
        Assert.Equal(player, session.Game.CurrentPlayerIndex);
        Assert.Equal(1, session.Game.DrawsThisTurn);
        Assert.Equal(TimeSpan.FromSeconds(60), session.TimeLeft(player));
    }

    [Fact]
    public void The_game_clock_lasts_the_whole_game_and_then_every_turn_gets_fifteen_seconds()
    {
        var session = StartedByHumans(Difficulty.VeryHard);
        session.Continue();
        var player = session.Game.CurrentPlayerIndex;

        Assert.Equal(TimeSpan.FromMinutes(8), session.TimeLeft(player));
        Assert.Null(session.Tick(TimeSpan.FromMinutes(8)));
        Assert.Equal(TimeSpan.FromSeconds(15), session.TimeLeft(player));

        var result = session.Tick(TimeSpan.FromSeconds(15));

        Assert.NotNull(result);
        Assert.IsType<TimeRanOut>(result.Events[0]);
        Assert.Equal(TimeSpan.Zero, session.Clock!.GameLeft(player));
    }

    [Fact]
    public void The_clock_shows_minutes_and_seconds_rounded_up()
    {
        Assert.Equal("8:00", TurnClock.Format(TimeSpan.FromMinutes(8)));
        Assert.Equal("0:42", TurnClock.Format(TimeSpan.FromSeconds(41.2)));
        Assert.Equal("0:00", TurnClock.Format(TimeSpan.FromSeconds(-3)));
    }

    // --- The mistake penalty --------------------------------------------------------------------

    [Fact]
    public void At_very_hard_a_tile_that_does_not_match_costs_five_points()
    {
        var session = StartedByHumans(Difficulty.VeryHard);
        session.Continue();
        var game = session.Game;
        var (tileId, placement) = game.CurrentPlayer.Rack
            .SelectMany(t => game.Board.OpenCells.SelectMany(c => Enumerable.Range(0, 3).Select(r => (t, Placement: new Placement(c, r)))))
            .Where(x => game.Board.Validate(x.t, x.Placement) == MoveError.CornerMismatch)
            .Select(x => (x.t.Id, x.Placement))
            .First();

        var result = session.Place(tileId, placement);

        Assert.Equal(MoveError.CornerMismatch, result.Error);
        Assert.Equal(-Difficulties.VeryHardMistakePenalty, game.CurrentPlayer.Score);
        Assert.EndsWith($"Dat kost {Difficulties.VeryHardMistakePenalty} punten.", session.Message, StringComparison.Ordinal);
        Assert.False(session.ShowsOpenCells);
    }

    // --- The help of the easiest level ----------------------------------------------------------

    [Fact]
    public void Only_easy_helps_the_player_and_only_very_hard_hides_the_open_cells()
    {
        foreach (var level in Difficulties.All)
        {
            var session = new PlaySession(TwoHumans, new Random(7), level);
            Assert.Equal(level == Difficulty.Easy, session.Assists);
            Assert.Equal(level != Difficulty.VeryHard, session.ShowsOpenCells);
        }
    }

    [Fact]
    public void At_easy_every_tile_that_fits_is_marked()
    {
        var session = StartedByHumans(Difficulty.Easy);
        session.Continue();
        var game = session.Game;

        var fitting = game.CurrentPlayer.Rack.Where(t => game.ValidPlacements(t.Id).Count > 0).ToList();

        Assert.Equal(fitting.OrderBy(t => t.Id), session.MarkedTiles.OrderBy(t => t.Id));
    }

    [Fact]
    public void At_normal_fitting_tiles_are_not_marked()
    {
        var session = StartedByHumans(Difficulty.Normal);
        session.Continue();

        Assert.Empty(session.MarkedTiles);
    }

    [Fact]
    public void At_easy_the_rack_of_the_computer_is_open()
    {
        var easy = AnnAgainstTheComputer(Difficulty.Easy);
        var normal = AnnAgainstTheComputer(Difficulty.Normal);

        Assert.Equal(easy.Game.Players[1].Rack, easy.OpenRack(1));
        Assert.Empty(easy.OpenRack(0));
        Assert.Empty(normal.OpenRack(1));
    }

    // --- Taking a move back ---------------------------------------------------------------------

    [Fact]
    public void At_easy_the_only_human_takes_back_their_tile_and_the_computer_answer()
    {
        var session = AnnAgainstTheComputer(Difficulty.Easy);
        Assert.True(session.CanUndo);

        Assert.True(session.Undo().Succeeded);

        Assert.True(session.Game.Board.IsEmpty);
        Assert.Equal(0, session.Game.CurrentPlayerIndex);
        Assert.Equal(GamePhase.Starting, session.Game.Phase);
        Assert.Equal("Ann neemt de laatste zet terug.", session.Message);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Undo_is_not_offered_above_easy()
    {
        Assert.False(AnnAgainstTheComputer(Difficulty.Normal).CanUndo);
        Assert.False(AnnAgainstTheComputer(Difficulty.Normal).Undo().Succeeded);
    }

    [Fact]
    public void With_two_humans_a_move_cannot_be_taken_back()
    {
        var session = StartedByHumans(Difficulty.Easy);
        session.Continue();

        Assert.False(session.CanUndo);
    }
}
