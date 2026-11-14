using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Core.Tiles;
using static Triominos.Tests.TestTiles;

namespace Triominos.Tests;

public class GameTests
{
    private static readonly string[] TwoPlayers = ["Ann", "Bob"];

    private static TriominosGame Deal(Tile[] first, Tile[] second, params Tile[] pool) =>
        TriominosGame.FromDeal(TwoPlayers, [first, second], pool);

    [Theory]
    [InlineData(2, 9)]
    [InlineData(3, 7)]
    [InlineData(4, 7)]
    public void Start_deals_tiles_by_player_count(int players, int perPlayer)
    {
        var names = Enumerable.Range(1, players).Select(i => $"P{i}").ToList();

        var game = TriominosGame.Start(names, new Random(1));

        Assert.All(game.Players, p => Assert.Equal(perPlayer, p.Rack.Count));
        Assert.Equal(TileSet.StandardSize - (players * perPlayer), game.PoolCount);
        Assert.Equal(GamePhase.Starting, game.Phase);
        Assert.True(game.Board.IsEmpty); // D-001: no random tile on the board
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Start_rejects_unsupported_player_counts(int players) // D-006
    {
        var names = Enumerable.Range(1, players).Select(i => $"P{i}").ToList();

        Assert.Throws<ArgumentOutOfRangeException>(() => TriominosGame.Start(names, new Random(1)));
    }

    [Fact]
    public void Same_seed_gives_the_same_deal()
    {
        var a = TriominosGame.Start(TwoPlayers, new Random(42));
        var b = TriominosGame.Start(TwoPlayers, new Random(42));

        Assert.Equal(a.Players[0].Rack, b.Players[0].Rack);
        Assert.Equal(a.Players[1].Rack, b.Players[1].Rack);
    }

    [Fact]
    public void Highest_triple_starts()
    {
        var five = T(5, 5, 5);
        var game = Deal([T(4, 4, 4), T(0, 1, 2)], [T(3, 3, 3), five]);

        Assert.Equal(1, game.CurrentPlayerIndex);
        Assert.Equal(new[] { five }, game.AllowedStartTiles);
    }

    [Fact]
    public void Starter_may_choose_triple_zero_for_forty_points()
    {
        var zero = T(0, 0, 0);
        var three = T(3, 3, 3);
        var game = Deal([zero, three, T(1, 2, 3)], [T(2, 2, 2)]);

        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.Equal(new[] { three, zero }, game.AllowedStartTiles);

        var result = game.Place(zero.Id, new Placement(Cell.Origin, 0));

        Assert.True(result.Succeeded);
        Assert.Equal(40, game.Players[0].Score);
    }

    [Fact]
    public void Starting_triple_scores_its_value_plus_ten()
    {
        var three = T(3, 3, 3);
        var game = Deal([T(0, 0, 0), three, T(1, 2, 3)], [T(2, 2, 2)]);

        game.Place(three.Id, new Placement(Cell.Origin, 1));

        Assert.Equal(19, game.Players[0].Score);
    }

    [Fact]
    public void Without_triples_the_highest_tile_starts_without_bonus_and_ties_use_the_next_tile()
    {
        var eight = T(1, 2, 5);
        var game = Deal([eight, T(0, 1, 2)], [T(1, 3, 4), T(0, 0, 1)]);

        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.Equal(new[] { eight }, game.AllowedStartTiles);

        var result = game.Place(eight.Id, new Placement(Cell.Origin, 0));

        Assert.True(result.Succeeded);
        Assert.Equal(8, game.Players[0].Score);
        Assert.Equal(GamePhase.Playing, game.Phase);
        Assert.Equal(1, game.CurrentPlayerIndex);
    }

    [Fact]
    public void Start_must_use_an_allowed_tile_on_the_origin()
    {
        var five = T(5, 5, 5);
        var other = T(0, 1, 2);
        var game = Deal([five, other], [T(1, 2, 3)], T(0, 0, 1));

        Assert.Equal(MoveError.NotAStartTile, game.Place(other.Id, new Placement(Cell.Origin, 0)).Error);
        Assert.Equal(MoveError.StartNotAtOrigin, game.Place(five.Id, new Placement(new Cell(2, 2), 0)).Error);
        Assert.Equal(MoveError.WrongPhase, game.Draw().Error);
        Assert.Equal(3, game.ValidPlacements(five.Id).Count);
        Assert.Empty(game.ValidPlacements(other.Id));
    }

    [Fact]
    public void Open_cells_are_the_origin_at_the_start_and_then_the_cells_next_to_the_tiles()
    {
        var five = T(5, 5, 5);
        var game = Deal([five, T(0, 1, 2)], [T(1, 2, 3)], T(0, 0, 1));

        Assert.Equal([Cell.Origin], game.OpenCells);

        game.Place(five.Id, new Placement(Cell.Origin, 0));

        var neighbors = new[] { Cell.Origin.Neighbor(0), Cell.Origin.Neighbor(1), Cell.Origin.Neighbor(2) };
        Assert.Equal(neighbors.ToHashSet(), game.OpenCells.ToHashSet());
    }

    [Fact]
    public void Tile_of_another_player_is_rejected()
    {
        var five = T(5, 5, 5);
        var notMine = T(1, 2, 3);
        var game = Deal([five, T(0, 1, 2)], [notMine]);

        Assert.Equal(MoveError.TileNotInRack, game.Place(notMine.Id, new Placement(Cell.Origin, 0)).Error);
    }

    [Fact]
    public void Drawing_costs_five_per_tile_up_to_three_then_passing_costs_ten() // D-002, D-005
    {
        var game = Deal(
            [T(5, 5, 5), T(5, 5, 5)],
            [T(0, 1, 2)],
            T(0, 1, 3), T(1, 2, 3), T(0, 0, 1), T(0, 2, 4));
        game.Place(game.AllowedStartTiles[0].Id, new Placement(Cell.Origin, 0));

        Assert.False(game.CanPass); // tiles left to draw
        for (var i = 0; i < Scoring.MaxDrawsPerTurn; i++)
        {
            var draw = game.Draw();
            Assert.True(draw.Succeeded);
            Assert.IsType<TileDrawn>(Assert.Single(draw.Events));
        }

        Assert.Equal(MoveError.DrawLimitReached, game.Draw().Error);
        Assert.Equal(4, game.Players[1].Rack.Count);
        Assert.Equal(-15, game.Players[1].Score);
        Assert.True(game.CanPass);

        var pass = game.Pass();

        Assert.True(pass.Succeeded);
        Assert.Equal(-25, game.Players[1].Score);
        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.Equal(0, game.DrawsThisTurn);
        Assert.Equal(1, game.PoolCount);
    }

    [Fact]
    public void Passing_is_allowed_when_no_tile_can_be_drawn_even_if_a_move_exists()
    {
        // The player may have overlooked the move, so the game does not point it out by refusing to pass.
        var game = Deal([T(5, 5, 5), T(5, 5, 5)], [T(5, 5, 1), T(0, 0, 1)]);
        game.Place(game.AllowedStartTiles[0].Id, new Placement(Cell.Origin, 0));

        Assert.False(game.CanDraw); // empty pool
        Assert.True(game.Players[1].Rack.Any(t => game.ValidPlacements(t.Id).Count > 0));
        Assert.True(game.CanPass);

        var pass = game.Pass();

        Assert.True(pass.Succeeded);
        Assert.Equal(-10, game.Players[1].Score);
        Assert.Equal(0, game.CurrentPlayerIndex);
    }

    [Fact]
    public void Passing_is_refused_while_tiles_can_still_be_drawn()
    {
        var game = Deal([T(5, 5, 5), T(5, 5, 5)], [T(1, 2, 3)], T(0, 1, 3), T(1, 2, 3), T(0, 0, 1));
        game.Place(game.AllowedStartTiles[0].Id, new Placement(Cell.Origin, 0));
        game.Draw();

        Assert.True(game.CanDraw);
        Assert.False(game.CanPass);
        Assert.Equal(MoveError.PassNotAllowed, game.Pass().Error);
    }

    [Fact]
    public void Going_out_scores_twenty_five_plus_the_other_racks() // D-004
    {
        var first = T(5, 5, 5);
        var last = T(5, 5, 5);
        var game = Deal([T(1, 2, 3), T(0, 1, 2)], [first, last]);
        game.Place(first.Id, new Placement(Cell.Origin, 0));   // Bob: 15 + 10
        game.Pass();                                            // Ann: -10, empty pool

        var result = game.Place(last.Id, new Placement(new Cell(0, 1), 0));

        Assert.True(result.Succeeded);
        Assert.Equal(GamePhase.Finished, game.Phase);
        Assert.Equal(new GameOutcome(GameEndReason.WentOut, 1, 25 + 6 + 3), game.Outcome);
        Assert.Equal(25 + 15 + 34, game.Players[1].Score);
        Assert.Equal(-10, game.Players[0].Score);
        Assert.IsType<GameFinished>(result.Events[^1]);
        Assert.Equal(MoveError.WrongPhase, game.Pass().Error);
    }

    [Fact]
    public void Everyone_passing_with_an_empty_pool_is_a_draw() // D-003
    {
        var game = Deal([T(5, 5, 5), T(0, 0, 1)], [T(1, 2, 3)]);
        game.Place(game.AllowedStartTiles[0].Id, new Placement(Cell.Origin, 0));

        game.Pass(); // Bob
        var result = game.Pass(); // Ann

        Assert.True(result.Succeeded);
        Assert.Equal(new GameOutcome(GameEndReason.Blocked, null, 0), game.Outcome);
        Assert.Equal(15, game.Players[0].Score);
        Assert.Equal(-10, game.Players[1].Score);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 50)]
    [InlineData(2, 60)]
    [InlineData(3, 70)]
    public void Hexagon_bonus_grows_with_hexagons_completed(int hexagons, int bonus)
    {
        Assert.Equal(bonus, Scoring.HexagonBonus(hexagons));
    }

