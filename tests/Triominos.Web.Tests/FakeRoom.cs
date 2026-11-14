using System.Text.Json;
using Triominos.Web.Online;

namespace Triominos.Web.Tests;

/// <summary>A connection to a room that the test drives by hand.</summary>
internal sealed class FakeSocket : IRoomSocket
{
    public List<string> Sent { get; } = [];

    public bool Disposed { get; private set; }

    public event Action<string>? MessageReceived;

    public event Action? Closed;

    /// <summary>Gets what was sent, without the keep-alive pings.</summary>
    public IEnumerable<ClientMessage> Messages =>
        Sent.Where(text => text != "ping").Select(text => JsonSerializer.Deserialize(text, OnlineJson.Default.ClientMessage)!);

    public Task SendAsync(string text)
    {
        Sent.Add(text);
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        Closed?.Invoke();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>The server says something.</summary>
    public void Receive(string json) => MessageReceived?.Invoke(json);

    /// <summary>The connection drops.</summary>
    public void Drop() => Closed?.Invoke();
}

/// <summary>A server that is only there when the test says so.</summary>
internal sealed class FakeTransport : IRoomTransport
{
    public string Code { get; set; } = "BCDFG";

    public bool Unavailable { get; set; }

    public List<FakeSocket> Sockets { get; } = [];

    public List<string> Connected { get; } = [];

    public FakeSocket Last => Sockets[^1];

    public Task<string> CreateRoomAsync() => Unavailable ? throw new RoomUnavailableException() : Task.FromResult(Code);

    public Task<IRoomSocket> ConnectAsync(string code)
    {
        if (Unavailable)
        {
            throw new RoomUnavailableException();
        }

        var socket = new FakeSocket();
        Sockets.Add(socket);
        Connected.Add(code);
        return Task.FromResult<IRoomSocket>(socket);
    }
}
