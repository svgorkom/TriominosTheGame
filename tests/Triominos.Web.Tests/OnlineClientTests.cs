using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Web.Online;

namespace Triominos.Web.Tests;

/// <summary>The connection to a room: who is in it, which actions arrive and how a lost connection is mended.</summary>
public class OnlineClientTests
{
    private readonly MemoryStore _store = new();
    private readonly FakeTransport _transport = new();
    private readonly OnlineClient _client;

    public OnlineClientTests()
    {
        _client = new OnlineClient(_transport, _store)
        {
            ReconnectDelays = [TimeSpan.Zero],
            KeepAliveInterval = TimeSpan.FromHours(1),
        };
    }

    private static string Room(int seat, bool started = false, string extra = "") =>
        $$"""{"t":"room","code":"BCDFG","seat":{{seat}},"token":"tok-{{seat}}","players":[{"seat":0,"name":"Anna","connected":true},{"seat":1,"name":"Ben","connected":true}],"started":{{(started ? "true" : "false")}}{{extra}}}""";

    private async Task<FakeSocket> EnterAsRoomAsync(int seat = 0)
    {
        if (seat == 0)
        {
            await _client.CreateRoomAsync("Anna");
        }
        else
        {
            await _client.JoinRoomAsync("bcdfg", "Ben");
        }

        _transport.Last.Receive(Room(seat));
        return _transport.Last;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The condition was not met in time.");
    }

    [Fact]
    public async Task Making_a_room_joins_it_with_the_name_and_the_protocol_version()
    {
        await _client.CreateRoomAsync("  Anna ");

        Assert.Equal(OnlineStatus.Connecting, _client.Status);
        var join = Assert.Single(_transport.Last.Messages);
        Assert.Equal("join", join.T);
        Assert.Equal("Anna", join.Name);
        Assert.Equal(MoveCodec.ProtocolVersion, join.V);
        Assert.Null(join.Token);
        Assert.Equal(new[] { "BCDFG" }, _transport.Connected);
    }

    [Fact]
    public async Task The_room_answers_with_the_seat_the_players_and_a_token_that_is_kept()
    {
        await EnterAsRoomAsync();

        Assert.Equal(OnlineStatus.Online, _client.Status);
        Assert.Equal("BCDFG", _client.RoomCode);
        Assert.Equal(0, _client.LocalSeat);
        Assert.True(_client.IsHost);
        Assert.Equal(new[] { "Anna", "Ben" }, _client.Players.Select(p => p.Name));
        Assert.Equal("BCDFG|tok-0|Anna", _store.Values[OnlineClient.RoomKey]);
        Assert.False(_client.InGame);
    }

