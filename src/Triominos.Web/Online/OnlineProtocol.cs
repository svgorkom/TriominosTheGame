using System.Text.Json.Serialization;
using Triominos.Core.Board;
using Triominos.Core.Game;

namespace Triominos.Web.Online;

// The messages between the game and the room on the server (server/src/room.ts holds the other
// half). The server knows no rules; it hands out seats and puts the actions in one order.

/// <summary>An action as it travels: <c>place</c> (with a tile, a cell and a rotation), <c>draw</c>, <c>pass</c> or <c>hint</c>.</summary>
public sealed class MoveDto
{
    /// <summary>Gets or sets the kind of action.</summary>
    public string K { get; set; } = string.Empty;

    /// <summary>Gets or sets the tile of a <c>place</c>.</summary>
    public int? Tile { get; set; }

    /// <summary>Gets or sets the cell row of a <c>place</c>.</summary>
    public int? Row { get; set; }

    /// <summary>Gets or sets the cell column of a <c>place</c>.</summary>
    public int? Col { get; set; }

    /// <summary>Gets or sets the rotation of a <c>place</c>.</summary>
    public int? Rot { get; set; }
}

/// <summary>How hints work in the game; the host chooses.</summary>
public sealed class SettingsDto
{
    /// <summary>Gets or sets the hints each player may take.</summary>
    public int HintsMax { get; set; }

    /// <summary>Gets or sets what a hint costs.</summary>
    public int HintPenalty { get; set; }
}

/// <summary>A player in a room.</summary>
public sealed class PlayerDto
{
    /// <summary>Gets or sets the seat, which is the position in the turn order.</summary>
    public int Seat { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the player is connected now.</summary>
    public bool Connected { get; set; }
}

/// <summary>An action in the log of a game, with the seat that gave it.</summary>
public sealed class LogEntryDto
{
    /// <summary>Gets or sets the seat.</summary>
    public int Seat { get; set; }

    /// <summary>Gets or sets the action.</summary>
    public MoveDto Move { get; set; } = new();
}

/// <summary>What the server sends.</summary>
public sealed class ServerMessage
{
    /// <summary>Gets or sets the type: <c>room</c>, <c>players</c>, <c>started</c>, <c>move</c> or <c>error</c>.</summary>
    public string T { get; set; } = string.Empty;

    /// <summary>Gets or sets the room code (<c>room</c>).</summary>
    public string? Code { get; set; }

    /// <summary>Gets or sets the seat of the receiver (<c>room</c>).</summary>
    public int? Seat { get; set; }

    /// <summary>Gets or sets the token that gives the seat back after a lost connection (<c>room</c>).</summary>
    public string? Token { get; set; }

    /// <summary>Gets or sets the players (<c>room</c>, <c>players</c>, <c>started</c>).</summary>
    public List<PlayerDto>? Players { get; set; }

    /// <summary>Gets or sets whether a game is under way (<c>room</c>).</summary>
    public bool? Started { get; set; }

    /// <summary>Gets or sets the seed of the deal (<c>room</c>, <c>started</c>).</summary>
    public int? Seed { get; set; }

    /// <summary>Gets or sets how hints work (<c>room</c>, <c>started</c>).</summary>
    public SettingsDto? Settings { get; set; }

    /// <summary>Gets or sets every action of the game so far (<c>room</c>).</summary>
    public List<LogEntryDto>? Log { get; set; }

    /// <summary>Gets or sets the number of the action in the log (<c>move</c>).</summary>
    public int? N { get; set; }

    /// <summary>Gets or sets the action (<c>move</c>).</summary>
    public MoveDto? Move { get; set; }

    /// <summary>Gets or sets the error code (<c>error</c>).</summary>
    public string? Error { get; set; }
}

/// <summary>What the game sends: <c>join</c>, <c>start</c>, <c>move</c> or <c>leave</c>.</summary>
public sealed class ClientMessage
{
    /// <summary>Gets or sets the type.</summary>
    public string T { get; set; } = string.Empty;

    /// <summary>Gets or sets the version of this protocol (<c>join</c>).</summary>
    public int? V { get; set; }

    /// <summary>Gets or sets the name (<c>join</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the token of a seat that was taken before (<c>join</c>).</summary>
    public string? Token { get; set; }

    /// <summary>Gets or sets how hints work (<c>start</c>).</summary>
    public SettingsDto? Settings { get; set; }

    /// <summary>Gets or sets how many actions the sender has seen so far (<c>move</c>).</summary>
    public int? N { get; set; }

    /// <summary>Gets or sets the action (<c>move</c>).</summary>
    public MoveDto? Move { get; set; }
}

/// <summary>The answer to creating a room.</summary>
public sealed class CreateRoomResponse
{
    /// <summary>Gets or sets the code of the new room.</summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>The JSON of the protocol, generated at build time so trimming cannot remove what it needs.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ServerMessage))]
[JsonSerializable(typeof(ClientMessage))]
[JsonSerializable(typeof(CreateRoomResponse))]
internal sealed partial class OnlineJson : JsonSerializerContext;

/// <summary>Turns game actions into what travels and back.</summary>
public static class MoveCodec
{
    /// <summary>Gets the version of the protocol; the server refuses a different one.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Returns the action as it travels.</summary>
    /// <param name="action">The action.</param>
    public static MoveDto ToDto(GameAction action) => action switch
    {
        PlaceAction place => new MoveDto
        {
            K = "place",
            Tile = place.TileId,
            Row = place.Placement.Cell.Row,
            Col = place.Placement.Cell.Col,
            Rot = place.Placement.Rotation,
        },
        DrawAction => new MoveDto { K = "draw" },
        PassAction => new MoveDto { K = "pass" },
        HintAction => new MoveDto { K = "hint" },
        _ => throw new ArgumentException($"Unknown action {action}.", nameof(action)),
    };

    /// <summary>Returns the action that travelled, or null when it is not one.</summary>
    /// <param name="move">What was received.</param>
    public static GameAction? FromDto(MoveDto? move) => move switch
    {
        { K: "place", Tile: { } tile, Row: { } row, Col: { } col, Rot: { } rot } => new PlaceAction(tile, new Placement(new Cell(row, col), rot)),
        { K: "draw" } => new DrawAction(),
        { K: "pass" } => new PassAction(),
        { K: "hint" } => new HintAction(),
        _ => null,
    };
}
