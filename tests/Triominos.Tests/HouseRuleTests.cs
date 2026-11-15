using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Core.Tiles;
using static Triominos.Tests.TestTiles;

namespace Triominos.Tests;

/// <summary>The house rules of the difficulty levels: the mistake penalty, running out of time and taking a move back.</summary>
public class HouseRuleTests
{
    private static readonly string[] TwoPlayers = ["Ann", "Bob"];

    /// <summary>
    /// Ann starts with 5-5-5. Bob holds 2-2-2, which fits nowhere next to it, and 5-5-1, which does.
    /// The pool holds four tiles.
    /// </summary>
    private static TriominosGame Deal(GameRules rules) =>
        TriominosGame.FromDeal(
            TwoPlayers,
            [[T(5, 5, 5), T(0, 1, 2), T(1, 2, 3)], [T(2, 2, 2), T(5, 5, 1), T(0, 0, 4)]],
            [T(0, 0, 1), T(0, 0, 2), T(0, 0, 3), T(0, 1, 1)],
            rules);

    private static Tile Rack(TriominosGame game, int a, int b, int c) =>
        game.CurrentPlayer.Rack.Single(t => (t.A, t.B, t.C) == (a, b, c));

    private static void AnnStarts(TriominosGame game)
    {
        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.True(game.Place(Rack(game, 5, 5, 5).Id, new Placement(Cell.Origin, 0)).Succeeded);
    }

    private static Placement Mismatch(TriominosGame game) => new(game.Board.OpenCells.First(), 0);

    private static void BobPlaces(TriominosGame game)
    {
        var tile = Rack(game, 5, 5, 1);
        Assert.True(game.Place(tile.Id, game.ValidPlacements(tile.Id)[0]).Succeeded);
    }

    // --- Mistake penalty ------------------------------------------------------------------------

    [Fact]
    public void A_mismatched_tile_costs_the_mistake_penalty_and_goes_back_to_the_rack()
    {
        var game = Deal(new GameRules(HintRules.Default, MistakePenalty: 5));
        AnnStarts(game);
        var tile = Rack(game, 2, 2, 2);

        var result = game.Place(tile.Id, Mismatch(game));

        Assert.Equal(MoveError.CornerMismatch, result.Error);
        Assert.False(result.Succeeded);
        Assert.Equal(new MistakeMade(1, 5), Assert.Single(result.Events));
        Assert.Equal(-5, game.CurrentPlayer.Score);
        Assert.Contains(tile, game.CurrentPlayer.Rack);
        Assert.Equal(1, game.CurrentPlayerIndex);
    }

    [Fact]
    public void Without_a_mistake_penalty_a_mismatched_tile_costs_nothing()
    {
        var game = Deal(GameRules.Default);
        AnnStarts(game);

        var result = game.Place(Rack(game, 2, 2, 2).Id, Mismatch(game));

        Assert.Equal(MoveError.CornerMismatch, result.Error);
        Assert.Empty(result.Events);
        Assert.Equal(0, game.CurrentPlayer.Score);
    }

    [Fact]
    public void A_tile_away_from_the_others_is_refused_without_a_penalty()
    {
        var game = Deal(new GameRules(HintRules.Default, MistakePenalty: 5));
        AnnStarts(game);

        var result = game.Place(Rack(game, 2, 2, 2).Id, new Placement(new Cell(10, 10), 0));

        Assert.Equal(MoveError.NotAdjacent, result.Error);
        Assert.Empty(result.Events);
        Assert.Equal(0, game.CurrentPlayer.Score);
    }

    // --- Running out of time --------------------------------------------------------------------

    [Fact]
    public void Running_out_of_time_draws_a_tile_while_drawing_is_allowed()
    {
        var game = Deal(GameRules.Default);
        AnnStarts(game);

        var result = game.TimeOut();

        Assert.True(result.Succeeded);
        Assert.IsType<TimeRanOut>(result.Events[0]);
        Assert.IsType<TileDrawn>(result.Events[1]);
        Assert.Equal(-Scoring.DrawPenalty, game.CurrentPlayer.Score);
        Assert.Equal(1, game.DrawsThisTurn);
        Assert.Equal(1, game.CurrentPlayerIndex);
    }

    [Fact]
    public void Running_out_of_time_after_three_draws_passes()
    {
        var game = Deal(GameRules.Default);
        AnnStarts(game);
        for (var i = 0; i < Scoring.MaxDrawsPerTurn; i++)
        {
            Assert.True(game.TimeOut().Succeeded);
        }

        var result = game.TimeOut();

        Assert.Equal(new TimeRanOut(1), result.Events[0]);
        Assert.IsType<TurnPassed>(result.Events[1]);
        Assert.Equal(-(Scoring.MaxDrawsPerTurn * Scoring.DrawPenalty) - Scoring.PassPenalty, game.Players[1].Score);
        Assert.Equal(0, game.CurrentPlayerIndex);
    }

