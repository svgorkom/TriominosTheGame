using Microsoft.JSInterop;
using Triominos.Core.Board;

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

/// <summary>
/// C# side of the Three.js scene in <c>wwwroot/js/scene/scene.js</c>. Calls are event-sized: a few
/// per move, never per frame.
/// </summary>
public sealed class TableScene(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    /// <summary>Loads the scene module and creates the scene on the canvas.</summary>
    public async Task InitAsync(Microsoft.AspNetCore.Components.ElementReference canvas, SceneQuality quality)
    {
        _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/scene/scene.js");
        await _module.InvokeVoidAsync("init", canvas, ToJs(quality));
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

    /// <summary>Changes the rendering quality.</summary>
    public async Task SetQualityAsync(SceneQuality quality)
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("setQuality", ToJs(quality));
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
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