    // --- Hints ---------------------------------------------------------------------------------

    [Fact]
    public void A_hint_costs_points_and_keeps_the_turn_and_the_tiles()
    {
        var game = Deal([T(4, 4, 4), T(0, 1, 2)], [T(3, 3, 3), T(5, 5, 5)]);
        var player = game.CurrentPlayerIndex;

        var result = game.Hint();

        Assert.True(result.Succeeded);
        Assert.Equal(new HintTaken(player, Scoring.DefaultHintPenalty), Assert.Single(result.Events));
        Assert.Equal(-Scoring.DefaultHintPenalty, game.CurrentPlayer.Score);
        Assert.Equal(Scoring.DefaultHintsPerPlayer - 1, game.CurrentPlayer.HintsLeft);
        Assert.Equal(player, game.CurrentPlayerIndex);
        Assert.Equal(GamePhase.Starting, game.Phase);
        Assert.Equal(2, game.CurrentPlayer.Rack.Count);
    }

    [Fact]
    public void A_player_may_take_three_hints_and_not_a_fourth()
    {
        var game = Deal([T(4, 4, 4), T(0, 1, 2)], [T(3, 3, 3), T(5, 5, 5)]);

        for (var i = 0; i < Scoring.DefaultHintsPerPlayer; i++)
        {
            Assert.True(game.Hint().Succeeded);
        }

        var refused = game.Hint();

        Assert.Equal(MoveError.HintLimitReached, refused.Error);
        Assert.False(game.CanHint);
        Assert.Equal(-Scoring.DefaultHintPenalty * Scoring.DefaultHintsPerPlayer, game.CurrentPlayer.Score);
    }

