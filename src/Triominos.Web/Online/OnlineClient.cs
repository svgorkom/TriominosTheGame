using System.Net.WebSockets;
using System.Text.Json;
using Triominos.Core.Game;
using Triominos.Web.Play;
using Triominos.Web.Storage;

namespace Triominos.Web.Online;

/// <summary>Whether the game is connected to its room.</summary>
public enum OnlineStatus
{
    /// <summary>In no room.</summary>
    Offline,

    /// <summary>Connecting to a room and waiting for its answer.</summary>
    Connecting,

    /// <summary>In a room and connected.</summary>
    Online,

    /// <summary>In a room, the connection dropped and the game is trying to get it back.</summary>
    Reconnecting,
}

/// <summary>A player in the room, as the lobby shows them.</summary>
/// <param name="Seat">The seat, which is the position in the turn order.</param>
/// <param name="Name">The name.</param>
/// <param name="Connected">Whether the player is connected now.</param>
public sealed record LobbyPlayer(int Seat, string Name, bool Connected);

/// <summary>Everything a screen needs to start playing an online game.</summary>
/// <param name="Seed">The seed that deals the game; every player deals from the same one.</param>
/// <param name="Hints">How hints work in this game.</param>
/// <param name="Names">The players' names in turn order.</param>
/// <param name="LocalSeat">The seat at this screen.</param>
/// <param name="History">The actions played so far, for a player who joins a game under way; empty for a new game.</param>
public sealed record OnlineGameStart(int Seed, HintRules Hints, IReadOnlyList<string> Names, int LocalSeat, IReadOnlyList<LoggedAction> History);

/// <summary>
/// The connection of this screen to the room of an online game. It knows who is in the room and
/// hands over the actions of the other players in order; the rules stay in Core. Every player runs
/// the whole game from the same seed, so the room only has to put the actions in one order.
/// </summary>
public sealed class OnlineClient(IRoomTransport transport, IPreferenceStore store)
{
    /// <summary>The key under which the room is kept, so a refreshed page takes its seat back.</summary>
    internal const string RoomKey = "triominos.room";

    private const int MaxReconnectAttempts = 20;

    private IRoomSocket? _socket;
    private CancellationTokenSource? _keepAlive;
    private string _name = string.Empty;
    private string? _token;

    // How many actions of the game under way this screen has played or been given; the room's log has this many entries when we are up to date.
    private int _delivered;
    private int? _gameSeed;
    private bool _leaving;

    /// <summary>Gets the state of the connection.</summary>
    public OnlineStatus Status { get; private set; }

    /// <summary>Gets the code of the room, or null outside a room.</summary>
    public string? RoomCode { get; private set; }

    /// <summary>Gets the seat at this screen, or null outside a room.</summary>
    public int? LocalSeat { get; private set; }

    /// <summary>Gets the players in the room, in turn order.</summary>
    public IReadOnlyList<LobbyPlayer> Players { get; private set; } = [];

    /// <summary>Gets whether a game is under way in the room.</summary>
    public bool InGame => _gameSeed is not null;

    /// <summary>Gets whether this screen belongs to the host, who starts the games.</summary>
    public bool IsHost => LocalSeat == 0;

