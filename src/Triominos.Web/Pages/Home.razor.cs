using Microsoft.AspNetCore.Components;
using Triominos.Core.Board;
using Triominos.Core.Game;
using Triominos.Web.Interop;
using Triominos.Web.Online;
using Triominos.Web.Play;
using Triominos.Web.Storage;

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
        Online,
        Settings,
    }

    private ElementReference _canvas;
    private ElementReference _continueButton;
    private TableScene? _scene;
    private PlaySession? _session;
    private HudState? _hud;
    private Overlay _overlay = Overlay.Menu;
    private IReadOnlyList<PlayerSetup> _setup = PlayerSetup.Default;
    private HintRules _hints = HintRules.Default;
    private SceneQuality _quality = SceneQuality.Medium;
    private TableTheme _theme = TableTheme.Classic;
    private AnimationSpeed _speed = AnimationSpeed.Normal;
    private bool _soundOn = true;
    private int _volume = 60;
    private int _gamesStarted;
    private bool _focusContinueButton;

    // A started game is dealt in the first render after it, when the pot and the player panels
    // that the dealt tiles fly to are on the page, and once the scene exists (a player can be
    // quick to press "Start het spel" while the scene still loads).
    private bool _dealPending;
    private bool _sceneReady;

    // An online game that is already under way (a refreshed page, a player who comes back) is shown as it stands, without a deal.
    private bool _resumeSyncPending;

    // Actions of the other players that arrived while the scene was still busy; they are played one by one, in order.
    private readonly Queue<(int Seat, GameAction Action)> _remoteQueue = new();

    // True while the scene plays a move or the deal: the HUD keeps its old numbers, the next
    // screen stays away and input is refused, until the scene says it is done.
    private bool _busy;

    // True from the start of a game until the deal is done: the rack of a human who plays at once
    // (no handover screen) stays out of sight until the tiles have been dealt.
    private bool _dealing;

    /// <summary>Gets or sets a fixed seed (<c>?seed=7</c>) that makes every game the same; for testing.</summary>
    [Parameter]
    [SupplyParameterFromQuery]
    public int? Seed { get; set; }

    /// <summary>Gets or sets the code of a room, from a link someone shared (<c>?kamer=BCDFG</c>); the page then opens on the online screen.</summary>
    [Parameter]
    [SupplyParameterFromQuery(Name = "kamer")]
    public string? Room { get; set; }

    /// <summary>Gets or sets how long a computer player waits before its move; the default follows the animation speed. Tests set it to zero.</summary>
    [Parameter]
    public TimeSpan? ComputerPause { get; set; }

    private bool CanResume => _session is { Screen: not SessionScreen.Finished };

    /// <summary>Gets whether an online game cannot take input now, because the connection to the room is down.</summary>
    private bool OnlineBlocked => _session is { LocalSeat: not null } && Online.Status != OnlineStatus.Online;

    /// <summary>Gets the running game, for the tests of the page.</summary>
    internal PlaySession? Session => _session;

    protected override async Task OnInitializedAsync()
    {
        // Settings and players come back from the last visit; whatever is missing or damaged is the default.
        var settings = await Preferences.LoadSettingsAsync();
        _quality = settings.Quality;
        _speed = settings.Speed;
        _soundOn = settings.SoundOn;
        _volume = settings.Volume;
        _theme = settings.Theme;
        _setup = await Preferences.LoadPlayersAsync();
        _hints = await Preferences.LoadHintsAsync();

        Online.Changed += OnOnlineChanged;
        Online.GameStarted += OnOnlineGameStarted;
        Online.ActionReceived += OnOnlineAction;
        if (!string.IsNullOrWhiteSpace(Room))
        {
            _overlay = Overlay.Online;
        }

        // A refreshed page takes its seat back in the room of the last visit, if that is still there.
        _ = Online.TryResumeAsync();
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
            await _scene.InitAsync(_canvas, _quality, _theme);
            await _scene.SetAnimationSpeedAsync(_speed);
            await _scene.SetSoundAsync(_soundOn, _volume, preview: false);
            await _scene.ShowEmptyTableAsync();
            _sceneReady = true;
        }

        if (_dealPending && _sceneReady && _session is not null)
        {
            _dealPending = false;
            await SyncSceneAsync(focus: true);
            await DealAsync();
        }

        if (_resumeSyncPending && _sceneReady && _session is not null)
        {
            _resumeSyncPending = false;
            await SyncSceneAsync(focus: true);
            _hud = HudState.From(_session);
            StateHasChanged();
            await PumpRemoteAsync();
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
        if (_dealing)
        {
            await _scene.SetRackAsync([], []);
        }
        else
        {
            await _scene.SetRackAsync(_session.VisibleRack, _session.MarkedTiles);
        }

        await _scene.SetHintAsync(_session.HintCell);
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
        if (_dealing)
        {
            _dealing = false;
            if (_session is { VisibleRack.Count: > 0 })
            {
                _ = ShowRackAsync(_session);
            }
        }

        if (_session is not null)
        {
            _hud = HudState.From(_session);
        }

        // A computer plays on its own: the page stays busy, so no button or menu gets in between,
        // until the turn is a human's again or the round is over.
        if (_session is { IsComputerTurn: true } session && _overlay == Overlay.None)
        {
            _busy = true;
            _ = PlayComputerStepAsync(session);
        }

        // The other players may have moved while the scene was busy.
        if (!_busy && _remoteQueue.Count > 0)
        {
            _ = PumpRemoteAsync();
        }

        StateHasChanged();
    }

    private async Task ShowRackAsync(PlaySession session)
    {
        if (_scene is not null)
        {
            await _scene.SetRackAsync(session.VisibleRack, session.MarkedTiles);
        }
    }

    /// <summary>Waits a moment, so the player can follow what happened, and plays one move for the computer.</summary>
    private async Task PlayComputerStepAsync(PlaySession session)
    {
        await Task.Delay(ComputerPause ?? AnimationSpeeds.ComputerPause(_speed));
        await InvokeAsync(async () =>
        {
            // A new game replaces the session; a step meant for the old one must do nothing.
            if (!ReferenceEquals(session, _session) || !session.IsComputerTurn)
            {
                return;
            }

            await AfterMoveAsync(session.PlayComputerStep());
        });
    }

    private void OpenMenu() => _overlay = Overlay.Menu;

    private void OpenSetup() => _overlay = Overlay.Setup;

    private void OpenOnline() => _overlay = Overlay.Online;

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
    private async Task StartGameAsync(IReadOnlyList<PlayerSetup> players)
    {
        if (_busy)
        {
            return;
        }

        // A game on this screen replaces a game in a room, and the seat in that room goes with it.
        if (Online.Status != OnlineStatus.Offline)
        {
            await Online.LeaveAsync();
        }

        _remoteQueue.Clear();
        _setup = players;
        _session = new PlaySession(players, NewRandom(), _hints);
        _hud = HudState.From(_session);
        _overlay = Overlay.None;
        _focusContinueButton = true;
        _busy = true;
        _dealPending = true;
        _dealing = true;
        await Preferences.SavePlayersAsync(players);
        await Preferences.SaveHintsAsync(_hints);
    }

    private void SetHints(HintRules rules) => _hints = rules;

    private static string HintTitle(HintRules rules) =>
        $"Laat een voorstel voor je zet zien ({(rules.Penalty == 0 ? "gratis" : $"-{rules.Penalty} punten")}, hoogstens {rules.MaxPerPlayer} per spel)";

    private Task PlayAgainAsync() => StartGameAsync(_setup);

    /// <summary>The host deals a new game for everyone in the room; it arrives here like it does at the other screens.</summary>
    private Task PlayAgainOnlineAsync() => Online.StartGameAsync(_hints);

    private void OnOnlineChanged() => _ = InvokeAsync(StateHasChanged);

    private void OnOnlineGameStarted(OnlineGameStart start) => _ = InvokeAsync(() => StartOnlineGameAsync(start));

    private void OnOnlineAction(int seat, GameAction action) =>
        _ = InvokeAsync(async () =>
        {
            _remoteQueue.Enqueue((seat, action));
            await PumpRemoteAsync();
        });

    /// <summary>Starts the game of a room on this screen: every player deals from the same seed, so the games are equal.</summary>
    private async Task StartOnlineGameAsync(OnlineGameStart start)
    {
        PlaySession session;
        try
        {
            var players = start.Names.Select((name, i) => new PlayerSetup(name, PlayerColors.All[i % PlayerColors.All.Count])).ToList();
            session = new PlaySession(players, new Random(start.Seed), start.Hints, start.LocalSeat);
        }
        catch (ArgumentException)
        {
            // The names in the room cannot make a game; better to leave than to play a game the others do not.
            await Online.LeaveAsync();
            return;
        }

        _remoteQueue.Clear();
        _session = session;
        if (start.History.Count > 0)
        {
            session.Replay(start.History);
        }

        _hud = HudState.From(session);
        _overlay = Overlay.None;
        if (start.History.Count == 0)
        {
            _busy = true;
            _dealPending = true;
            _dealing = true;
        }
        else
        {
            _busy = false;
            _dealing = false;
            _dealPending = false;
            _resumeSyncPending = true;
        }

        StateHasChanged();
    }

    /// <summary>Plays the queued actions of the other players, one at a time, each after the scene has finished the one before.</summary>
    private async Task PumpRemoteAsync()
    {
        while (!_busy && _sceneReady && _session is { LocalSeat: not null } session && _remoteQueue.TryDequeue(out var item))
        {
            var result = session.ApplyRemote(item.Seat, item.Action);
            if (item.Action is HintAction && result.Succeeded)
            {
                // A hint is only a change of score.
                _hud = HudState.From(session);
                StateHasChanged();
                continue;
            }

            await AfterMoveAsync(result);
        }
    }

    /// <summary>Tells the other players what this screen's player did.</summary>
    private async Task ReportAsync(MoveResult result, GameAction action)
    {
        if (result.Succeeded && _session?.LocalSeat is not null)
        {
            await Online.SendActionAsync(action);
        }
    }

    private async Task ThemeChangedAsync(TableTheme theme)
    {
        _theme = theme;
        await SaveSettingsAsync();
        if (_scene is not null)
        {
            await _scene.SetThemeAsync(theme);
        }
    }

    private async Task QualityChangedAsync(SceneQuality quality)
    {
        _quality = quality;
        await SaveSettingsAsync();
        if (_scene is not null)
        {
            await _scene.SetQualityAsync(quality);
        }
    }

    private async Task SpeedChangedAsync(AnimationSpeed speed)
    {
        _speed = speed;
        await SaveSettingsAsync();
        if (_scene is not null)
        {
            await _scene.SetAnimationSpeedAsync(speed);
        }
    }

    private async Task SoundOnChangedAsync(bool soundOn)
    {
        _soundOn = soundOn;
        await ApplySoundAsync(preview: soundOn);
    }

    private async Task VolumeChangedAsync(int volume)
    {
        _volume = volume;
        await ApplySoundAsync(preview: true);
    }

    private Task SaveSettingsAsync() =>
        Preferences.SaveSettingsAsync(new PlaySettings(_quality, _speed, _soundOn, _volume, _theme));

    private async Task ApplySoundAsync(bool preview)
    {
        await SaveSettingsAsync();
        if (_scene is not null)
        {
            await _scene.SetSoundAsync(_soundOn, _volume, preview);
        }
    }

    private async Task<TileSelection> SelectTileAsync(int tileId)
    {
        var session = _session;
        if (session is null || _overlay != Overlay.None || session.IsRemoteTurn)
        {
            return new TileSelection([], []);
        }

        var placements = session.Pick(tileId);
        await InvokeAsync(() =>
        {
            _hud = HudState.From(session);
            StateHasChanged();
        });
        // Every open cell lights up, also for a tile that fits nowhere: it can be carried, never dropped.
        return new TileSelection([.. session.Game.OpenCells], placements);
    }

    private async Task DropTileAsync(int tileId, Placement placement)
    {
        await InvokeAsync(async () =>
        {
            if (_busy || _session is null || _overlay != Overlay.None)
            {
                return;
            }

            if (OnlineBlocked)
            {
                // No connection: the tile goes back to the rack.
                await AfterMoveAsync(new MoveResult(MoveError.WrongPhase, []));
                return;
            }

            var result = _session.Place(tileId, placement);
            await ReportAsync(result, new PlaceAction(tileId, placement));
            await AfterMoveAsync(result);
        });
    }

    private async Task DrawAsync()
    {
        await InvokeAsync(async () =>
        {
            if (_busy || _session is null || _overlay != Overlay.None || OnlineBlocked)
            {
                return;
            }

            var result = _session.Draw();
            await ReportAsync(result, new DrawAction());
            await AfterMoveAsync(result);
        });
    }

    private async Task PassAsync()
    {
        if (_busy || _session is null || _overlay != Overlay.None || OnlineBlocked)
        {
            return;
        }

        var result = _session.Pass();
        await ReportAsync(result, new PassAction());
        await AfterMoveAsync(result);
    }

    /// <summary>Takes a hint: the score drops, the suggested tile is singled out in the rack and its cell lights up.</summary>
    private async Task HintAsync()
    {
        if (_busy || _session is null || _scene is null || _overlay != Overlay.None || OnlineBlocked || !_session.CanHint)
        {
            return;
        }

        var hint = _session.RequestHint();
        await ReportAsync(hint, new HintAction());
        _hud = HudState.From(_session);
        await ShowRackAsync(_session);
        await _scene.SetHintAsync(_session.HintCell);
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
            _busy = false;
            _hud = HudState.From(_session);
            await SyncSceneAsync(focus: false);
            StateHasChanged();
            return;
        }

        _focusContinueButton = _session.Screen == SessionScreen.Handover;
        _busy = true;
        await _scene.SetHintAsync(null);
        await _scene.PlayMoveAsync(MoveViews.From(_session.Game, result, _session.VisibleRack, _session.MarkedTiles, _session.Setup, _session.LocalSeat));
        StateHasChanged();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Online.Changed -= OnOnlineChanged;
        Online.GameStarted -= OnOnlineGameStarted;
        Online.ActionReceived -= OnOnlineAction;
        if (_scene is not null)
        {
            await _scene.DisposeAsync();
        }
    }
}
