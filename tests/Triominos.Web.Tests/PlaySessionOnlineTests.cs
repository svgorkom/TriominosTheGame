using Triominos.Core.Ai;
using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Web.Play;

namespace Triominos.Web.Tests;

/// <summary>An online game: every player has a screen of their own and runs the whole game from the same seed.</summary>
public class PlaySessionOnlineTests
{
    private static readonly string[] Names = ["Anna", "Ben", "Cas"];

    private static IReadOnlyList<PlayerSetup> Humans(int count = 3) =>
        [.. Names.Take(count).Select((name, i) => new PlayerSetup(name, PlayerColors.All[i]))];

    private static PlaySession Screen(int seed, int seat, int count = 3, HintRules? hints = null) =>
        new(Humans(count), new Random(seed), hints, seat);

    /// <summary>Everything a player can see or be told about a game, as one string.</summary>
    private static string Describe(PlaySession session) =>
        string.Join(
            '|',
            session.Game.Phase,
            session.Game.CurrentPlayerIndex,
            session.Game.PoolCount,
            string.Join(',', session.Game.Players.Select(p => $"{p.Name}:{p.Score}:{p.HintsUsed}:[{string.Join(' ', p.Rack.Select(t => t.Id))}]")),
            string.Join(',', session.Game.Board.Tiles.Select(t => $"{t.Tile.Id}@{t.Placement.Cell.Row},{t.Placement.Cell.Col}r{t.Placement.Rotation}")));

    private static GameAction ToAction(ComputerMove move) => move switch
    {
        PlaceMove place => new PlaceAction(place.TileId, place.Placement),
        DrawMove => new DrawAction(),
        _ => new PassAction(),
    };

    /// <summary>The player to move acts at their own screen, as the UI does.</summary>
    private static MoveResult ActLocally(PlaySession session, GameAction action) => action switch
    {
        PlaceAction place => session.Place(place.TileId, place.Placement),
        DrawAction => session.Draw(),
        PassAction => session.Pass(),
        HintAction => session.RequestHint(),
        _ => throw new InvalidOperationException(),
    };

