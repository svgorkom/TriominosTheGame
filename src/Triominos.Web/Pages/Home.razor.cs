using Microsoft.AspNetCore.Components;
using Triominos.Core.Board;
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
    private int _gamesStarted;
    private bool _focusContinueButton;

    /// <summary>Gets or sets a fixed seed (<c>?seed=7</c>) that makes every game the same; for testing.</summary>
    [Parameter]
    [SupplyParameterFromQuery]
    public int? Seed { get; set; }

    private SceneQuality Quality { get; set; } = SceneQuality.Medium;

    protected override void OnInitialized()
    {
        _session = new PlaySession(NewRandom());
        _focusContinueButton = true;
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
            };
            await _scene.InitAsync(_canvas, Quality);
            await SyncSceneAsync(focus: true);
        }

        if (_focusContinueButton && _session.Screen == SessionScreen.Handover)
        {
            _focusContinueButton = false;
            await _continueButton.FocusAsync();
        }
    }

    private Random NewRandom() => Seed is { } seed ? new Random(seed + _gamesStarted++) : Random.Shared;

    /// <summary>Sends the board and the visible rack to the scene.</summary>
    private async Task SyncSceneAsync(bool focus)
    {
        if (_scene is null)
        {
            return;
        }

        await _scene.SetBoardAsync(_session.Game.Board, focus);
        await _scene.SetRackAsync(_session.VisibleRack, _session.MarkedTiles);
    }

    private Task<IReadOnlyList<Placement>> SelectTileAsync(int tileId)
    {
        return InvokeAsync(() =>
        {
            var placements = _session.Pick(tileId);
            StateHasChanged();
            return placements;
        });
    }

    private async Task DropTileAsync(int tileId, Placement placement)
    {
        await InvokeAsync(async () =>
        {
            _session.Place(tileId, placement);
            await AfterMoveAsync();
        });
    }

    private async Task DrawAsync()
    {
        await InvokeAsync(async () =>
        {
            _session.Draw();
            await AfterMoveAsync();
        });
    }

    private async Task PassAsync()
    {
        _session.Pass();
        await AfterMoveAsync();
    }

    private async Task ContinueAsync()
    {
        _session.Continue();
        await SyncSceneAsync(focus: false);
    }

    private async Task NewGameAsync()
    {
        _session = new PlaySession(NewRandom());
        _focusContinueButton = true;
        await SyncSceneAsync(focus: true);
    }

    /// <summary>After any move, also a refused one: redraws the board and rack (a refused tile goes back).</summary>
    private async Task AfterMoveAsync()
    {
        _focusContinueButton = _session.Screen == SessionScreen.Handover;
        await SyncSceneAsync(focus: false);
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
