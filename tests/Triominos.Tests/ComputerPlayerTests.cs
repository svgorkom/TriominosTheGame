using Triominos.Core.Ai;
using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Core.Tiles;
using static Triominos.Tests.TestTiles;

namespace Triominos.Tests;

public class ComputerPlayerTests
{
    private static readonly string[] TwoPlayers = ["Ann", "Bob"];

    private static TriominosGame Deal(Tile[] first, Tile[] second, params Tile[] pool) =>
        TriominosGame.FromDeal(TwoPlayers, [first, second], pool);

    /// <summary>Plays the computer's move with the normal commands.</summary>
    private static MoveResult Apply(TriominosGame game, ComputerMove move) => move switch
    {
        PlaceMove place => game.Place(place.TileId, place.Placement),
        DrawMove => game.Draw(),
        PassMove => game.Pass(),
        _ => throw new InvalidOperationException($"Unknown move {move}."),
    };

    // --- ScoreOf ------------------------------------------------------------------------------

    [Fact]
    public void ScoreOf_a_starting_triple_includes_the_start_bonus_and_changes_nothing()
    {
        var triple = T(2, 2, 2);
        var game = Deal([triple, T(0, 1, 2)], [T(0, 0, 1), T(0, 1, 1)]);

        var score = game.ScoreOf(triple.Id, new Placement(Cell.Origin, 0));

        Assert.Equal(new ScoreBreakdown(6, Scoring.StartTripleBonus, 0, 0), score);
        Assert.True(game.Board.IsEmpty);
        Assert.Equal(GamePhase.Starting, game.Phase);
    }

    [Fact]
    public void ScoreOf_is_null_for_a_move_the_game_would_refuse()
    {
        var triple = T(2, 2, 2);
        var other = T(0, 1, 2);
        var game = Deal([triple, other], [T(0, 0, 1), T(0, 1, 1)]);

        Assert.Null(game.ScoreOf(other.Id, new Placement(Cell.Origin, 0))); // not a starting tile
        Assert.Null(game.ScoreOf(triple.Id, new Placement(new Cell(3, 3), 0))); // not at the origin
        Assert.Null(game.ScoreOf(-5, new Placement(Cell.Origin, 0))); // not in the rack
    }

