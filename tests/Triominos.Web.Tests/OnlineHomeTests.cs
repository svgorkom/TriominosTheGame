using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Triominos.Core.Game;
using Triominos.Web.Interop;
using Triominos.Web.Online;
using Triominos.Web.Pages;
using Triominos.Web.Play;
using Triominos.Web.Storage;

namespace Triominos.Web.Tests;

/// <summary>
/// The table page in an online game: the lobby, the start, the actions of the other players and the
/// loss of the connection. The tests play the part of the scene like in <see cref="HomeTests"/>, and
/// of the server by sending the room's messages through <see cref="FakeSocket"/>.
/// </summary>
public sealed class OnlineHomeTests : BunitContext
{
    private static readonly string[] Names = ["Anna", "Ben"];

    private readonly MemoryStore _store = new();
    private readonly FakeTransport _transport = new();
    private readonly BunitJSModuleInterop _scene;

    public OnlineHomeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _scene = JSInterop.SetupModule("./js/scene/scene.js");
        _scene.Mode = JSRuntimeMode.Loose;
        ComponentFactories.AddStub<PageTitle>();
        Services.AddSingleton<IPreferenceStore>(_store);
        Services.AddSingleton<Preferences>();
        Services.AddSingleton<IRoomTransport>(_transport);
        Services.AddSingleton<OnlineClient>();
    }

    private static string Room(int seat, int players = 2) =>
        $$"""{"t":"room","code":"BCDFG","seat":{{seat}},"token":"tok-{{seat}}","players":[{{string.Join(',', Names.Take(players).Select((name, i) => $$"""{"seat":{{i}},"name":"{{name}}","connected":true}"""))}}],"started":false}""";

    private static string Started(int seed) =>
        $$"""{"t":"started","seed":{{seed}},"settings":{"hintsMax":3,"hintPenalty":10},"players":[{"seat":0,"name":"Anna","connected":true},{"seat":1,"name":"Ben","connected":true}]}""";

    /// <summary>The first seed for which the given seat (0 = Anna, 1 = Ben) starts.</summary>
    private static int SeedWhereSeatStarts(int seat)
    {
        for (var seed = 0; seed < 500; seed++)
        {
            if (TriominosGame.Start(Names, new Random(seed)).CurrentPlayerIndex == seat)
            {
                return seed;
            }
        }

        throw new InvalidOperationException($"No seed lets seat {seat} start.");
    }

    private static IElement Button(IRenderedComponent<Home> cut, string selector, string text) =>
        cut.FindAll(selector).Single(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal));

    private TableScene CurrentScene() =>
        ((DotNetObjectReference<TableScene>)_scene.Invocations["init"].Last().Arguments[2]!).Value;

    /// <summary>Opens the lobby, enters a name and makes a room, which the server answers.</summary>
    private IRenderedComponent<Home> HostARoom(int players = 2)
    {
        var cut = Render<Home>();
        Button(cut, ".menu button", "Online spelen").Click();
        cut.Find(".lobby input:not(.code-input)").Input("Anna");
        Button(cut, ".lobby button", "Kamer maken").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, _transport.Sockets.Count));
        _transport.Last.Receive(Room(0, players));
        cut.WaitForAssertion(() => Assert.Equal("BCDFG", cut.Find(".room-code").TextContent));
        return cut;
    }

    /// <summary>The host starts the game and the deal is done at this screen.</summary>
    private async Task<IRenderedComponent<Home>> StartAsync(int seed)
    {
        var cut = HostARoom();
        Button(cut, ".lobby button", "Start het spel").Click();
        _transport.Last.Receive(Started(seed));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.Session));
        await cut.InvokeAsync(CurrentScene().HandleAnimationsFinished);
        return cut;
    }

    [Fact]
    public void The_menu_leads_to_a_lobby_where_a_room_can_be_made_or_joined()
    {
        var cut = Render<Home>();

        Button(cut, ".menu button", "Online spelen").Click();

        Assert.Equal(new[] { "Kamer maken", "Meedoen", "Terug" }, cut.FindAll(".lobby button").Select(b => b.TextContent.Trim()));
        Assert.Empty(cut.FindAll(".menu"));
    }

    [Fact]
    public void A_link_with_a_room_code_opens_the_lobby_with_the_code_filled_in()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("kamer", "bcdfg"));

        var cut = Render<Home>();

        Assert.Equal("BCDFG", cut.Find(".code-input").GetAttribute("value"));
    }

    [Fact]
    public void A_name_is_needed_before_anything_is_sent_to_the_server()
    {
        var cut = Render<Home>();
        Button(cut, ".menu button", "Online spelen").Click();

        Button(cut, ".lobby button", "Kamer maken").Click();

        Assert.Equal("Kies een naam van 1 tot 16 tekens.", cut.Find(".lobby .note").TextContent);
        Assert.Empty(_transport.Sockets);
    }

    [Fact]
    public void A_made_room_shows_its_code_a_link_and_the_players_and_the_host_can_start()
    {
        var cut = HostARoom();

        Assert.Equal(new[] { "Anna (jij)", "Ben" }, cut.FindAll(".lobby-player .player-name").Select(n => n.TextContent.Trim()));
        Assert.Contains("kamer=BCDFG", cut.Find(".share-link").GetAttribute("value")!);
        Assert.False(Button(cut, ".lobby button", "Start het spel").HasAttribute("disabled"));
    }

    [Fact]
    public void The_host_cannot_start_alone()
    {
        var alone = HostARoom(players: 1);

        Assert.True(Button(alone, ".lobby button", "Start het spel").HasAttribute("disabled"));
    }

    [Fact]
    public void A_guest_waits_for_the_host()
    {
        var guest = Render<Home>();
        Button(guest, ".menu button", "Online spelen").Click();
        guest.Find(".lobby input:not(.code-input)").Input("Ben");
        guest.Find(".code-input").Input("bcdfg");
        Button(guest, ".lobby button", "Meedoen").Click();
        guest.WaitForAssertion(() => Assert.Equal(1, _transport.Sockets.Count));
        _transport.Last.Receive(Room(1));

        guest.WaitForAssertion(() => Assert.Contains("Wachten tot Anna het spel start", guest.Find(".lobby").TextContent));
        Assert.Empty(guest.FindAll(".lobby button").Where(b => b.TextContent.Trim() == "Start het spel"));
    }

    [Fact]
    public async Task The_host_asks_the_room_to_start_and_every_screen_deals_the_same_game_from_the_seed()
    {
        var cut = HostARoom();

        Button(cut, ".lobby button", "Start het spel").Click();
        Assert.Equal("start", _transport.Last.Messages.Last().T);
        _transport.Last.Receive(Started(7));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Instance.Session));
        await cut.InvokeAsync(CurrentScene().HandleAnimationsFinished);

        var session = cut.Instance.Session!;
        var reference = TriominosGame.Start(Names, new Random(7));
        Assert.Equal(0, session.LocalSeat);
        Assert.Equal(reference.Players[0].Rack.Select(t => t.Id), session.Game.Players[0].Rack.Select(t => t.Id));
        Assert.Equal(reference.CurrentPlayerIndex, session.Game.CurrentPlayerIndex);
        Assert.Empty(cut.FindAll(".lobby"));
        Assert.Empty(cut.FindAll(".overlay"));
        _scene.VerifyInvoke("deal");
    }

    [Fact]
    public async Task The_move_of_another_player_is_played_in_the_scene_and_the_turn_comes_to_this_screen()
    {
        var seed = SeedWhereSeatStarts(1);
        var cut = await StartAsync(seed);
        var session = cut.Instance.Session!;
        Assert.True(session.IsRemoteTurn);
        var start = TriominosGame.Start(Names, new Random(seed)).AllowedStartTiles[0];

        _transport.Last.Receive($$$"""{"t":"move","n":0,"seat":1,"move":{"k":"place","tile":{{{start.Id}}},"row":0,"col":0,"rot":0}}""");

        cut.WaitForAssertion(() => Assert.Equal(1, session.Game.Board.Tiles.Count));
        _scene.VerifyInvoke("playMove");
        await cut.InvokeAsync(CurrentScene().HandleAnimationsFinished);
        Assert.False(session.IsRemoteTurn);
        Assert.Equal(0, session.Game.CurrentPlayerIndex);
        Assert.DoesNotContain(_transport.Last.Messages, m => m.T == "move");
    }

    [Fact]
    public async Task A_move_made_here_is_sent_to_the_room()
    {
        var seed = SeedWhereSeatStarts(0);
        var cut = await StartAsync(seed);
        var session = cut.Instance.Session!;
        var start = session.Game.AllowedStartTiles[0];

        await cut.InvokeAsync(() => CurrentScene().HandleTileDroppedAsync(start.Id, 0, 0, 0));

        var sent = _transport.Last.Messages.Last();
        Assert.Equal("move", sent.T);
        Assert.Equal(0, sent.N);
        Assert.Equal("place", sent.Move!.K);
        Assert.Equal(start.Id, sent.Move.Tile);
        Assert.Equal(1, session.Game.Board.Tiles.Count);
    }

    [Fact]
    public async Task While_the_connection_is_down_nothing_can_be_played_and_the_player_is_told()
    {
        var seed = SeedWhereSeatStarts(0);
        var cut = await StartAsync(seed);
        var session = cut.Instance.Session!;
        var start = session.Game.AllowedStartTiles[0];
        var socket = _transport.Last;

        socket.Drop();
        cut.WaitForAssertion(() => Assert.Contains("Verbinding verbroken", cut.Find(".message").TextContent));
        await cut.InvokeAsync(() => CurrentScene().HandleTileDroppedAsync(start.Id, 0, 0, 0));

        Assert.Equal(0, session.Game.Board.Tiles.Count);
        Assert.DoesNotContain(socket.Messages, m => m.T == "move");
    }
}
