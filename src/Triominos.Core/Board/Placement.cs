using Triominos.Core.Tiles;

namespace Triominos.Core.Board;

/// <summary>Where and how a tile is laid: the cell and the rotation (0 to 2, steps of 120 degrees).</summary>
/// <param name="Cell">Target cell.</param>
/// <param name="Rotation">Rotation, 0 to 2.</param>
public readonly record struct Placement(Cell Cell, int Rotation)
{
    /// <summary>Gets whether the rotation is 0, 1 or 2.</summary>
    public bool HasValidRotation => Rotation is >= 0 and <= 2;
}

/// <summary>A tile lying on the board.</summary>
/// <param name="Tile">The tile.</param>
/// <param name="Placement">Its cell and rotation.</param>
public sealed record PlacedTile(Tile Tile, Placement Placement)
{
    /// <summary>Gets the value shown at a corner of the cell.</summary>
    /// <param name="corner">Corner index, 0 to 2, clockwise.</param>
    public int ValueAtCorner(int corner) => Tile.ValueAtCorner(Placement.Rotation, corner);
}

/// <summary>Bonus shapes completed by a placement.</summary>
/// <param name="Bridge">True when the tile forms a bridge.</param>
/// <param name="Hexagons">Number of hexagons completed, 0 to 3.</param>
public readonly record struct PlacementBonus(bool Bridge, int Hexagons);
