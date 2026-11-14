using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Components;

namespace Triominos.Web.Online;

/// <summary>The server cannot be reached or refused the request.</summary>
public sealed class RoomUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RoomUnavailableException"/> class.</summary>
    public RoomUnavailableException()
        : base("The room server is not available.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RoomUnavailableException"/> class.</summary>
    /// <param name="inner">What went wrong underneath.</param>
    public RoomUnavailableException(Exception inner)
        : base("The room server is not available.", inner)
    {
    }
}

/// <summary>An open connection to a room.</summary>
public interface IRoomSocket : IAsyncDisposable
{
    /// <summary>Raised for every text the server sends.</summary>
    event Action<string>? MessageReceived;

    /// <summary>Raised once when the connection ends, whoever ended it.</summary>
    event Action? Closed;

    /// <summary>Sends a text.</summary>
    /// <param name="text">The text.</param>
    Task SendAsync(string text);

    /// <summary>Closes the connection.</summary>
    Task CloseAsync();
}

/// <summary>How the game reaches the server: making a room and connecting to one.</summary>
public interface IRoomTransport
{
    /// <summary>Makes a room and returns its code.</summary>
    /// <exception cref="RoomUnavailableException">The server cannot be reached.</exception>
    Task<string> CreateRoomAsync();

    /// <summary>Connects to a room.</summary>
    /// <param name="code">The room code.</param>
    /// <exception cref="RoomUnavailableException">No connection could be made, for instance because the room does not exist.</exception>
    Task<IRoomSocket> ConnectAsync(string code);
}

/// <summary>Talks to the Worker that serves the site, over the same origin: <c>POST api/rooms</c> and a WebSocket on <c>ws/CODE</c>.</summary>
public sealed class BrowserRoomTransport(HttpClient http, NavigationManager navigation) : IRoomTransport
{
    /// <inheritdoc />
    public async Task<string> CreateRoomAsync()
    {
        try
        {
            using var response = await http.PostAsync("api/rooms", content: null);
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync(OnlineJson.Default.CreateRoomResponse);
            return string.IsNullOrEmpty(created?.Code) ? throw new RoomUnavailableException() : created.Code;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
        {
            throw new RoomUnavailableException(exception);
        }
    }

    /// <inheritdoc />
    public async Task<IRoomSocket> ConnectAsync(string code)
    {
        var uri = new UriBuilder(new Uri(new Uri(navigation.BaseUri), $"ws/{Uri.EscapeDataString(code)}"));
        uri.Scheme = uri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(uri.Uri, CancellationToken.None);
        }
        catch (Exception exception) when (exception is WebSocketException or HttpRequestException or InvalidOperationException)
        {
            socket.Dispose();
            throw new RoomUnavailableException(exception);
        }

        return new BrowserRoomSocket(socket);
    }
}

/// <summary>A WebSocket in the browser. It reads on its own until the connection ends.</summary>
internal sealed class BrowserRoomSocket : IRoomSocket
{
    private readonly ClientWebSocket _socket;
    private readonly CancellationTokenSource _stop = new();
    private bool _closedRaised;

    public BrowserRoomSocket(ClientWebSocket socket)
    {
        _socket = socket;
        _ = ReadAsync();
    }

    public event Action<string>? MessageReceived;

    public event Action? Closed;

    public Task SendAsync(string text) =>
        _socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, _stop.Token);

    public async Task CloseAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // Already gone.
        }
        finally
        {
            RaiseClosed();
        }
    }

    public ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _socket.Dispose();
        _stop.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task ReadAsync()
    {
        // A room that is playing sends the whole log at once, so a message can be larger than the buffer.
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                var received = await _socket.ReceiveAsync(buffer, _stop.Token);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Write(buffer, 0, received.Count);
                if (received.EndOfMessage)
                {
                    var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    message.SetLength(0);
                    MessageReceived?.Invoke(text);
                }
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The connection ended; the same as a close.
        }

        RaiseClosed();
    }

    private void RaiseClosed()
    {
        if (!_closedRaised)
        {
            _closedRaised = true;
            Closed?.Invoke();
        }
    }
}
