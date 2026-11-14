using Triominos.Core.Ai;
using Triominos.Core.Board;
using Triominos.Core.Game;

namespace Triominos.Tests;

public class GameActionTests
{
    private static readonly string[] Names = ["Ann", "Bob", "Cas"];

    private static GameAction ToAction(ComputerMove move) => move switch
    {
        PlaceMove place => new PlaceAction(place.TileId, place.Placement),
        DrawMove => new DrawAction(),
        PassMove => new PassAction(),
        _ => throw new InvalidOperationException($"Unknown move {move}."),
    };

    /// <summary>Everything a player can see or be told about a game, as one string.</summary>
    private static string Describe(TriominosGame game) =>
        string.Join(
            '|',
            game.Phase,
            game.CurrentPlayerIndex,
            game.PoolCount,
            game.DrawsThisTurn,
            string.Join(',', game.Players.Select(p => $"{p.Name}:{p.Score}:{p.HintsUsed}:[{string.Join(' ', p.Rack.Select(t => t.Id))}]")),
            string.Join(',', game.Board.Tiles.Select(t => $"{t.Tile.Id}@{t.Placement.Cell.Row},{t.Placement.Cell.Col}r{t.Placement.Rotation}")));

    [Fact]
    public void The_same_seed_and_the_same_actions_give_the_same_game_at_every_step()
    {
        // The computer plays one game; a second game, dealt from the same seed, only receives its actions.
        var played = TriominosGame.Start(Names, new Random(7));
        var replayed = TriominosGame.Start(Names, new Random(7));
        Assert.Equal(Describe(played), Describe(replayed));

        var random = new Random(1);
        for (var step = 0; step < 400 && played.Phase != GamePhase.Finished; step++)
        {
            var action = ToAction(ComputerPlayer.Decide(played, ComputerLevel.Normal, random));

            var first = action.ApplyTo(played);
            var second = action.ApplyTo(replayed);

            Assert.True(first.Succeeded, $"step {step}: {first.Error}");
            Assert.Equal(first.Error, second.Error);
            Assert.Equal(Describe(played), Describe(replayed));
        }

        Assert.Equal(GamePhase.Finished, played.Phase);
        Assert.Equal(played.Outcome, replayed.Outcome);
    }

    [Fact]
    public void A_different_seed_gives_a_different_deal()
    {
        var one = TriominosGame.Start(Names, new Random(7));
        var other = TriominosGame.Start(Names, new Random(8));

        Assert.NotEqual(Describe(one), Describe(other));
    }

    [Fact]
    public void A_hint_costs_points_and_changes_nothing_else()
    {
        var game = TriominosGame.Start(Names, new Random(7));
        var before = game.CurrentPlayer.Score;

        var result = new HintAction().ApplyTo(game);

        Assert.True(result.Succeeded);
        Assert.Equal(before - game.Hints.Penalty, game.CurrentPlayer.Score);
        Assert.Equal(1, game.CurrentPlayer.HintsUsed);
    }

    [Fact]
    public void An_action_the_rules_refuse_comes_back_as_an_error_and_changes_nothing()
    {
        var game = TriominosGame.Start(Names, new Random(7));
        var before = Describe(game);

        var result = new PlaceAction(-1, new Placement(Cell.Origin, 0)).ApplyTo(game);

        Assert.Equal(MoveError.TileNotInRack, result.Error);
        Assert.Equal(before, Describe(game));
    }
}
