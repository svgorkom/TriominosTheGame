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
/// The table page with a computer opponent: the tests play the part of the scene like in
/// <see cref="HomeTests"/>, and the computer answers by itself after the scene reports it is done.
/// </summary>
public sealed class ComputerHomeTests : BunitContext
{
    private static readonly string[] Names = ["Speler 1", "Computer"];

    private readonly MemoryStore _store = new();
    private readonly BunitJSModuleInterop _scene;

    public ComputerHomeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _scene = JSInterop.SetupModule("./js/scene/scene.js");
        _scene.Mode = JSRuntimeMode.Loose;
        ComponentFactories.AddStub<PageTitle>();
        Services.AddSingleton<IPreferenceStore>(_store);
        Services.AddSingleton<Preferences>();
    }

    /// <summary>The seed for which the given seat (0 = the human, 1 = the computer) starts.</summary>
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

    private IRenderedComponent<Home> RenderHome(int seed) =>
        Render<Home>(parameters =>
        {
            parameters.Add(home => home.Seed, seed);
            parameters.Add(home => home.ComputerPause, TimeSpan.Zero);
        });

    private static IElement Button(IRenderedComponent<Home> cut, string selector, string text) =>
        cut.FindAll(selector).Single(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal));

    private static bool Disabled(IRenderedComponent<Home> cut, string text) => Button(cut, ".actions button", text).HasAttribute("disabled");

    private TableScene CurrentScene() =>
        ((DotNetObjectReference<TableScene>)_scene.Invocations["init"].Last().Arguments[2]!).Value;

    /// <summary>What changes with every move; used to see that the computer moved.</summary>
    private static (int Tiles, int Pool, int Scores, int Current, int Draws) Signature(PlaySession session) =>
        (session.Game.Board.Tiles.Count, session.Game.PoolCount, session.Game.Players.Sum(p => p.Score), session.Game.CurrentPlayerIndex, session.Game.DrawsThisTurn);

    /// <summary>
    /// The scene reports it is done. If that gives the turn to the computer, waits until the computer
    /// has made its move (which the page hands to the scene again).
    /// </summary>
    private async Task SceneDoneAsync(IRenderedComponent<Home> cut)
    {
        var session = cut.Instance.Session!;
        var before = Signature(session);
        await cut.InvokeAsync(CurrentScene().HandleAnimationsFinished);
        if (session.IsComputerTurn)
        {
            cut.WaitForAssertion(() => Assert.NotEqual(before, Signature(session)));
        }
    }

    /// <summary>Menu, setup with the second player switched to the computer, start.</summary>
    private static void StartAgainstTheComputer(IRenderedComponent<Home> cut)
    {
        Button(cut, ".menu button", "Nieuw spel").Click();
        cut.FindAll(".kind-toggle")[1].Click();
        cut.Find(".setup button.primary").Click();
    }

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

    [Fact]
    public void The_setup_screen_starts_a_game_with_the_computer_in_the_second_seat()
    {
        var cut = RenderHome(SeedWhereSeatStarts(0));

        StartAgainstTheComputer(cut);

        var setup = cut.Instance.Session!.Setup;
        Assert.Equal(new[] { "Speler 1", "Computer" }, setup.Select(p => p.Name));
        Assert.Equal(new[] { false, true }, setup.Select(p => p.IsComputer));
    }

    [Fact]
    public async Task A_human_who_starts_plays_at_once_without_a_handover_screen()
    {
        var cut = RenderHome(SeedWhereSeatStarts(0));
        StartAgainstTheComputer(cut);

        await SceneDoneAsync(cut); // the deal

        Assert.Empty(cut.FindAll(".overlay"));
        Assert.Equal(SessionScreen.Playing, cut.Instance.Session!.Screen);
        Assert.False(cut.Instance.Session.IsComputerTurn);
    }

    [Fact]
    public async Task A_computer_that_starts_plays_by_itself_after_the_deal_and_gives_the_turn_to_the_human()
    {
        var cut = RenderHome(SeedWhereSeatStarts(1));
        StartAgainstTheComputer(cut);
        var session = cut.Instance.Session!;

        await SceneDoneAsync(cut); // the deal; the computer then lays its starting tile

        Assert.Single(session.Game.Board.Tiles);
        _scene.VerifyInvoke("playMove");

        // While the scene plays the computer's move, nothing can be pressed.
        Assert.True(Disabled(cut, "Trekken"));
        Assert.True(Disabled(cut, "Passen"));
        Assert.True(Disabled(cut, "Menu"));

        await SceneDoneAsync(cut); // the computer's move is shown

        Assert.False(session.IsComputerTurn);
        Assert.Equal(0, session.Game.CurrentPlayerIndex);
        Assert.Empty(cut.FindAll(".overlay"));
        Assert.Contains("Computer begint met", cut.Find(".message").TextContent);
        Assert.Equal(!session.CanDraw, Disabled(cut, "Trekken"));
        Assert.False(Disabled(cut, "Menu"));
    }

    [Fact]
    public async Task After_the_move_of_the_human_the_computer_answers_without_a_click()
    {
        var cut = RenderHome(SeedWhereSeatStarts(0));
        StartAgainstTheComputer(cut);
        var session = cut.Instance.Session!;
        await SceneDoneAsync(cut); // the deal

        await LayAnyTileAsync(cut); // the human starts
        Assert.True(Disabled(cut, "Trekken"));
        _scene.VerifyInvoke("playMove", 1);

        await SceneDoneAsync(cut); // the computer answers inside this call, with no click

        _scene.VerifyInvoke("playMove", 2);
    }

    [Fact]
    public async Task A_human_and_the_computer_play_a_whole_game_through_the_page()
    {
        var cut = RenderHome(SeedWhereSeatStarts(0));
        StartAgainstTheComputer(cut);
        var session = cut.Instance.Session!;
        await SceneDoneAsync(cut); // the deal

        for (var step = 0; step < 3000 && session.Screen != SessionScreen.Finished; step++)
        {
            if (session.IsComputerTurn)
            {
                // The computer's move is on its way or was just made; let the scene finish it.
                await SceneDoneAsync(cut);
                continue;
            }

            var game = session.Game;
            Assert.Empty(cut.FindAll(".overlay"));
            Assert.Equal(!session.CanDraw, Disabled(cut, "Trekken"));
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

            await SceneDoneAsync(cut);
        }

        Assert.Equal(SessionScreen.Finished, session.Screen);
        await SceneDoneAsync(cut);
        Assert.NotEmpty(cut.FindAll(".results"));
    }
}
