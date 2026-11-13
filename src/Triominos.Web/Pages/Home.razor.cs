using Microsoft.AspNetCore.Components;
using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Web.Interop;
using Triominos.Web.Play;

namespace Triominos.Web.Pages;

/// <summary>The table: the 3D scene, the HUD and the handover between turns.</summary>
public partial class Home
{
    private ElementReference _canvas;
    private ElementReference _continueButton;
    private TableScene? _scene;
    private PlaySession _session = default!;
    private HudState _hud = default!;
    private int _gamesStarted;
    private bool _focusContinueButton;

    // True while the scene plays a move or the deal: the HUD keeps its old numbers, the next
    // screen stays away and input is refused, until the scene says it is done.
    private bool _busy;

    /// <summary>Gets or sets a fixed seed (<c>?seed=7</c>) that makes every game the same; for testing.</summary>
    [Parameter]
    [SupplyParameterFromQuery]
    public int? Seed { get; set; }

    private SceneQuality Quality { get; set; } = SceneQuality.Medium;

    protected override void OnInitialized()
    {
        _session = new PlaySession(NewRandom());
        _hud = HudState.From(_session);
        _focusContinueButton = true;

        // The first screen follows the deal, which starts as soon as the scene exists.
        _busy = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _scene = new TableScene(JS)
            {
                OnTileSelected = SelectTileAsync,
                OnTileDropped = DropTileAsync,
                OnDrawRequested = DrawAsync,
                OnAnimationsFinished = () => _ = InvokeAsync(AnimationsFinished),
            };
            await _scene.InitAsync(_canvas, Quality);
            await SyncSceneAsync(focus: true);
            await DealAsync();
        }

        if (_focusContinueButton && !_busy && _session.Screen == SessionScreen.Handover)
        {
            _focusContinueButton = false;
            await _continueButton.FocusAsync();
        }
    }

    private Random NewRandom() => Seed is { } seed ? new Random(seed + _gamesStarted++) : Random.Shared;

    /// <summary>Sends the board and the visible rack to the scene at once, without animation.</summary>
    private async Task SyncSceneAsync(bool focus)
    {
        if (_scene is null)
        {
            return;
        }

        await _scene.SetBoardAsync(_session.Game.Board, focus);
        await _scene.SetRackAsync(_session.VisibleRack, _session.MarkedTiles);
    }

    /// <summary>Deals the tiles of the new round from the pot to the players; the first screen waits for it.</summary>
    private async Task DealAsync()
    {
        if (_scene is null)
        {
            _busy = false;
            return;
        }

        _busy = true;
        await _scene.DealAsync([.. _session.Game.Players.Select(p => p.Rack.Count)]);
    }

    /// <summary>The scene is done: show the numbers of the new state and, if it is a new turn, the handover screen.</summary>
    private void AnimationsFinished()
    {
        _busy = false;
        _hud = HudState.From(_session);
        StateHasChanged();
    }

    private async Task<IReadOnlyList<Placement>> SelectTileAsync(int tileId)
    {
        var placements = _session.Pick(tileId);
        await InvokeAsync(() =>
        {
            _hud = HudState.From(_session);
            StateHasChanged();
        });
        return placements;
    }

    private async Task DropTileAsync(int tileId, Placement placement)
    {
        await InvokeAsync(async () =>
        {
            if (_busy)
            {
                return;
            }

            await AfterMoveAsync(_session.Place(tileId, placement));
        });
    }

    private async Task DrawAsync()
    {
        await InvokeAsync(async () =>
        {
            if (_busy)
            {
                return;
            }

            await AfterMoveAsync(_session.Draw());
        });
    }

    private async Task PassAsync()
    {
        if (_busy)
        {
            return;
        }

        await AfterMoveAsync(_session.Pass());
    }

    private async Task ContinueAsync()
    {
        _session.Continue();
        await SyncSceneAsync(focus: false);
    }

    private async Task NewGameAsync()
    {
        if (_busy)
        {
            return;
        }

        _session = new PlaySession(NewRandom());
        _hud = HudState.From(_session);
        _focusContinueButton = true;
        _busy = true;
        await SyncSceneAsync(focus: true);
        await DealAsync();
    }

    /// <summary>
    /// After a move. An accepted move is handed to the scene to play, and the page waits for it. A
    /// refused move only shows its message and puts the tile back.
    /// </summary>
    private async Task AfterMoveAsync(MoveResult result)
    {
        if (_scene is null)
        {
            return;
        }

        if (!result.Succeeded)
        {
            _hud = HudState.From(_session);
            await SyncSceneAsync(focus: false);
            StateHasChanged();
            return;
        }

        _focusContinueButton = _session.Screen == SessionScreen.Handover;
        _busy = true;
        await _scene.PlayMoveAsync(MoveViews.From(_session.Game, result, _session.VisibleRack, _session.MarkedTiles));
        StateHasChanged();
    }

    private async Task QualityChangedAsync()
    {
        if (_scene is not null)
        {
            await _scene.SetQualityAsync(Quality);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_scene is not null)
        {
            await _scene.DisposeAsync();
        }
    }
}