    [Fact]
    public void The_starting_player_cannot_run_out_of_time()
    {
        var game = Deal(GameRules.Default);

        Assert.Equal(MoveError.WrongPhase, game.TimeOut().Error);
        Assert.Equal(GamePhase.Starting, game.Phase);
    }

    // --- Taking a move back ---------------------------------------------------------------------

    [Fact]
    public void Undo_is_off_unless_the_rules_allow_it()
    {
        var game = Deal(GameRules.Default);
        AnnStarts(game);
        BobPlaces(game);

        Assert.False(game.CanUndo);
        Assert.Equal(MoveError.WrongPhase, game.Undo().Error);
    }

    [Fact]
    public void A_player_who_has_not_placed_a_tile_has_nothing_to_undo()
    {
        var game = Deal(new GameRules(HintRules.Default, UndoAllowed: true));
        AnnStarts(game);

        Assert.False(game.CanUndo);
        Assert.Equal(MoveError.NothingToUndo, game.Undo().Error);
    }

    [Fact]
    public void Undo_takes_back_the_last_placement_and_every_move_after_it()
    {
        var game = Deal(new GameRules(HintRules.Default, UndoAllowed: true));
        AnnStarts(game);
        Assert.True(game.Draw().Succeeded);
        BobPlaces(game);
        Assert.True(game.CanUndo);

        var result = game.Undo();

        Assert.Equal(new MoveUndone(0), Assert.Single(result.Events));
        Assert.Equal(GamePhase.Starting, game.Phase);
        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.True(game.Board.IsEmpty);
        Assert.Equal(new[] { 0, 0 }, game.Players.Select(p => p.Score));
        Assert.Equal(3, game.Players[0].Rack.Count);
        Assert.Equal(3, game.Players[1].Rack.Count);
        Assert.Equal(4, game.PoolCount);
    }

    [Fact]
    public void Undo_keeps_what_the_player_did_before_the_placement()
    {
        var game = Deal(new GameRules(HintRules.Default, UndoAllowed: true));
        AnnStarts(game);
        Assert.True(game.Draw().Succeeded);
        BobPlaces(game);

        // Ann cannot place 0-1-2 or 1-2-3 next to two fives; she draws three times and passes.
        while (game.CurrentPlayerIndex == 0 && game.CanDraw)
        {
            Assert.True(game.Draw().Succeeded);
        }

        if (game.CurrentPlayerIndex == 0)
        {
            Assert.True(game.Pass().Succeeded);
        }

        Assert.Equal(1, game.CurrentPlayerIndex);
        Assert.True(game.CanUndo);

        Assert.True(game.Undo().Succeeded);

        Assert.Equal(1, game.CurrentPlayerIndex);
        Assert.Equal(GamePhase.Playing, game.Phase);
        Assert.Equal(1, game.DrawsThisTurn);
        Assert.Equal(-Scoring.DrawPenalty, game.Players[1].Score);
        Assert.Equal(4, game.Players[1].Rack.Count);
        Assert.Single(game.Board.Tiles);
        Assert.Equal(3, game.PoolCount);
    }

    [Fact]
    public void After_an_undo_the_same_moves_can_be_made_again()
    {
        var game = Deal(new GameRules(HintRules.Default, UndoAllowed: true));
        AnnStarts(game);
        var score = game.Players[0].Score;
        BobPlaces(game);

        Assert.True(game.Undo().Succeeded);
        AnnStarts(game);

        Assert.Equal(score, game.Players[0].Score);
        Assert.Equal(1, game.CurrentPlayerIndex);
    }

    // --- The levels -----------------------------------------------------------------------------

    [Fact]
    public void Normal_is_the_default_game()
    {
        Assert.Equal(GameRules.Default, Difficulties.Rules(Difficulty.Normal));
    }

    [Fact]
    public void Every_level_keeps_the_scoring_and_only_easy_allows_undo()
    {
        foreach (var level in Difficulties.All)
        {
            var rules = Difficulties.Rules(level);
            Assert.Equal(level == Difficulty.Easy, rules.UndoAllowed);
            Assert.Equal(level == Difficulty.VeryHard ? Difficulties.VeryHardMistakePenalty : 0, rules.MistakePenalty);
        }
    }

    [Fact]
    public void The_levels_get_harder_on_hints_and_time()
    {
        Assert.Equal(new HintRules(5, 0), Difficulties.Rules(Difficulty.Easy).Hints);
        Assert.Null(Difficulties.Rules(Difficulty.Easy).Clock);
        Assert.Equal(TimeSpan.FromSeconds(60), Difficulties.Rules(Difficulty.Hard).Clock?.Turn);
        Assert.False(Difficulties.Rules(Difficulty.VeryHard).Hints.Enabled);
        Assert.NotNull(Difficulties.Rules(Difficulty.VeryHard).Clock?.Game);
    }
}
