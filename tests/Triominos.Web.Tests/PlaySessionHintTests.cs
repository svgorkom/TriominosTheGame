using Triominos.Core.Ai;
using Triominos.Core.Game;
using Triominos.Web.Play;

namespace Triominos.Web.Tests;

/// <summary>Asking for a hint in a game on one screen, without the page or the scene.</summary>
public class PlaySessionHintTests
{
    private static readonly PlayerSetup[] TwoHumans =
    [
        new("Ann", PlayerColors.All[0]),
        new("Bob", PlayerColors.All[1]),
    ];

    private static PlaySession Start(params PlayerSetup[] players) => new(players, new Random(7));

    private static PlaySession Playing()
    {
        var session = Start(TwoHumans);
        session.Continue();
        return session;
    }

    [Fact]
    public void A_hint_costs_points_singles_out_the_tile_and_points_at_the_cell()
    {
        var session = Playing();
        var player = session.Game.CurrentPlayer;
        var expected = Assert.IsType<PlaceMove>(ComputerPlayer.Decide(session.Game));

        var result = session.RequestHint();

        Assert.True(result.Succeeded);
        Assert.Equal(-Scoring.DefaultHintPenalty, player.Score);
        Assert.Equal(expected, session.Hint);
        Assert.Equal(expected.Placement.Cell, session.HintCell);
        Assert.Contains(session.MarkedTiles, t => t.Id == expected.TileId);
        Assert.StartsWith("Hint", session.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_hint_in_the_same_turn_is_refused_and_costs_nothing()
    {
        var session = Playing();
        session.RequestHint();
        var score = session.Game.CurrentPlayer.Score;

        Assert.False(session.CanHint);
        Assert.False(session.RequestHint().Succeeded);
        Assert.Equal(score, session.Game.CurrentPlayer.Score);
    }

    [Fact]
    public void A_hint_is_gone_after_the_move()
    {
        var session = Playing();
        session.RequestHint();
        var move = Assert.IsType<PlaceMove>(session.Hint);

        Assert.True(session.Place(move.TileId, move.Placement).Succeeded);

        Assert.Null(session.Hint);
        Assert.Null(session.HintCell);
    }

    [Fact]
    public void The_next_player_starts_without_a_hint_and_with_hints_of_their_own()
    {
        var session = Playing();
        session.RequestHint();
        var move = Assert.IsType<PlaceMove>(session.Hint);
        session.Place(move.TileId, move.Placement);
        session.Continue();

        Assert.True(session.CanHint);
        Assert.Equal(Scoring.DefaultHintsPerPlayer, session.HintsLeft);
        Assert.Empty(session.MarkedTiles);
    }

    [Fact]
    public void A_hint_in_the_middle_of_the_game_is_what_the_normal_computer_would_do()
    {
        var session = Playing();
        var first = Assert.IsType<PlaceMove>(ComputerPlayer.Decide(session.Game));
        session.Place(first.TileId, first.Placement);
        session.Continue();
        var expected = ComputerPlayer.Decide(session.Game);

        var result = session.RequestHint();

        Assert.True(result.Succeeded);
        Assert.Equal(expected, session.Hint);
        Assert.Equal(-Scoring.DefaultHintPenalty, session.Game.CurrentPlayer.Score);
    }

    [Fact]
    public void A_computer_gets_no_hint()
    {
        PlayerSetup[] players = [new("Ann", PlayerColors.All[0]), new("Computer", PlayerColors.All[1], IsComputer: true)];
        var seed = Enumerable.Range(0, 500).First(s => new PlaySession(players, new Random(s)).IsComputerTurn);
        var session = new PlaySession(players, new Random(seed));

        Assert.False(session.CanHint);
        Assert.False(session.RequestHint().Succeeded);
        Assert.Equal(0, session.Game.CurrentPlayer.Score);
    }

    [Fact]
    public void The_session_uses_the_hint_rules_it_is_given()
    {
        var session = new PlaySession(TwoHumans, new Random(7), new HintRules(1, 5));
        session.Continue();

        session.RequestHint();

        Assert.Equal(-5, session.Game.CurrentPlayer.Score);
        Assert.Equal(0, session.HintsLeft);
        Assert.Contains("(-5, nog 0 over)", session.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_hints_off_no_hint_can_be_asked()
    {
        var session = new PlaySession(TwoHumans, new Random(7), new HintRules(0, 10));
        session.Continue();

        Assert.False(session.CanHint);
        Assert.False(session.RequestHint().Succeeded);
        Assert.Equal(0, session.Game.CurrentPlayer.Score);
    }
}
