using Microsoft.AspNetCore.Components;
using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Web.Interop;
using Triominos.Web.Play;

namespace Triominos.Web.Pages;

/// <summary>The table: the 3D scene, the HUD, the menus and the handover between turns.</summary>
public partial class Home
{
    /// <summary>The screen laid over the table; none while a game is being played.</summary>
    private enum Overlay
    {
        None,
        Menu,
        Setup,
        Settings,
    }

    private ElementReference _canvas;
    private ElementReference _continueButton;
    private TableScene? _scene;
    private PlaySession? _session;
    private HudState? _hud;
    private Overlay _overlay = Overlay.Menu;
    private IReadOnlyList<PlayerSetup> _setup = PlayerSetup.Default;
    private SceneQuality _quality = SceneQuality.Medium;
    private AnimationSpeed _speed = AnimationSpeed.Normal;
    private int _gamesStarted;
    private bool _focusContinueButton;

    // A started game is dealt in the first render after it, when the pot and the player panels
    // that the dealt tiles fly to are on the page, and once the scene exists (a player can be
    // quick to press "Start het spel" while the scene still loads).
    private bool _dealPending;
    private bool _sceneReady;

    // True while the scene plays a move or the deal: the HUD keeps its old numbers, the next
    // screen stays away and input is refused, until the scene says it is done.
    private bool _busy;

    /// <summary>Gets or sets a fixed seed (<c>?seed=7</c>) that makes every game the same; for testing.</summary>
    [Parameter]
    [SupplyParameterFromQuery]
    public int? Seed { get; set; }

    private bool CanResume => _session is { Screen: not SessionScreen.Finished };

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
            await _scene.InitAsync(_canvas, _quality);
            await _scene.SetAnimationSpeedAsync(_speed);
            await _scene.ShowEmptyTableAsync();
            _sceneReady = true;
        }

        if (_dealPending && _sceneReady && _session is not null)
        {
            _dealPending = false;
            await SyncSceneAsync(focus: true);
            await DealAsync();
        }

        if (_focusContinueButton && !_busy && _overlay == Overlay.None && _session?.Screen == SessionScreen.Handover)
        {
            _focusContinueButton = false;
            await _continueButton.FocusAsync();
        }
    }

    private Random NewRandom() => Seed is { } seed ? new Random(seed + _gamesStarted++) : Random.Shared;

    /// <summary>Sends the board and the visible rack to the scene at once, without animation.</summary>
    private async Task SyncSceneAsync(bool focus)
    {
        if (_scene is null || _session is null)
        {
            return;
        }

        await _scene.SetBoardAsync(_session.Game.Board, focus);
        await _scene.SetRackAsync(_session.VisibleRack, _session.MarkedTiles);
    }

    /// <summary>Deals the tiles of the new round from the pot to the players; the first screen waits for it.</summary>
    private async Task DealAsync()
    {
        if (_scene is null || _session is null)
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
        if (_session is not null)
        {
            _hud = HudState.From(_session);
        }

        StateHasChanged();
    }

    private void OpenMenu() => _overlay = Overlay.Menu;

    private void OpenSetup() => _overlay = Overlay.Setup;

    private void OpenSettings() => _overlay = Overlay.Settings;

    private void ResumeGame() => _overlay = Overlay.None;

    /// <summary>Opens the menu over a running game. A tile the player carries goes back to the rack.</summary>
    private async Task OpenMenuAsync()
    {
        if (_busy)
        {
            return;
        }

        _overlay = Overlay.Menu;
        if (_scene is not null && _session is not null)
        {
            await _scene.SetRackAsync(_session.VisibleRack, _session.MarkedTiles);
        }
    }

    /// <summary>Starts a game with the chosen players. The deal itself follows in the next render.</summary>
    private Task StartGameAsync(IReadOnlyList<PlayerSetup> players)
    {
        if (_busy)
        {
            return Task.CompletedTask;
        }

        _setup = players;
        _session = new PlaySession(players, NewRandom());
        _hud = HudState.From(_session);
        _overlay = Overlay.None;
        _focusContinueButton = true;
        _busy = true;
        _dealPending = true;
        return Task.CompletedTask;
    }

    private Task PlayAgainAsync() => StartGameAsync(_setup);

    private async Task QualityChangedAsync(SceneQuality quality)
    {
        _quality = quality;
        if (_scene is not null)
        {
            await _scene.SetQualityAsync(quality);
        }
    }

    private async Task SpeedChangedAsync(AnimationSpeed speed)
    {
        _speed = speed;
        if (_scene is not null)
        {
            await _scene.SetAnimationSpeedAsync(speed);
        }
    }

    private async Task<IReadOnlyList<Placement>> SelectTileAsync(int tileId)
    {
        var session = _session;
        if (session is null || _overlay != Overlay.None)
        {
            return [];
        }

        var placements = session.Pick(tileId);
        await InvokeAsync(() =>
        {
            _hud = HudState.From(session);
            StateHasChanged();
        });
        return placements;
    }

    private async Task DropTileAsync(int tileId, Placement placement)
    {
        await InvokeAsync(async () =>
        {
            if (_busy || _session is null || _overlay != Overlay.None)
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
            if (_busy || _session is null || _overlay != Overlay.None)
            {
                return;
            }

            await AfterMoveAsync(_session.Draw());
        });
    }

    private async Task PassAsync()
    {
        if (_busy || _session is null || _overlay != Overlay.None)
        {
            return;
        }

        await AfterMoveAsync(_session.Pass());
    }

    private async Task ContinueAsync()
    {
        _session?.Continue();
        await SyncSceneAsync(focus: false);
    }

    /// <summary>
    /// After a move. An accepted move is handed to the scene to play, and the page waits for it. A
    /// refused move only shows its message and puts the tile back.
    /// </summary>
    private async Task AfterMoveAsync(MoveResult result)
    {
        if (_scene is null || _session is null)
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_scene is not null)
        {
            await _scene.DisposeAsync();
        }
    }
}