    [Fact]
    public void ScoreOf_equals_the_score_that_Place_gives()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var game = TriominosGame.Start(TwoPlayers, new Random(seed));
            for (var move = 0; move < 12 && game.Phase != GamePhase.Finished; move++)
            {
                var decision = ComputerPlayer.Decide(game);
                var expected = decision is PlaceMove place ? game.ScoreOf(place.TileId, place.Placement) : null;

                var result = Apply(game, decision);

                if (expected is not null)
                {
                    Assert.Equal(expected, result.Events.OfType<TilePlaced>().Single().Score);
                }
            }
        }
    }

    // --- Decide -------------------------------------------------------------------------------

    [Fact]
    public void Starts_with_the_allowed_tile_that_scores_most()
    {
        var tripleZero = T(0, 0, 0);
        var tripleFive = T(5, 5, 5);
        var game = Deal([tripleZero, tripleFive, T(0, 1, 2)], [T(0, 1, 1), T(1, 1, 2), T(0, 0, 1)]);

        var move = Assert.IsType<PlaceMove>(ComputerPlayer.Decide(game));

        // Both triples may start: 0-0-0 scores 0 + 10 + 30 = 40, 5-5-5 scores 15 + 10 = 25.
        Assert.Equal(2, game.AllowedStartTiles.Count);
        Assert.Equal(tripleZero.Id, move.TileId);
        Assert.Equal(Cell.Origin, move.Placement.Cell);
    }

    [Fact]
    public void Plays_the_tile_with_the_highest_total_score()
    {
        var start = T(1, 1, 1);
        var lowTile = T(1, 1, 0);
        var highTile = T(1, 1, 5);
        var game = Deal([start, T(2, 2, 3)], [lowTile, highTile]);
        game.Place(start.Id, new Placement(Cell.Origin, 0));

        var move = Assert.IsType<PlaceMove>(ComputerPlayer.Decide(game));

        // Both of Bob's tiles fit next to the 1-1-1; the one worth 7 beats the one worth 2.
        Assert.Equal(highTile.Id, move.TileId);
        Assert.Equal(7, game.ScoreOf(move.TileId, move.Placement)!.Value.Total);
    }

    [Fact]
    public void Is_deterministic_and_breaks_ties_by_the_lowest_tile_id()
    {
        var start = T(1, 1, 1);
        var first = T(1, 1, 1);
        var second = T(1, 1, 1);
        var game = Deal([start, T(0, 1, 2)], [first, second]);
        game.Place(start.Id, new Placement(Cell.Origin, 0));

        var once = ComputerPlayer.Decide(game);
        var again = ComputerPlayer.Decide(game);

        Assert.Equal(once, again);
        Assert.Equal(first.Id, Assert.IsType<PlaceMove>(once).TileId);
    }

    [Fact]
    public void Draws_when_no_tile_fits_and_does_not_draw_while_a_tile_fits()
    {
        var start = T(1, 1, 1);
        var game = Deal([start, T(2, 2, 3)], [T(4, 4, 5)], T(1, 1, 5), T(2, 3, 3));
        game.Place(start.Id, new Placement(Cell.Origin, 0));

        Assert.IsType<DrawMove>(ComputerPlayer.Decide(game));

        Apply(game, new DrawMove()); // draws 1-1-5, which fits
        Assert.IsType<PlaceMove>(ComputerPlayer.Decide(game));
    }

    [Fact]
    public void Passes_after_three_draws_without_a_move()
    {
        var start = T(1, 1, 1);
        var game = Deal([start, T(2, 2, 3)], [T(4, 4, 5)], T(5, 5, 5), T(5, 5, 5), T(5, 5, 5), T(0, 0, 0));
        game.Place(start.Id, new Placement(Cell.Origin, 0));

        for (var draw = 0; draw < Scoring.MaxDrawsPerTurn; draw++)
        {
            Assert.IsType<DrawMove>(ComputerPlayer.Decide(game));
            Apply(game, new DrawMove());
        }

        Assert.IsType<PassMove>(ComputerPlayer.Decide(game));
    }

    [Fact]
    public void Passes_at_once_when_the_pool_is_empty_and_nothing_fits()
    {
        var start = T(1, 1, 1);
        var game = Deal([start, T(2, 2, 3)], [T(4, 4, 5)]);
        game.Place(start.Id, new Placement(Cell.Origin, 0));

        Assert.IsType<PassMove>(ComputerPlayer.Decide(game));
    }

    [Fact]
    public void The_move_does_not_depend_on_the_racks_of_other_players()
    {
        var start = T(1, 1, 1);
        var mine = T(1, 1, 5);
        var other = T(1, 1, 0);

        // Ann's second tile differs between the two games; Bob's rack and the board are the same.
        var gameA = Deal([start, T(2, 2, 3)], [mine, other]);
        var gameB = Deal([start, T(0, 0, 2)], [mine, other]);
        gameA.Place(start.Id, new Placement(Cell.Origin, 0));
        gameB.Place(start.Id, new Placement(Cell.Origin, 0));

        Assert.Equal(ComputerPlayer.Decide(gameA), ComputerPlayer.Decide(gameB));
    }

    [Fact]
    public void Decide_throws_when_the_round_is_over()
    {
        var game = Deal([T(0, 0, 0)], [T(1, 1, 1)]);
        game.Place(game.CurrentPlayer.Rack[0].Id, new Placement(Cell.Origin, 0));

        Assert.Equal(GamePhase.Finished, game.Phase);
        Assert.Throws<InvalidOperationException>(() => ComputerPlayer.Decide(game));
    }

    // --- Whole games --------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Computers_play_complete_games_with_only_best_scoring_placements(int players)
    {
        var names = Enumerable.Range(1, players).Select(i => $"P{i}").ToList();
        for (var seed = 0; seed < 150; seed++)
        {
            var game = TriominosGame.Start(names, new Random(seed));
            var moves = 0;
            while (game.Phase != GamePhase.Finished)
            {
                var decision = ComputerPlayer.Decide(game);
                if (decision is PlaceMove place)
                {
                    Assert.Equal(BestScore(game), game.ScoreOf(place.TileId, place.Placement)!.Value.Total);
                }
                else
                {
                    // A computer only draws or passes when no tile fits.
                    Assert.Equal(int.MinValue, BestScore(game));
                }

                var result = Apply(game, decision);
                Assert.True(result.Succeeded, $"Seed {seed}, move {moves}: {result.Error}");
                Assert.True(++moves < 1000, $"Seed {seed} did not finish.");
            }

            Assert.NotNull(game.Outcome);
        }
    }

    [Fact]
    public void The_computer_beats_a_random_mover_in_most_games()
    {
        const int Games = 200;
        var wins = 0;
        var decided = 0;
        for (var seed = 0; seed < Games; seed++)
        {
            var game = TriominosGame.Start(TwoPlayers, new Random(seed));
            var chooser = new Random(seed + 10_000);
            var computer = seed % 2; // alternate seats so the start does not decide the test
            while (game.Phase != GamePhase.Finished)
            {
                Apply(game, game.CurrentPlayerIndex == computer ? ComputerPlayer.Decide(game) : RandomMove(game, chooser));
            }

            var best = game.Players.Max(p => p.Score);
            if (game.Players.Count(p => p.Score == best) == 1)
            {
                decided++;
                wins += game.Players[computer].Score == best ? 1 : 0;
            }
        }

        Assert.True(decided >= Games * 9 / 10, $"Too many ties: {Games - decided}.");
        Assert.True(wins >= decided * 75 / 100, $"The computer won only {wins} of {decided} games.");
    }

    /// <summary>The best score of any valid move of the current player; int.MinValue when there is none.</summary>
    private static int BestScore(TriominosGame game) =>
        game.CurrentPlayer.Rack
            .SelectMany(t => game.ValidPlacements(t.Id).Select(p => game.ScoreOf(t.Id, p)!.Value.Total))
            .DefaultIfEmpty(int.MinValue)
            .Max();

    /// <summary>A random valid mover: plays a random valid placement, else draws, else passes.</summary>
    private static ComputerMove RandomMove(TriominosGame game, Random random)
    {
        var moves = game.CurrentPlayer.Rack
            .SelectMany(t => game.ValidPlacements(t.Id).Select(p => new PlaceMove(t.Id, p)))
            .ToList();
        if (moves.Count > 0)
        {
            return moves[random.Next(moves.Count)];
        }

        return game.CanDraw ? new DrawMove() : new PassMove();
    }
}
