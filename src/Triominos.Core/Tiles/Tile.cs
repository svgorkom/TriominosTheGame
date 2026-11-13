namespace Triominos.Core.Tiles;

/// <summary>
/// A Triominos tile: three corner values, listed clockwise. Tiles cannot be flipped, so a
/// rotation only shifts which corner holds which value; the clockwise order never changes.
/// </summary>
/// <param name="Id">Unique identifier within a game.</param>
/// <param name="A">First corner value, clockwise.</param>
/// <param name="B">Second corner value, clockwise.</param>
/// <param name="C">Third corner value, clockwise.</param>
public sealed record Tile(int Id, int A, int B, int C)
{
    /// <summary>Gets whether all three corners have the same value.</summary>
    public bool IsTriple => A == B && B == C;

    /// <summary>Gets the point value of the tile: the sum of its corners.</summary>
    public int Value => A + B + C;

    /// <summary>
    /// Gets the value shown at a cell corner when the tile is placed with the given rotation.
    /// </summary>
    /// <param name="rotation">Rotation in steps of 120 degrees, 0 to 2.</param>
    /// <param name="corner">Cell corner index, 0 to 2, clockwise (see <see cref="Board.Cell.Corner"/>).</param>
    public int ValueAtCorner(int rotation, int corner)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rotation);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rotation, 2);
        ArgumentOutOfRangeException.ThrowIfNegative(corner);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(corner, 2);

        return ((corner + rotation) % 3) switch
        {
            0 => A,
            1 => B,
            _ => C,
        };
    }

    /// <inheritdoc />
    public override string ToString() => $"{A}-{B}-{C}";
}