    /// <summary>Gets the Dutch text about the last thing that went wrong, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets or sets how long to wait before each attempt to get the connection back; the last one repeats.</summary>
    public IReadOnlyList<TimeSpan> ReconnectDelays { get; set; } = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15)];

    /// <summary>Gets or sets how often the game tells the server it is still there; an idle connection may be closed otherwise.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>Raised when anything the lobby shows has changed.</summary>
    public event Action? Changed;

    /// <summary>Raised when a game starts for this screen: a new game, or a game under way that this screen joins again.</summary>
    public event Action<OnlineGameStart>? GameStarted;

    /// <summary>Raised for an action of another player, in the order the room got them.</summary>
    public event Action<int, GameAction>? ActionReceived;

    /// <summary>Makes a room and joins it as its host.</summary>
    /// <param name="name">The name to play under.</param>
    public async Task CreateRoomAsync(string name)
    {
        if (!Begin(name))
        {
            return;
        }

        try
        {
            var code = await transport.CreateRoomAsync();
            await ConnectAsync(code, token: null);
        }
        catch (RoomUnavailableException)
        {
            Fail("Kan de server niet bereiken. Probeer het later opnieuw.");
        }
    }

    /// <summary>Joins a room that someone else made.</summary>
    /// <param name="code">The code of the room.</param>
    /// <param name="name">The name to play under.</param>
    public async Task JoinRoomAsync(string code, string name)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (cleaned.Length == 0)
        {
            Error = "Vul de code van de kamer in.";
            Changed?.Invoke();
            return;
        }

        if (!Begin(name))
        {
            return;
        }

        try
        {
            await ConnectAsync(cleaned, token: null);
        }
        catch (RoomUnavailableException)
        {
            Fail("Die kamer bestaat niet, of er is geen verbinding. Controleer de code.");
        }
    }

    /// <summary>Takes the seat back in the room of the last visit, if there is one. Quiet when there is none.</summary>
    public async Task TryResumeAsync()
    {
        if (Status != OnlineStatus.Offline)
        {
            return;
        }

        var parts = (await store.GetAsync(RoomKey))?.Split('|', 3);
        if (parts is not { Length: 3 } || parts.Any(string.IsNullOrWhiteSpace))
        {
            return;
        }

        _name = parts[2];
        _token = parts[1];
        _leaving = false;
        Status = OnlineStatus.Connecting;
        Changed?.Invoke();
        try
        {
            await ConnectAsync(parts[0], parts[1]);
        }
        catch (RoomUnavailableException)
        {
            // The room is gone (rooms last a day) or the server is out of reach; either way the old seat is of no use.
            Forget();
            Changed?.Invoke();
        }
    }

    /// <summary>The host deals a new game for everyone in the room.</summary>
    /// <param name="hints">How hints work in the game.</param>
    public async Task StartGameAsync(HintRules hints)
    {
        Error = null;
        await TrySendAsync(new ClientMessage
        {
            T = "start",
            Settings = new SettingsDto { HintsMax = hints.MaxPerPlayer, HintPenalty = hints.Penalty },
        });
    }

    /// <summary>
    /// Tells the others what this screen's player did. The action is already played here, which is
    /// fine because only the player to move can act, so no two actions cross.
    /// </summary>
    /// <param name="action">The action.</param>
    /// <returns>False when it could not be sent.</returns>
    public async Task<bool> SendActionAsync(GameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Status != OnlineStatus.Online || !InGame)
        {
            return false;
        }

        var seen = _delivered++;
        return await TrySendAsync(new ClientMessage { T = "move", N = seen, Move = MoveCodec.ToDto(action) });
    }

    /// <summary>Leaves the room and forgets the seat.</summary>
    public async Task LeaveAsync()
    {
        _leaving = true;
        var socket = _socket;
        _socket = null;
        StopKeepAlive();
        if (socket is not null)
        {
            try
            {
                if (!InGame)
                {
                    await socket.SendAsync(Serialize(new ClientMessage { T = "leave" }));
                }

                await socket.CloseAsync();
                await socket.DisposeAsync();
            }
            catch (Exception exception) when (IsConnectionProblem(exception))
            {
                // Gone already.
            }
        }

        Forget();
        Error = null;
        Changed?.Invoke();
    }

    private static string Serialize(ClientMessage message) => JsonSerializer.Serialize(message, OnlineJson.Default.ClientMessage);

    private static bool IsConnectionProblem(Exception exception) =>
        exception is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or IOException;

    private static string Describe(string? error) => error switch
    {
        "bad_name" => $"Kies een naam van 1 tot {PlayerSetup.MaxNameLength} tekens.",
        "name_taken" => "Die naam is al bezet in deze kamer.",
        "room_full" => $"Deze kamer is vol: hoogstens {Scoring.MaxPlayers} spelers.",
        "already_started" => "Dit spel is al begonnen.",
        "not_host" => "Alleen de gastheer kan het spel starten.",
        "too_few_players" => $"Er zijn minstens {Scoring.MinPlayers} spelers nodig.",
        "out_of_sync" => "Je spel loopt niet meer gelijk met dat van de anderen. Ververs de pagina om weer mee te doen.",
        "too_many_moves" => "Dit spel heeft te veel zetten gehad. Start een nieuw spel.",
        "wrong_version" => "Deze versie van het spel is verouderd. Ververs de pagina.",
        _ => "Er ging iets mis met de verbinding.",
    };

    private bool Begin(string name)
    {
        _name = (name ?? string.Empty).Trim();
        if (_name.Length is 0 or > PlayerSetup.MaxNameLength)
        {
            Error = Describe("bad_name");
            Changed?.Invoke();
            return false;
        }

        _leaving = false;
        Error = null;
        Status = OnlineStatus.Connecting;
        Changed?.Invoke();
        return true;
    }

    private async Task ConnectAsync(string code, string? token)
    {
        var socket = await transport.ConnectAsync(code);
        _socket = socket;
        RoomCode = code;
        socket.MessageReceived += text => OnMessage(socket, text);
        socket.Closed += () => OnClosed(socket);
        StartKeepAlive(socket);
        if (!await TrySendAsync(new ClientMessage { T = "join", V = MoveCodec.ProtocolVersion, Name = _name, Token = token }))
        {
            throw new RoomUnavailableException();
        }
    }

    private async Task<bool> TrySendAsync(ClientMessage message)
    {
        var socket = _socket;
        if (socket is null)
        {
            return false;
        }

        try
        {
            await socket.SendAsync(Serialize(message));
            return true;
        }
        catch (Exception exception) when (IsConnectionProblem(exception))
        {
            // The close event follows and starts the reconnect.
            return false;
        }
    }

    private void OnMessage(IRoomSocket socket, string text)
    {
        if (!ReferenceEquals(socket, _socket) || text == "pong")
        {
            return;
        }

        ServerMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(text, OnlineJson.Default.ServerMessage);
        }
        catch (JsonException)
        {
            return;
        }

        switch (message?.T)
        {
            case "room":
                OnRoom(message);
                break;
            case "players":
                Players = Map(message.Players);
                break;
            case "started":
                OnStarted(message);
                break;
            case "move":
                OnMove(message);
                break;
            case "error":
                OnError(message);
                break;
            default:
                return;
        }

        Changed?.Invoke();
    }

    private static List<LobbyPlayer> Map(List<PlayerDto>? players) =>
        [.. (players ?? []).OrderBy(p => p.Seat).Select(p => new LobbyPlayer(p.Seat, p.Name, p.Connected))];

    private static HintRules HintsOf(SettingsDto? settings) =>
        settings is null ? HintRules.Default : new HintRules(settings.HintsMax, settings.HintPenalty);

    private void OnRoom(ServerMessage message)
    {
        if (message.Seat is not { } seat || message.Token is null)
        {
            return;
        }

        _token = message.Token;
        LocalSeat = seat;
        RoomCode = message.Code ?? RoomCode;
        Players = Map(message.Players);
        Status = OnlineStatus.Online;
        Error = null;
        _ = store.SetAsync(RoomKey, $"{RoomCode}|{_token}|{_name}");

        if (message.Started != true || message.Seed is not { } seed)
        {
            return;
        }

        var log = (message.Log ?? []).Select(e => (e.Seat, Action: MoveCodec.FromDto(e.Move))).ToList();
        if (log.Any(e => e.Action is null))
        {
            Error = Describe("wrong_version");
            return;
        }

        if (_gameSeed == seed && _delivered <= log.Count)
        {
            // The same game as before the connection dropped: only what was missed.
            for (var i = _delivered; i < log.Count; i++)
            {
                if (log[i].Seat != seat)
                {
                    ActionReceived?.Invoke(log[i].Seat, log[i].Action!);
                }
            }

            _delivered = log.Count;
            return;
        }

        // A game this screen has not seen (a refreshed page), or one that went on without actions this screen played.
        _gameSeed = seed;
        _delivered = log.Count;
        GameStarted?.Invoke(new OnlineGameStart(
            seed,
            HintsOf(message.Settings),
            [.. Players.Select(p => p.Name)],
            seat,
            [.. log.Select(e => new LoggedAction(e.Seat, e.Action!))]));
    }

    private void OnStarted(ServerMessage message)
    {
        if (LocalSeat is not { } seat || message.Seed is not { } seed)
        {
            return;
        }

        Players = Map(message.Players);
        Error = null;
        _gameSeed = seed;
        _delivered = 0;
        GameStarted?.Invoke(new OnlineGameStart(seed, HintsOf(message.Settings), [.. Players.Select(p => p.Name)], seat, []));
    }

    private void OnMove(ServerMessage message)
    {
        if (message.N is not { } n || message.Seat is not { } seat || !InGame)
        {
            return;
        }

        if (n < _delivered)
        {
            return;
        }

        if (n > _delivered || MoveCodec.FromDto(message.Move) is not { } action)
        {
            // An action went missing: let the room send the whole game again.
            _ = _socket?.CloseAsync();
            return;
        }

        _delivered++;
        ActionReceived?.Invoke(seat, action);
    }

    private void OnError(ServerMessage message)
    {
        var text = Describe(message.Error);
        if (Status == OnlineStatus.Online)
        {
            Error = text;
            return;
        }

        // The room refused us: the name, a full room, a game that started, an old version.
        Fail(text);
    }

    private void OnClosed(IRoomSocket socket)
    {
        if (!ReferenceEquals(socket, _socket))
        {
            return;
        }

        _socket = null;
        StopKeepAlive();
        if (_leaving || Status == OnlineStatus.Offline)
        {
            return;
        }

        if (Status == OnlineStatus.Connecting)
        {
            Fail("De verbinding met de kamer is verbroken.");
            Changed?.Invoke();
            return;
        }

        Status = OnlineStatus.Reconnecting;
        Changed?.Invoke();
        _ = ReconnectAsync();
    }

    private async Task ReconnectAsync()
    {
        for (var attempt = 0; attempt < MaxReconnectAttempts; attempt++)
        {
            var delay = ReconnectDelays.Count == 0 ? TimeSpan.Zero : ReconnectDelays[Math.Min(attempt, ReconnectDelays.Count - 1)];
            await Task.Delay(delay);
            if (Status != OnlineStatus.Reconnecting || _leaving || RoomCode is null)
            {
                return;
            }

            try
            {
                // The room answers with the whole game, so this screen catches up with what it missed.
                await ConnectAsync(RoomCode, _token);
                return;
            }
            catch (RoomUnavailableException)
            {
                // Try again.
            }
        }

        if (Status == OnlineStatus.Reconnecting)
        {
            Fail("De verbinding met de kamer is verloren.");
            Changed?.Invoke();
        }
    }

    /// <summary>Gives up on the room: remembers why, closes the connection and forgets the seat.</summary>
    private void Fail(string message)
    {
        var socket = _socket;
        _socket = null;
        StopKeepAlive();
        Forget();
        Error = message;
        if (socket is not null)
        {
            _ = CloseQuietlyAsync(socket);
        }

        Changed?.Invoke();
    }

    private void Forget()
    {
        Status = OnlineStatus.Offline;
        RoomCode = null;
        LocalSeat = null;
        Players = [];
        _token = null;
        _gameSeed = null;
        _delivered = 0;
        _ = store.SetAsync(RoomKey, string.Empty);
    }

    private static async Task CloseQuietlyAsync(IRoomSocket socket)
    {
        try
        {
            await socket.CloseAsync();
            await socket.DisposeAsync();
        }
        catch (Exception exception) when (IsConnectionProblem(exception))
        {
            // Gone already.
        }
    }

    private void StartKeepAlive(IRoomSocket socket)
    {
        StopKeepAlive();
        var source = new CancellationTokenSource();
        _keepAlive = source;
        _ = KeepAliveAsync(socket, source.Token);
    }

    private void StopKeepAlive()
    {
        _keepAlive?.Cancel();
        _keepAlive?.Dispose();
        _keepAlive = null;
    }

    private async Task KeepAliveAsync(IRoomSocket socket, CancellationToken cancel)
    {
        try
        {
            using var timer = new PeriodicTimer(KeepAliveInterval);
            while (await timer.WaitForNextTickAsync(cancel))
            {
                await socket.SendAsync("ping");
            }
        }
        catch (Exception exception) when (IsConnectionProblem(exception))
        {
            // The connection ended; its close event deals with that.
        }
    }
}
