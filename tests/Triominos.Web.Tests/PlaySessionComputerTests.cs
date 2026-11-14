using Triominos.Core.Game;
using Triominos.Web.Interop;
using Triominos.Web.Play;

namespace Triominos.Web.Tests;

/// <summary>The turn flow of a game with computer players, without the page or the scene.</summary>
public class PlaySessionComputerTests
{
    private static PlayerSetup Human(string name, int color) => new(name, PlayerColors.All[color]);

    private static PlayerSetup Computer(string name, int color) => new(name, PlayerColors.All[color], IsComputer: true);

    private static PlaySession Start(int seed, params PlayerSetup[] players) => new(players, new Random(seed));

    /// <summary>The first seed for which the given seat starts.</summary>
    private static int SeedWhereSeatStarts(int seat, params PlayerSetup[] players)
    {
        for (var seed = 0; seed < 500; seed++)
        {
            if (Start(seed, players).Game.CurrentPlayerIndex == seat)
            {
                return seed;
            }
        }

        throw new InvalidOperationException($"No seed lets seat {seat} start.");
    }

    /// <summary>A human move by the simplest rule: lay a tile if one fits, else draw, else pass.</summary>
    private static MoveResult HumanMove(PlaySession session)
    {
        var game = session.Game;
        foreach (var tile in game.CurrentPlayer.Rack)
        {
            var placements = session.Pick(tile.Id);
            if (placements.Count > 0)
            {
                return session.Place(tile.Id, placements[0]);
            }
        }

        return session.CanDraw ? session.Draw() : session.Pass();
    }

    /// <summary>A whole human turn: moves until the turn passes on or the round ends.</summary>
    private static void HumanTurn(PlaySession session)
    {
        var player = session.Game.CurrentPlayerIndex;
        for (var step = 0; step < 10 && session.Game.CurrentPlayerIndex == player && session.Screen != SessionScreen.Finished; step++)
        {
            Assert.True(HumanMove(session).Succeeded);
        }
    }

    [Fact]
    public void With_one_human_there_is_no_handover_at_the_start_whoever_begins()
    {
        PlayerSetup[] players = [Human("Ann", 0), Computer("Computer", 1)];

        for (var seed = 0; seed < 20; seed++)
        {
            Assert.Equal(SessionScreen.Playing, Start(seed, players).Screen);
        }
    }

    [Fact]
    public void With_two_humans_the_game_still_starts_with_a_handover()
    {
        var session = Start(1, Human("Ann", 0), Human("Bob", 1));

        Assert.Equal(SessionScreen.Handover, session.Screen);
        Assert.False(session.IsComputerTurn);
    }

    [Fact]
    public void A_human_who_starts_sees_the_rack_and_the_marked_tiles_and_is_not_a_computer_turn()
    {
        PlayerSetup[] players = [Human("Ann", 0), Computer("Computer", 1)];
        var session = Start(SeedWhereSeatStarts(0, players), players);

        Assert.False(session.IsComputerTurn);
        Assert.Equal(session.Game.Players[0].Rack, session.VisibleRack);
        Assert.NotEmpty(session.MarkedTiles);
        Assert.Contains("begint met een van de gemarkeerde stenen", session.Message);
    }

    [Fact]
    public void During_a_computer_turn_the_only_human_keeps_the_rack_in_view_and_cannot_move()
    {
        PlayerSetup[] players = [Human("Ann", 0), Computer("Computer", 1)];
        var session = Start(SeedWhereSeatStarts(1, players), players);

        Assert.True(session.IsComputerTurn);
        Assert.Equal(session.Game.Players[0].Rack, session.VisibleRack);
        Assert.Empty(session.MarkedTiles);
        Assert.False(session.CanDraw);
        Assert.False(session.CanPass);

        var tile = session.Game.Players[1].Rack[0];
        Assert.Empty(session.Pick(tile.Id));
        Assert.Equal("De computer is aan de beurt.", session.Message);
        Assert.Equal(MoveError.WrongPhase, session.Place(tile.Id, new(new(0, 0), 0)).Error);
        Assert.Equal(MoveError.WrongPhase, session.Draw().Error);
        Assert.Equal(MoveError.WrongPhase, session.Pass().Error);
        Assert.True(session.Game.Board.IsEmpty);
    }

    [Fact]
    public void A_computer_step_is_one_move_and_hands_the_turn_to_the_human_without_a_handover()
    {
        PlayerSetup[] players = [Human("Ann", 0), Computer("Computer", 1)];
        var session = Start(SeedWhereSeatStarts(1, players), players);

        var result = session.PlayComputerStep();

        Assert.True(result.Succeeded);
        Assert.Single(result.Events.OfType<TilePlaced>());
        Assert.Single(session.Game.Board.Tiles);
        Assert.Equal(0, session.Game.CurrentPlayerIndex);
        Assert.Equal(SessionScreen.Playing, session.Screen);
        Assert.False(session.IsComputerTurn);
        Assert.StartsWith("Computer begint met", session.Message);
    }