    [Fact]
    public void A_computer_cannot_have_the_seat_of_a_screen_and_the_seat_must_exist()
    {
        var withComputer = new[] { new PlayerSetup("Anna", PlayerColors.All[0]), new PlayerSetup("Computer", PlayerColors.All[1], IsComputer: true) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaySession(withComputer, new Random(1), null, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaySession(Humans(), new Random(1), null, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaySession(Humans(), new Random(1), null, -1));
    }

    [Fact]
    public void There_is_no_handover_and_every_screen_shows_its_own_rack()
    {
        for (var seat = 0; seat < 3; seat++)
        {
            var session = Screen(7, seat);

            Assert.Equal(SessionScreen.Playing, session.Screen);
            Assert.Equal(session.Game.Players[seat].Rack, session.VisibleRack);
            Assert.Equal(seat, session.LocalSeat);
        }
    }

    [Fact]
    public void Only_the_screen_of_the_player_to_move_can_act()
    {
        var screens = Enumerable.Range(0, 3).Select(seat => Screen(7, seat)).ToList();
        var mover = screens[0].Game.CurrentPlayerIndex;

        foreach (var (screen, seat) in screens.Select((s, i) => (s, i)))
        {
            Assert.Equal(seat != mover, screen.IsRemoteTurn);
            Assert.Equal(seat == mover, screen.MarkedTiles.Count > 0);
            Assert.Equal(seat == mover ? screen.Game.CanDraw : false, screen.CanDraw);
            Assert.False(seat != mover && screen.CanPass);
            Assert.False(seat != mover && screen.CanHint);
        }

        var other = screens[(mover + 1) % 3];
        var tile = other.Game.Players[(mover + 1) % 3].Rack[0];
        Assert.Empty(other.Pick(tile.Id));
        Assert.Equal($"{Names[mover]} is aan de beurt.", other.Message);
        Assert.False(other.Draw().Succeeded);
        Assert.False(other.Pass().Succeeded);
        Assert.False(other.RequestHint().Succeeded);
    }

    [Fact]
    public void An_action_counts_only_from_the_player_to_move_and_never_from_oneself()
    {
        var mover = Screen(7, 0).Game.CurrentPlayerIndex;
        var watcher = Screen(7, (mover + 1) % 3);
        var before = Describe(watcher);

        Assert.False(watcher.ApplyRemote((mover + 2) % 3, new DrawAction()).Succeeded);
        Assert.False(watcher.ApplyRemote(watcher.LocalSeat!.Value, new DrawAction()).Succeeded);
        Assert.Equal(before, Describe(watcher));

        // The first move of a game is a starting tile in the middle; drawing only starts after that.
        var start = Screen(7, mover).Game.AllowedStartTiles[0];
        Assert.True(watcher.ApplyRemote(mover, new PlaceAction(start.Id, new Placement(Cell.Origin, 0))).Succeeded);
        Assert.NotEqual(before, Describe(watcher));
    }

    [Fact]
    public void A_game_between_screens_stays_equal_move_after_move_and_a_late_joiner_replays_it()
    {
        var hints = new HintRules(2, 5);
        var screens = Enumerable.Range(0, 3).Select(seat => Screen(11, seat, hints: hints)).ToList();
        var log = new List<LoggedAction>();
        var random = new Random(3);

        for (var step = 0; step < 500 && screens[0].Screen != SessionScreen.Finished; step++)
        {
            var mover = screens[0].Game.CurrentPlayerIndex;
            var action = ToAction(ComputerPlayer.Decide(screens[mover].Game, ComputerLevel.Normal, random));

            // Now and then the player pays for a hint first.
            if (step % 7 == 3 && screens[mover].CanHint)
            {
                Assert.True(ActLocally(screens[mover], new HintAction()).Succeeded);
                Pass(screens, mover, new HintAction(), log);
            }

            var result = ActLocally(screens[mover], action);
            Assert.True(result.Succeeded, $"step {step}: {result.Error}");
            Pass(screens, mover, action, log);
        }

        Assert.Equal(SessionScreen.Finished, screens[0].Screen);
        Assert.Equal(screens[0].Game.Outcome, screens[2].Game.Outcome);

        // A player who joins afterwards, or comes back after a refresh, plays the log without animation and ends up in the same game.
        var late = Screen(11, 1, hints: hints);
        late.Replay(log);
        Assert.Equal(Describe(screens[1]), Describe(late));
        Assert.Equal(SessionScreen.Finished, late.Screen);
    }

    private static void Pass(List<PlaySession> screens, int mover, GameAction action, List<LoggedAction> log)
    {
        log.Add(new LoggedAction(mover, action));
        for (var seat = 0; seat < screens.Count; seat++)
        {
            if (seat != mover)
            {
                var received = screens[seat].ApplyRemote(mover, action);
                Assert.True(received.Succeeded, $"seat {seat} could not play {action} of {mover}: {received.Error}");
            }
        }

        var reference = Describe(screens[mover]);
        foreach (var screen in screens)
        {
            Assert.Equal(reference, Describe(screen));
        }
    }

    [Fact]
    public void Replaying_skips_what_the_rules_refused_like_everyone_else_did()
    {
        var session = Screen(7, 0);
        var mover = session.Game.CurrentPlayerIndex;
        var before = Describe(session);

        // Not this player's turn, and a tile that is in nobody's rack.
        session.Replay([new LoggedAction((mover + 1) % 3, new DrawAction()), new LoggedAction(mover, new PlaceAction(-1, new(new(0, 0), 0)))]);

        Assert.Equal(before, Describe(session));
    }

    [Fact]
    public void A_remote_hint_costs_that_player_points_and_says_so()
    {
        var mover = Screen(7, 0).Game.CurrentPlayerIndex;
        var watcher = Screen(7, (mover + 1) % 3);
        var score = watcher.Game.Players[mover].Score;

        var result = watcher.ApplyRemote(mover, new HintAction());

        Assert.True(result.Succeeded);
        Assert.Equal(score - watcher.Game.Hints.Penalty, watcher.Game.Players[mover].Score);
        Assert.Equal($"{Names[mover]} neemt een hint (-{watcher.Game.Hints.Penalty}).", watcher.Message);
    }
}
