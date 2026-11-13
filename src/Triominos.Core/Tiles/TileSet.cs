namespace Triominos.Core.Tiles;

/// <summary>Creates the standard Triominos set.</summary>
public static class TileSet
{
    /// <summary>The number of tiles in the standard set.</summary>
    public const int StandardSize = 56;

    /// <summary>The highest corner value.</summary>
    public const int MaxValue = 5;

    /// <summary>
    /// Creates the 56 tiles of the standard set: every combination of three values from 0 to 5,
    /// with the values ascending clockwise, as printed on the physical tiles.
    /// </summary>
    public static IReadOnlyList<Tile> Standard()
    {
        var tiles = new List<Tile>(StandardSize);
        var id = 0;
        for (var a = 0; a <= MaxValue; a++)
        {
            for (var b = a; b <= MaxValue; b++)
            {
                for (var c = b; c <= MaxValue; c++)
                {
                    tiles.Add(new Tile(id++, a, b, c));
                }
            }
        }

        return tiles;
    }
}