    [Fact]
    public void A_computer_step_is_refused_on_a_human_turn()
    {
        PlayerSetup[] players = [Human("Ann", 0), Computer("Computer", 1)];
        var session = Start(SeedWhereSeatStarts(0, players), players);

        var result = session.PlayComputerStep();

        Assert.Equal(MoveError.WrongPhase, result.Error);
        Assert.True(session.Game.Board.IsEmpty);
    }

    [Fact]
    public void With_several_humans_the_rack_is_hidden_while_a_computer_plays_and_the_next_human_gets_a_handover()
    {
        PlayerSetup[] players = [Human("Ann", 0), Computer("Computer", 1), Human("Bob", 2)];
        var session = Start(SeedWhereSeatStarts(1, players), players);

        Assert.True(session.IsComputerTurn);
        Assert.Empty(session.VisibleRack);

        session.PlayComputerStep();

        Assert.Equal(2, session.Game.CurrentPlayerIndex);
        Assert.Equal(SessionScreen.Handover, session.Screen);
        Assert.Empty(session.VisibleRack);
    }

    [Fact]
    public void Two_humans_and_a_computer_hand_over_to_a_human_only_after_a_computer_turn_when_a_human_follows()
    {
        PlayerSetup[] players = [Human("Ann", 0), Human("Bob", 1), Computer("Computer", 2)];
        var session = Start(SeedWhereSeatStarts(0, players), players);
        session.Continue();

        HumanTurn(session); // Ann starts; Bob follows, so there is a handover.
        Assert.Equal(SessionScreen.Handover, session.Screen);
        session.Continue();

        HumanTurn(session); // Bob moves; the computer follows, with no handover.
        Assert.True(session.IsComputerTurn);
        Assert.Equal(SessionScreen.Playing, session.Screen);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_human_and_computers_play_a_whole_game_to_the_end(int computers)
    {
        PlayerSetup[] players =
        [
            Human("Ann", 0),
            .. Enumerable.Range(1, computers).Select(i => Computer($"Computer {i}", i)),
        ];

        for (var seed = 0; seed < 25; seed++)
        {
            var session = Start(seed, players);
            for (var step = 0; step < 3000 && session.Screen != SessionScreen.Finished; step++)
            {
                Assert.NotEqual(SessionScreen.Handover, session.Screen); // one human: never a handover
                var result = session.IsComputerTurn ? session.PlayComputerStep() : HumanMove(session);
                Assert.True(result.Succeeded, $"Seed {seed}, step {step}: {result.Error}");
            }

            Assert.Equal(SessionScreen.Finished, session.Screen);
        }
    }

    [Fact]
    public void A_computer_draw_is_described_without_naming_the_tile()
    {
        PlayerSetup[] players = [Human("Ann", 0), Computer("Computer", 1)];

        // Over a number of games the computer is bound to have to draw at least once.
        for (var seed = 0; seed < 50; seed++)
        {
            var session = Start(seed, players);
            for (var step = 0; step < 3000 && session.Screen != SessionScreen.Finished; step++)
            {
                if (!session.IsComputerTurn)
                {
                    HumanMove(session);
                    continue;
                }

                var before = session.Game.Players[1].Rack.Count;
                var result = session.PlayComputerStep();
                if (result.Events.OfType<TileDrawn>().Any())
                {
                    Assert.Equal("Computer trekt een steen (-5).", session.Message);
                    Assert.Equal(before + 1, session.Game.Players[1].Rack.Count);
                    return;
                }
            }
        }

        Assert.Fail("The computer never had to draw in 50 games.");
    }

    [Fact]
    public void The_scene_is_told_which_moves_are_the_computers()
    {
        PlayerSetup[] players = [Human("Ann", 0), Computer("Computer", 1)];
        var session = Start(SeedWhereSeatStarts(1, players), players);
        var result = session.PlayComputerStep();

        var withSetup = MoveViews.From(session.Game, result, session.VisibleRack, session.MarkedTiles, session.Setup);
        var without = MoveViews.From(session.Game, result, session.VisibleRack, session.MarkedTiles);

        Assert.True(Assert.IsType<PlacedEventView>(withSetup.Events[0]).Hidden);
        Assert.False(Assert.IsType<PlacedEventView>(without.Events[0]).Hidden);
    }
}
