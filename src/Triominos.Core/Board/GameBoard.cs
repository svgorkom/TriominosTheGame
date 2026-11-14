using Triominos.Core.Game;
using Triominos.Core.Tiles;

namespace Triominos.Core.Board;

/// <summary>
/// The unbounded board. It stores the placed tiles, the value at every occupied vertex and the
/// empty cells next to placed tiles, so validation and move search stay cheap.
/// </summary>
public sealed class GameBoard
{
    private readonly Dictionary<Cell, PlacedTile> _tiles = [];
    private readonly Dictionary<Vertex, int> _vertexValues = [];
    private readonly HashSet<Cell> _openCells = [];

    /// <summary>Gets the placed tiles, in no particular order.</summary>
    public IReadOnlyCollection<PlacedTile> Tiles => _tiles.Values;

    /// <summary>Gets the empty cells that share an edge with a placed tile.</summary>
    public IReadOnlyCollection<Cell> OpenCells => _openCells;

    /// <summary>Gets whether no tile has been placed yet.</summary>
    public bool IsEmpty => _tiles.Count == 0;

    /// <summary>Gets the tile at a cell, or null.</summary>
    public PlacedTile? TileAt(Cell cell) => _tiles.GetValueOrDefault(cell);

    /// <summary>Gets whether a tile lies on the cell.</summary>
    public bool IsOccupied(Cell cell) => _tiles.ContainsKey(cell);

    /// <summary>Gets the value at a vertex, or null when no tile touches it.</summary>
    public int? ValueAt(Vertex vertex) => _vertexValues.TryGetValue(vertex, out var value) ? value : null;

    /// <summary>
    /// Checks whether a tile may be placed. On an empty board any cell is allowed (the game
    /// restricts the starting move). Otherwise the cell must be empty, share an edge with a placed
    /// tile, and every corner must equal the value already at that vertex, including vertices
    /// shared only diagonally. The board is not changed.
    /// </summary>
    public MoveError Validate(Tile tile, Placement placement)
    {
        ArgumentNullException.ThrowIfNull(tile);

        if (!placement.HasValidRotation)
        {
            return MoveError.InvalidRotation;
        }

        var cell = placement.Cell;
        if (IsOccupied(cell))
        {
            return MoveError.CellOccupied;
        }

        if (IsEmpty)
        {
            return MoveError.None;
        }

        if (!_openCells.Contains(cell))
        {
            return MoveError.NotAdjacent;
        }

        for (var corner = 0; corner < 3; corner++)
        {
            var existing = ValueAt(cell.Corner(corner));
            if (existing is not null && existing != tile.ValueAtCorner(placement.Rotation, corner))
            {
                return MoveError.CornerMismatch;
            }
        }

        return MoveError.None;
    }

    /// <summary>
    /// Gets every valid placement of a tile next to the placed tiles. Returns nothing for an empty
    /// board; the starting move is decided by the game.
    /// </summary>
    public IReadOnlyList<Placement> ValidPlacements(Tile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        var result = new List<Placement>();
        if (IsEmpty)
        {
            return result;
        }

        foreach (var cell in _openCells)
        {
            for (var rotation = 0; rotation < 3; rotation++)
            {
                var placement = new Placement(cell, rotation);
                if (Validate(tile, placement) == MoveError.None)
                {
                    result.Add(placement);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Evaluates the bonus shapes a placement into <paramref name="cell"/> would complete. Call it
    /// before placing, on a cell that passed <see cref="Validate"/>.
    /// </summary>
    /// <remarks>
    /// Bridge: the tile shares exactly one edge with placed tiles, and the corner opposite that edge
    /// already touches another tile. Hexagon: a corner of the tile is the centre of six cells, and
    /// this tile fills the last of them.
    /// </remarks>
    public PlacementBonus EvaluateBonus(Cell cell)
    {
        var sharedEdges = 0;
        var sharedEdge = -1;
        for (var edge = 0; edge < 3; edge++)
        {
            if (IsOccupied(cell.Neighbor(edge)))
            {
                sharedEdges++;
                sharedEdge = edge;
            }
        }

        var bridge = sharedEdges == 1 && ValueAt(cell.Corner((sharedEdge + 2) % 3)) is not null;

        return new PlacementBonus(bridge, HexagonCenters(cell).Count);
    }

    /// <summary>
    /// Returns the corners of a cell that are the centre of a full hexagon: all six cells around
    /// them are occupied, counting <paramref name="cell"/> itself as occupied. Before a placement
    /// these are the hexagons the tile would complete; after it, the ones it did complete.
    /// </summary>
    /// <param name="cell">The cell of the tile.</param>
    public IReadOnlyList<Vertex> HexagonCenters(Cell cell)
    {
        var centers = new List<Vertex>();
        for (var corner = 0; corner < 3; corner++)
        {
            var vertex = cell.Corner(corner);
            if (vertex.Cells().All(c => c == cell || IsOccupied(c)))
            {
                centers.Add(vertex);
            }
        }

        return centers;
    }

    /// <summary>
    /// Copies the board, so moves can be tried on the copy without touching the game. The copy
    /// shares only the immutable tiles with the original.
    /// </summary>
    internal GameBoard Clone()
    {
        var copy = new GameBoard();
        foreach (var (cell, tile) in _tiles)
        {
            copy._tiles.Add(cell, tile);
        }

        foreach (var (vertex, value) in _vertexValues)
        {
            copy._vertexValues.Add(vertex, value);
        }

        copy._openCells.UnionWith(_openCells);
        return copy;
    }

    /// <summary>Places a tile. Throws when the placement is not valid.</summary>
    internal PlacedTile Place(Tile tile, Placement placement)
    {
        var error = Validate(tile, placement);
        if (error != MoveError.None)
        {
            throw new InvalidOperationException($"Invalid placement of {tile} at {placement}: {error}.");
        }

        var placed = new PlacedTile(tile, placement);
        var cell = placement.Cell;
        _tiles.Add(cell, placed);
        _openCells.Remove(cell);

        for (var corner = 0; corner < 3; corner++)
        {
            _vertexValues[cell.Corner(corner)] = placed.ValueAtCorner(corner);
        }

        for (var edge = 0; edge < 3; edge++)
        {
            var neighbor = cell.Neighbor(edge);
            if (!IsOccupied(neighbor))
            {
                _openCells.Add(neighbor);
            }
        }

        return placed;
    }
}