    [Fact]
    public async Task A_code_is_cleaned_up_and_a_blank_one_or_a_bad_name_never_reaches_the_server()
    {
        await _client.JoinRoomAsync("  ", "Ben");
        Assert.Equal("Vul de code van de kamer in.", _client.Error);

        await _client.JoinRoomAsync("bcdfg", " ");
        Assert.Equal("Kies een naam van 1 tot 16 tekens.", _client.Error);

        await _client.JoinRoomAsync("bcdfg", new string('x', 17));
        Assert.Equal("Kies een naam van 1 tot 16 tekens.", _client.Error);
        Assert.Empty(_transport.Connected);

        await _client.JoinRoomAsync(" bcdfg ", "Ben");
        Assert.Equal(new[] { "BCDFG" }, _transport.Connected);
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_gives_an_error_and_leaves_the_client_offline()
    {
        _transport.Unavailable = true;

        await _client.CreateRoomAsync("Anna");

        Assert.Equal(OnlineStatus.Offline, _client.Status);
        Assert.Equal("Kan de server niet bereiken. Probeer het later opnieuw.", _client.Error);
    }

    [Fact]
    public async Task A_room_that_refuses_the_player_gives_the_reason_in_Dutch_and_closes_the_connection()
    {
        await _client.JoinRoomAsync("BCDFG", "Anna");

        _transport.Last.Receive("""{"t":"error","error":"name_taken"}""");

        Assert.Equal(OnlineStatus.Offline, _client.Status);
        Assert.Equal("Die naam is al bezet in deze kamer.", _client.Error);
        Assert.Null(_client.RoomCode);
        await WaitForAsync(() => _transport.Last.Disposed);
    }

    [Fact]
    public async Task The_host_starts_a_game_with_the_hint_rules()
    {
        var socket = await EnterAsRoomAsync();

        await _client.StartGameAsync(new HintRules(2, 5));

        var start = socket.Messages.Last();
        Assert.Equal("start", start.T);
        Assert.Equal(2, start.Settings!.HintsMax);
        Assert.Equal(5, start.Settings.HintPenalty);
    }

    [Fact]
    public async Task A_started_game_is_announced_with_everything_needed_to_deal_it()
    {
        var socket = await EnterAsRoomAsync(seat: 1);
        OnlineGameStart? started = null;
        _client.GameStarted += start => started = start;

        socket.Receive("""{"t":"started","seed":42,"settings":{"hintsMax":1,"hintPenalty":7},"players":[{"seat":0,"name":"Anna","connected":true},{"seat":1,"name":"Ben","connected":true}]}""");

        Assert.NotNull(started);
        Assert.Equal(42, started.Seed);
        Assert.Equal(new HintRules(1, 7), started.Hints);
        Assert.Equal(new[] { "Anna", "Ben" }, started.Names);
        Assert.Equal(1, started.LocalSeat);
        Assert.Empty(started.History);
        Assert.True(_client.InGame);
    }

    [Fact]
    public async Task Actions_of_the_others_arrive_in_order_and_an_action_seen_twice_counts_once()
    {
        var socket = await EnterAsRoomAsync(seat: 1);
        socket.Receive("""{"t":"started","seed":1,"settings":{"hintsMax":3,"hintPenalty":10},"players":[]}""");
        var received = new List<(int Seat, GameAction Action)>();
        _client.ActionReceived += (seat, action) => received.Add((seat, action));

        socket.Receive("""{"t":"move","n":0,"seat":0,"move":{"k":"place","tile":5,"row":0,"col":0,"rot":2}}""");
        socket.Receive("""{"t":"move","n":0,"seat":0,"move":{"k":"place","tile":5,"row":0,"col":0,"rot":2}}""");
        socket.Receive("""{"t":"move","n":1,"seat":0,"move":{"k":"draw"}}""");

        Assert.Equal(2, received.Count);
        Assert.Equal((0, (GameAction)new PlaceAction(5, new Placement(new Cell(0, 0), 2))), received[0]);
        Assert.Equal((0, (GameAction)new DrawAction()), received[1]);
    }

    [Fact]
    public async Task Own_actions_are_sent_with_the_number_of_actions_seen_so_far()
    {
        var socket = await EnterAsRoomAsync();
        socket.Receive("""{"t":"started","seed":1,"settings":{"hintsMax":3,"hintPenalty":10},"players":[]}""");

        Assert.True(await _client.SendActionAsync(new PassAction()));
        socket.Receive("""{"t":"move","n":1,"seat":1,"move":{"k":"hint"}}""");
        Assert.True(await _client.SendActionAsync(new DrawAction()));

        var moves = socket.Messages.Where(m => m.T == "move").ToList();
        Assert.Equal(new[] { 0, 2 }, moves.Select(m => m.N!.Value));
        Assert.Equal(new[] { "pass", "draw" }, moves.Select(m => m.Move!.K));
    }

    [Fact]
    public async Task Nothing_is_sent_outside_a_game()
    {
        await EnterAsRoomAsync();

        Assert.False(await _client.SendActionAsync(new DrawAction()));
    }

    [Fact]
    public async Task An_action_that_went_missing_makes_the_client_fetch_the_whole_game_again()
    {
        var socket = await EnterAsRoomAsync(seat: 1);
        socket.Receive("""{"t":"started","seed":9,"settings":{"hintsMax":3,"hintPenalty":10},"players":[]}""");
        var received = new List<(int Seat, GameAction Action)>();
        _client.ActionReceived += (seat, action) => received.Add((seat, action));

        socket.Receive("""{"t":"move","n":1,"seat":0,"move":{"k":"pass"}}""");

        await WaitForAsync(() => _transport.Sockets.Count == 2);
        _transport.Last.Receive(Room(1, started: true, extra: ""","seed":9,"settings":{"hintsMax":3,"hintPenalty":10},"log":[{"seat":0,"move":{"k":"draw"}},{"seat":0,"move":{"k":"pass"}}]"""));

        Assert.Equal(OnlineStatus.Online, _client.Status);
        Assert.Equal(new GameAction[] { new DrawAction(), new PassAction() }, received.Select(r => r.Action));
        Assert.Equal("tok-1", _transport.Last.Messages.First().Token);
    }

    [Fact]
    public async Task A_player_who_joins_a_game_under_way_gets_it_with_its_history()
    {
        _store.Values[OnlineClient.RoomKey] = "BCDFG|tok-1|Ben";
        OnlineGameStart? started = null;
        _client.GameStarted += start => started = start;

        await _client.TryResumeAsync();
        _transport.Last.Receive(Room(1, started: true, extra: ""","seed":5,"settings":{"hintsMax":0,"hintPenalty":0},"log":[{"seat":0,"move":{"k":"place","tile":3,"row":0,"col":0,"rot":0}},{"seat":1,"move":{"k":"draw"}}]"""));

        var join = Assert.Single(_transport.Last.Messages);
        Assert.Equal("tok-1", join.Token);
        Assert.Equal("Ben", join.Name);
        Assert.NotNull(started);
        Assert.Equal(5, started.Seed);
        Assert.Equal(new HintRules(0, 0), started.Hints);
        Assert.Equal(
            new[] { new LoggedActionKey(0, "PlaceAction"), new LoggedActionKey(1, "DrawAction") },
            started.History.Select(h => new LoggedActionKey(h.Seat, h.Action.GetType().Name)));
    }

    private readonly record struct LoggedActionKey(int Seat, string Kind);

    [Fact]
    public async Task Without_a_kept_room_resuming_does_nothing()
    {
        await _client.TryResumeAsync();

        Assert.Equal(OnlineStatus.Offline, _client.Status);
        Assert.Empty(_transport.Connected);
    }

    [Fact]
    public async Task A_kept_room_that_is_gone_is_forgotten_without_an_error()
    {
        _store.Values[OnlineClient.RoomKey] = "BCDFG|tok-1|Ben";
        _transport.Unavailable = true;

        await _client.TryResumeAsync();

        Assert.Equal(OnlineStatus.Offline, _client.Status);
        Assert.Null(_client.Error);
        Assert.Equal(string.Empty, _store.Values[OnlineClient.RoomKey]);
    }

    [Fact]
    public async Task A_dropped_connection_is_mended_and_the_player_catches_up_with_what_was_missed()
    {
        var socket = await EnterAsRoomAsync(seat: 1);
        socket.Receive("""{"t":"started","seed":9,"settings":{"hintsMax":3,"hintPenalty":10},"players":[]}""");
        var received = new List<(int Seat, GameAction Action)>();
        var restarted = 0;
        _client.ActionReceived += (seat, action) => received.Add((seat, action));
        _client.GameStarted += _ => restarted++;
        socket.Receive("""{"t":"move","n":0,"seat":0,"move":{"k":"draw"}}""");

        socket.Drop();
        Assert.Equal(OnlineStatus.Reconnecting, _client.Status);
        await WaitForAsync(() => _transport.Sockets.Count == 2);
        _transport.Last.Receive(Room(1, started: true, extra: ""","seed":9,"settings":{"hintsMax":3,"hintPenalty":10},"log":[{"seat":0,"move":{"k":"draw"}},{"seat":0,"move":{"k":"pass"}}]"""));

        Assert.Equal(OnlineStatus.Online, _client.Status);
        Assert.Equal(0, restarted);
        Assert.Equal(new GameAction[] { new DrawAction(), new PassAction() }, received.Select(r => r.Action));
    }

    [Fact]
    public async Task A_connection_that_cannot_be_mended_ends_with_an_error()
    {
        var socket = await EnterAsRoomAsync();
        _transport.Unavailable = true;

        socket.Drop();

        await WaitForAsync(() => _client.Status == OnlineStatus.Offline);
        Assert.Equal("De verbinding met de kamer is verloren.", _client.Error);
    }

    [Fact]
    public async Task Leaving_before_the_start_gives_up_the_seat_and_forgets_the_room()
    {
        var socket = await EnterAsRoomAsync();

        await _client.LeaveAsync();

        Assert.Equal("leave", socket.Messages.Last().T);
        Assert.Equal(OnlineStatus.Offline, _client.Status);
        Assert.Null(_client.RoomCode);
        Assert.Equal(string.Empty, _store.Values[OnlineClient.RoomKey]);
    }

    [Fact]
    public async Task The_lobby_hears_about_every_change()
    {
        var changes = 0;
        _client.Changed += () => changes++;

        var socket = await EnterAsRoomAsync();
        var afterJoin = changes;
        socket.Receive("""{"t":"players","players":[{"seat":0,"name":"Anna","connected":true}]}""");

        Assert.True(afterJoin > 0);
        Assert.True(changes > afterJoin);
        Assert.Equal(new[] { "Anna" }, _client.Players.Select(p => p.Name));
    }
}
