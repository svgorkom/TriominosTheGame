using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Triominos.Core.Game;
using Triominos.Web.Interop;
using Triominos.Web.Pages;
using Triominos.Web.Play;
using Triominos.Web.Storage;

namespace Triominos.Web.Tests;

/// <summary>
/// The table page with a stand-in for JavaScript: the tests play the part of the scene by calling
/// back into the page like the scene does (drop a tile, press the draw key, report that the
/// animations are done) and read the page and the running game.
/// </summary>
public sealed class HomeTests : BunitContext
{
    private readonly MemoryStore _store = new();
    private readonly BunitJSModuleInterop _scene;

    public HomeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _scene = JSInterop.SetupModule("./js/scene/scene.js");
        _scene.Mode = JSRuntimeMode.Loose;
        ComponentFactories.AddStub<PageTitle>();
        Services.AddSingleton<IPreferenceStore>(_store);
        Services.AddSingleton<Preferences>();
    }

    // --- The menu, the setup and the first turn -------------------------------------------

    [Fact]
    public void The_page_opens_on_the_main_menu_without_a_game_to_resume()
    {
        var cut = Render<Home>();

        Assert.Equal(new[] { "Nieuw spel", "Instellingen" }, cut.FindAll(".menu button").Select(b => b.TextContent.Trim()));
        Assert.Empty(cut.FindAll(".actions"));
    }

    [Fact]
    public async Task A_new_game_waits_for_the_deal_and_then_hands_over_to_the_first_player()
    {
        var cut = Render<Home>();

        StartGame(cut);
        var session = cut.Instance.Session!;

        // The scene is still dealing: no screen, no way to draw or pass.
        _scene.VerifyInvoke("deal");
        Assert.Empty(cut.FindAll(".overlay"));
        Assert.True(Disabled(cut, "Trekken"));
        Assert.True(Disabled(cut, "Passen"));

        await AnimationsFinishedAsync(cut);

        Assert.Equal(SessionScreen.Handover, session.Screen);
        Assert.Contains(session.Game.CurrentPlayer.Name, cut.Find(".overlay").TextContent);
        Assert.Equal("Toon mijn stenen", cut.Find(".overlay button.primary").TextContent.Trim());
        Assert.True(Disabled(cut, "Trekken"));
        Assert.True(Disabled(cut, "Passen"));
    }

    [Fact]
    public async Task After_the_handover_the_buttons_follow_what_the_game_allows()
    {
        var cut = await StartTurnAsync();
        var session = cut.Instance.Session!;

        Assert.Equal(SessionScreen.Playing, session.Screen);
        Assert.Empty(cut.FindAll(".overlay"));
        Assert.Equal(!session.CanDraw, Disabled(cut, "Trekken"));
        Assert.Equal(!session.CanPass, Disabled(cut, "Passen"));
    }

    [Fact]
    public async Task A_hint_costs_points_lights_the_suggestion_and_leaves_the_button_off_until_the_next_turn()
    {
        var cut = await StartTurnAsync();
        var session = cut.Instance.Session!;
        Assert.False(Disabled(cut, "Hint"));
        Assert.Contains($"({Scoring.MaxHintsPerGame})", Button(cut, ".actions button", "Hint").TextContent);

        Button(cut, ".actions button", "Hint").Click();

        Assert.Equal((-Scoring.HintPenalty).ToString(), cut.Find(".player.current .player-score").TextContent);
        Assert.StartsWith("Hint", cut.Find(".message").TextContent, StringComparison.Ordinal);
        Assert.Contains($"({Scoring.MaxHintsPerGame - 1})", Button(cut, ".actions button", "Hint").TextContent);
        Assert.True(Disabled(cut, "Hint"));
        _scene.VerifyInvoke("setHint");
        Assert.NotNull(session.Hint);
    }

    [Fact]
    public async Task The_menu_opens_over_the_game_switches_the_buttons_off_and_goes_back_to_it()
    {
        var cut = await StartTurnAsync();
        var session = cut.Instance.Session!;

        Button(cut, ".actions button", "Menu").Click();

        Assert.Equal(new[] { "Verder spelen", "Nieuw spel", "Instellingen" }, cut.FindAll(".menu button").Select(b => b.TextContent.Trim()));
        Assert.True(Disabled(cut, "Trekken"));
        Assert.True(Disabled(cut, "Passen"));

        Button(cut, ".menu button", "Verder spelen").Click();

        Assert.Empty(cut.FindAll(".menu"));
        Assert.Equal(!session.CanDraw, Disabled(cut, "Trekken"));
        Assert.Same(session, cut.Instance.Session);
    }

    [Fact]
    public async Task A_new_game_from_the_menu_replaces_the_running_one()
    {
        var cut = await StartTurnAsync();
        var first = cut.Instance.Session;

        Button(cut, ".actions button", "Menu").Click();
        Button(cut, ".menu button", "Nieuw spel").Click();
        Assert.Contains("wordt vervangen", cut.Find(".setup .note").TextContent);
        cut.Find(".setup button.primary").Click();

        Assert.NotSame(first, cut.Instance.Session);
        Assert.Equal(SessionScreen.Handover, cut.Instance.Session!.Screen);
    }

    // --- A turn --------------------------------------------------------------------------

    [Fact]
    public async Task Laying_the_starting_tile_keeps_the_next_screen_away_until_the_scene_is_done()
    {
        var cut = await StartTurnAsync();
        var session = cut.Instance.Session!;
        var first = session.Game.CurrentPlayer.Name;

        await LayAnyTileAsync(cut);

        // The scene plays the move: the HUD keeps its old numbers and nothing can be pressed.
        _scene.VerifyInvoke("playMove");
        Assert.Empty(cut.FindAll(".overlay"));
        Assert.True(Disabled(cut, "Trekken"));
        Assert.True(Disabled(cut, "Passen"));
        Assert.Equal(first, cut.Find(".player.current .player-name").TextContent);

        await AnimationsFinishedAsync(cut);

        var next = session.Game.CurrentPlayer.Name;
        Assert.NotEqual(first, next);
        Assert.Equal(SessionScreen.Handover, session.Screen);
        Assert.Contains(next, cut.Find(".overlay").TextContent);
        Assert.Equal(next, cut.Find(".player.current .player-name").TextContent);
        Assert.Contains("begint met", cut.Find(".message").TextContent);
    }

    [Fact]
    public async Task Drawing_a_tile_costs_points_and_keeps_the_turn_without_a_screen_in_between()
    {
        var cut = await StartTurnAsync();
        await LayAnyTileAsync(cut);
        await AnimationsFinishedAsync(cut);
        cut.Find(".overlay button.primary").Click();

        var session = cut.Instance.Session!;
        Assert.True(session.CanDraw);
        var player = session.Game.CurrentPlayerIndex;
        var pool = session.Game.PoolCount;
        var score = session.Game.Players[player].Score;
        var scene = CurrentScene();

        await cut.InvokeAsync(scene.HandleDrawRequestedAsync);

        // The HUD shows the old pool until the scene has flown the tile in.
        Assert.Equal(pool - 1, session.Game.PoolCount);
        Assert.Equal(pool.ToString(), cut.Find(".pool-count").TextContent);
        Assert.True(Disabled(cut, "Trekken"));

        await AnimationsFinishedAsync(cut);

        Assert.Equal((pool - 1).ToString(), cut.Find(".pool-count").TextContent);
        Assert.Equal(score - Scoring.DrawPenalty, session.Game.Players[player].Score);
        Assert.Equal(player, session.Game.CurrentPlayerIndex);
        Assert.Equal(SessionScreen.Playing, session.Screen);
        Assert.Empty(cut.FindAll(".overlay"));
    }

    [Fact]
    public async Task A_game_played_to_the_end_shows_the_result_and_offers_another_game()
    {
        var cut = await StartTurnAsync();
        var session = cut.Instance.Session!;

        await PlayToTheEndAsync(cut);

        Assert.Equal(SessionScreen.Finished, session.Screen);
        var heading = session.Winner is { } winner ? $"{winner.Name} wint!" : "Gelijkspel";
        Assert.Equal(heading, cut.Find(".overlay h2").TextContent);
        Assert.Equal(
            session.Game.Players.Select(p => p.Score).OrderDescending().Select(score => score.ToString()),
            cut.FindAll(".results .score").Select(score => score.TextContent));
        Assert.Equal(new[] { "Nog een spel", "Hoofdmenu" }, cut.FindAll(".overlay .menu-buttons button").Select(b => b.TextContent.Trim()));
        Assert.True(Disabled(cut, "Trekken"));
        Assert.True(Disabled(cut, "Passen"));

        Button(cut, ".overlay button", "Nog een spel").Click();

        Assert.NotSame(session, cut.Instance.Session);
        Assert.Equal(SessionScreen.Handover, cut.Instance.Session!.Screen);
    }

    [Fact]
    public async Task After_the_end_screen_the_main_menu_has_no_game_to_resume()
    {
        var cut = await StartTurnAsync();

        await PlayToTheEndAsync(cut);
        Button(cut, ".overlay button", "Hoofdmenu").Click();

        Assert.Equal(new[] { "Nieuw spel", "Instellingen" }, cut.FindAll(".menu button").Select(b => b.TextContent.Trim()));
    }

    // --- Stored settings and players -----------------------------------------------------

    [Fact]
    public void Stored_settings_and_players_are_there_on_the_next_visit()
    {
        _store.Values[Preferences.QualityKey] = "High";
        _store.Values[Preferences.SpeedKey] = "Fast";
        _store.Values[Preferences.SoundKey] = "off";
        _store.Values[Preferences.VolumeKey] = "35";
        _store.Values[Preferences.PlayersKey] = Preferences.FormatPlayers([new("Ann", PlayerColor.Coral), new("Bo", PlayerColor.Rose), new("Cleo", PlayerColor.Amethyst)]);
        var cut = Render<Home>();

        Button(cut, ".menu button", "Instellingen").Click();
        Assert.Equal(new[] { "Hoog", "Snel", "Uit" }, cut.FindAll(".settings .choice.selected").Select(c => c.TextContent.Trim()));
        Assert.Equal("35", cut.Find(".settings output").TextContent);

        cut.Find(".settings button.primary").Click();
        Button(cut, ".menu button", "Nieuw spel").Click();
        Assert.Equal(new[] { "Ann", "Bo", "Cleo" }, cut.FindAll(".name-input").Select(i => i.GetAttribute("value")));
    }

    [Fact]
    public void Damaged_stored_values_are_the_defaults()
    {
        _store.Values[Preferences.QualityKey] = "bogus";
        _store.Values[Preferences.VolumeKey] = "loud";
        _store.Values[Preferences.PlayersKey] = "garbage";
        var cut = Render<Home>();

        Button(cut, ".menu button", "Instellingen").Click();
        Assert.Equal(new[] { "Middel", "Normaal", "Aan" }, cut.FindAll(".settings .choice.selected").Select(c => c.TextContent.Trim()));
        Assert.Equal("60", cut.Find(".settings output").TextContent);

        cut.Find(".settings button.primary").Click();
        Button(cut, ".menu button", "Nieuw spel").Click();
        Assert.Equal(new[] { "Speler 1", "Speler 2" }, cut.FindAll(".name-input").Select(i => i.GetAttribute("value")));
    }

    [Fact]
    public void Changing_a_setting_stores_it()
    {
        var cut = Render<Home>();
        Button(cut, ".menu button", "Instellingen").Click();

        Button(cut, ".settings .choice", "Laag").Click();
        Button(cut, ".settings .choice", "Zeer snel").Click();
        cut.Find(".settings input[type=range]").Change("40");
        Button(cut, ".settings .choice", "Uit").Click();

        Assert.Equal("Low", _store.Values[Preferences.QualityKey]);
        Assert.Equal("VeryFast", _store.Values[Preferences.SpeedKey]);
        Assert.Equal("40", _store.Values[Preferences.VolumeKey]);
        Assert.Equal("off", _store.Values[Preferences.SoundKey]);
    }

    [Fact]
    public void Starting_a_game_stores_its_players()
    {
        var cut = Render<Home>();
        Button(cut, ".menu button", "Nieuw spel").Click();
        cut.FindAll(".name-input")[1].Input("Bea");

        cut.Find(".setup button.primary").Click();

        var stored = Preferences.ParsePlayers(_store.Values[Preferences.PlayersKey]);
        Assert.Equal(new[] { "Speler 1", "Bea" }, stored!.Select(p => p.Name));
    }

    // --- Helpers -------------------------------------------------------------------------

    private static IElement Button(IRenderedComponent<Home> cut, string selector, string text) =>
        cut.FindAll(selector).Single(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal));

    private static bool Disabled(IRenderedComponent<Home> cut, string text) => Button(cut, ".actions button", text).HasAttribute("disabled");

    /// <summary>The scene object that the page created and gave to the JavaScript module.</summary>
    private TableScene CurrentScene() =>
        ((DotNetObjectReference<TableScene>)_scene.Invocations["init"].Last().Arguments[2]!).Value;

    /// <summary>The scene reports that it has finished the animations of the deal or of a move.</summary>
    private async Task AnimationsFinishedAsync(IRenderedComponent<Home> cut)
    {
        var scene = CurrentScene();
        await cut.InvokeAsync(scene.HandleAnimationsFinished);
    }

    /// <summary>Menu, setup with the default players, start.</summary>
    private static void StartGame(IRenderedComponent<Home> cut)
    {
        Button(cut, ".menu button", "Nieuw spel").Click();
        cut.Find(".setup button.primary").Click();
    }

    /// <summary>Starts a game, waits for the deal and presses "Toon mijn stenen".</summary>
    private async Task<IRenderedComponent<Home>> StartTurnAsync()
    {
        var cut = Render<Home>();
        StartGame(cut);
        await AnimationsFinishedAsync(cut);
        cut.Find(".overlay button.primary").Click();
        return cut;
    }

    /// <summary>Drops the first tile of the rack that fits somewhere, like the player does with the mouse.</summary>
    private async Task LayAnyTileAsync(IRenderedComponent<Home> cut)
    {
        var game = cut.Instance.Session!.Game;
        foreach (var tile in game.CurrentPlayer.Rack)
        {
            var placements = game.ValidPlacements(tile.Id);
            if (placements.Count > 0)
            {
                var placement = placements[0];
                var scene = CurrentScene();
                await cut.InvokeAsync(() => scene.HandleTileDroppedAsync(tile.Id, placement.Cell.Row, placement.Cell.Col, placement.Rotation));
                return;
            }
        }

        throw new InvalidOperationException("No tile of the current player fits anywhere.");
    }

    /// <summary>Plays every turn by the simplest rule: lay a tile if one fits, else draw, else pass.</summary>
    private async Task PlayToTheEndAsync(IRenderedComponent<Home> cut)
    {
        var session = cut.Instance.Session!;
        for (var step = 0; step < 2000 && session.Screen != SessionScreen.Finished; step++)
        {
            if (session.Screen == SessionScreen.Handover)
            {
                cut.Find(".overlay button.primary").Click();
                continue;
            }

            // At every step the buttons show what the game allows.
            Assert.Equal(!session.CanDraw, Disabled(cut, "Trekken"));
            Assert.Equal(!session.CanPass, Disabled(cut, "Passen"));

            var game = session.Game;
            if (game.CurrentPlayer.Rack.Any(tile => game.ValidPlacements(tile.Id).Count > 0))
            {
                await LayAnyTileAsync(cut);
            }
            else if (session.CanDraw)
            {
                var scene = CurrentScene();
                await cut.InvokeAsync(scene.HandleDrawRequestedAsync);
            }
            else
            {
                Assert.True(session.CanPass, "The player can neither lay, draw nor pass.");
                Button(cut, ".actions button", "Passen").Click();
            }

            await AnimationsFinishedAsync(cut);
        }

        Assert.Equal(SessionScreen.Finished, session.Screen);
    }
}