    [Fact]
    public void The_hint_limit_is_per_player()
    {
        var five = T(5, 5, 5);
        var game = Deal([T(4, 4, 4), T(0, 1, 2)], [T(3, 3, 3), five]);
        for (var i = 0; i < Scoring.DefaultHintsPerPlayer; i++)
        {
            game.Hint();
        }

        Assert.True(game.Place(five.Id, new Placement(Cell.Origin, 0)).Succeeded);

        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.True(game.CanHint);
        Assert.Equal(Scoring.DefaultHintsPerPlayer, game.CurrentPlayer.HintsLeft);
    }

    [Fact]
    public void No_hint_once_the_round_is_over()
    {
        var game = Deal([T(5, 5, 5)], [T(1, 1, 1)]);
        game.Place(game.CurrentPlayer.Rack[0].Id, new Placement(Cell.Origin, 0));
        Assert.Equal(GamePhase.Finished, game.Phase);

        Assert.False(game.CanHint);
        Assert.Equal(MoveError.WrongPhase, game.Hint().Error);
    }

    [Fact]
    public void Hint_rules_set_the_penalty_and_the_number_of_hints()
    {
        var game = TriominosGame.FromDeal(TwoPlayers, [[T(4, 4, 4), T(0, 1, 2)], [T(3, 3, 3), T(5, 5, 5)]], [], new HintRules(2, 5));

        Assert.Equal(new HintRules(2, 5), game.Hints);
        Assert.Equal(new HintTaken(game.CurrentPlayerIndex, 5), Assert.Single(game.Hint().Events));
        Assert.True(game.Hint().Succeeded);
        Assert.Equal(MoveError.HintLimitReached, game.Hint().Error);
        Assert.Equal(-10, game.CurrentPlayer.Score);
    }

    [Fact]
    public void Zero_hints_turns_hints_off()
    {
        var game = TriominosGame.FromDeal(TwoPlayers, [[T(4, 4, 4), T(0, 1, 2)], [T(3, 3, 3), T(5, 5, 5)]], [], new HintRules(0, 10));

        Assert.False(game.Hints.Enabled);
        Assert.False(game.CanHint);
        Assert.Equal(MoveError.HintLimitReached, game.Hint().Error);
        Assert.Equal(0, game.CurrentPlayer.Score);
    }

    [Fact]
    public void A_free_hint_costs_no_points_but_still_counts()
    {
        var game = TriominosGame.FromDeal(TwoPlayers, [[T(4, 4, 4), T(0, 1, 2)], [T(3, 3, 3), T(5, 5, 5)]], [], new HintRules(1, 0));

        Assert.True(game.Hint().Succeeded);

        Assert.Equal(0, game.CurrentPlayer.Score);
        Assert.False(game.CanHint);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(3, -1)]
    public void Negative_hint_rules_are_refused(int count, int penalty)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TriominosGame.Start(TwoPlayers, new Random(1), new HintRules(count, penalty)));
    }

    [Fact]
    public void A_game_without_hint_rules_uses_the_defaults()
    {
        Assert.Equal(HintRules.Default, TriominosGame.Start(TwoPlayers, new Random(1)).Hints);
        Assert.Equal(new HintRules(Scoring.DefaultHintsPerPlayer, Scoring.DefaultHintPenalty), HintRules.Default);
    }
}
