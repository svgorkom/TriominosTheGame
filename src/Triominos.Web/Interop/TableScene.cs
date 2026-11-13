using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Triominos.Core.Board;
using Triominos.Core.Tiles;

namespace Triominos.Web.Interop;

/// <summary>Rendering quality of the 3D scene; names match the TypeScript <c>Quality</c> type.</summary>
public enum SceneQuality
{
    Low,
    Medium,
    High,
}

/// <summary>A tile as the scene draws it: the cell and the value at each corner (clockwise).</summary>
/// <param name="Row">Cell row.</param>
/// <param name="Col">Cell column.</param>
/// <param name="Values">Values at corners 0, 1 and 2.</param>
public sealed record TileView(int Row, int Col, int[] Values)
{
    /// <summary>Converts a tile on the Core board.</summary>
    public static TileView From(PlacedTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        var cell = tile.Placement.Cell;
        return new TileView(cell.Row, cell.Col, [tile.ValueAtCorner(0), tile.ValueAtCorner(1), tile.ValueAtCorner(2)]);
    }
}

/// <summary>A tile in the rack as the scene draws it.</summary>
/// <param name="Id">Tile id; sent back when the tile is picked up or dropped.</param>
/// <param name="Values">Corner values A, B and C, clockwise.</param>
/// <param name="Marked">True for a tile the rules single out (the allowed starting tiles).</param>
public sealed record RackTileView(int Id, int[] Values, bool Marked);

/// <summary>A cell where the picked-up tile fits, with a rotation that works.</summary>
/// <param name="Row">Cell row.</param>
/// <param name="Col">Cell column.</param>
/// <param name="Rotation">Rotation, 0 to 2.</param>
public sealed record PlacementView(int Row, int Col, int Rotation);

/// <summary>
/// C# side of the Three.js scene in <c>wwwroot/js/scene/scene.js</c>. Calls are event-sized: a few
/// per move, never per frame. The scene calls back when the player picks up a tile (answer: where
/// it fits), drops it, or presses the draw key.
/// </summary>
public sealed class TableScene(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<TableScene>? _self;

    /// <summary>Called when a tile is picked up; returns the placements where it fits.</summary>
    public Func<int, Task<IReadOnlyList<Placement>>>? OnTileSelected { get; set; }

    /// <summary>Called when a tile is dropped on a cell where it fits.</summary>
    public Func<int, Placement, Task>? OnTileDropped { get; set; }

    /// <summary>Called when the player presses the draw key.</summary>
    public Func<Task>? OnDrawRequested { get; set; }

    /// <summary>Loads the scene module and creates the scene on the canvas.</summary>
    public async Task InitAsync(ElementReference canvas, SceneQuality quality)
    {
        _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/scene/scene.js");
        _self = DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("init", canvas, ToJs(quality), _self);
    }

    /// <summary>Shows the tiles of a board; with <paramref name="focus"/>, the camera centres on them.</summary>
    public async Task SetBoardAsync(GameBoard board, bool focus)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (_module is null)
        {
            return;
        }

        var tiles = board.Tiles.Select(TileView.From).ToArray();
        await _module.InvokeVoidAsync("setBoard", tiles, focus);
    }

    /// <summary>
    /// Shows the rack of the player whose turn it is; an empty list hides it. Also puts back a tile
    /// that was being carried.
    /// </summary>
    /// <param name="tiles">The tiles in the rack.</param>
    /// <param name="marked">Tiles to single out (the allowed starting tiles).</param>
    public async Task SetRackAsync(IReadOnlyList<Tile> tiles, IReadOnlyCollection<Tile> marked)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(marked);
        if (_module is null)
        {
            return;
        }

        var views = tiles.Select(t => new RackTileView(t.Id, [t.A, t.B, t.C], marked.Contains(t))).ToArray();
        await _module.InvokeVoidAsync("setRack", views);
    }

    /// <summary>Changes the rendering quality.</summary>
    public async Task SetQualityAsync(SceneQuality quality)
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("setQuality", ToJs(quality));
        }
    }

    /// <summary>Called by the scene when a tile is picked up.</summary>
    [JSInvokable("TileSelected")]
    public async Task<PlacementView[]> HandleTileSelectedAsync(int tileId)
    {
        if (OnTileSelected is null)
        {
            return [];
        }

        var placements = await OnTileSelected(tileId);
        return [.. placements.Select(p => new PlacementView(p.Cell.Row, p.Cell.Col, p.Rotation))];
    }

    /// <summary>Called by the scene when a tile is dropped on a cell where it fits.</summary>
    [JSInvokable("TileDropped")]
    public async Task HandleTileDroppedAsync(int tileId, int row, int col, int rotation)
    {
        if (OnTileDropped is not null)
        {
            await OnTileDropped(tileId, new Placement(new Cell(row, col), rotation));
        }
    }

    /// <summary>Called by the scene when the draw key is pressed.</summary>
    [JSInvokable("DrawRequested")]
    public async Task HandleDrawRequestedAsync()
    {
        if (OnDrawRequested is not null)
        {
            await OnDrawRequested();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _self?.Dispose();
        _self = null;
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("dispose");
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is closing; the browser releases the scene itself.
        }

        _module = null;
    }

    private static string ToJs(SceneQuality quality) => quality.ToString().ToLowerInvariant();
}
