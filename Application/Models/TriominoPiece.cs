namespace Triominos.Models;

/// <summary>
/// Represents a triomino piece with three corner values.
/// For pointing-up (?): Value1=Top, Value2=BottomRight, Value3=BottomLeft
/// For pointing-down (?): Value1=TopLeft, Value2=TopRight, Value3=Bottom
/// </summary>
public class TriominoPiece
{
    public int Id { get; set; }
    public int Value1 { get; set; }
    public int Value2 { get; set; }
    public int Value3 { get; set; }
    public bool IsPointingUp { get; set; } = true;

    public TriominoPiece(int id, int v1, int v2, int v3)
    {
        Id = id;
        Value1 = v1;
        Value2 = v2;
        Value3 = v3;
    }

    /// <summary>
    /// Creates a deep copy of this piece
    /// </summary>
    public TriominoPiece Clone() => new(Id, Value1, Value2, Value3)
    {
        IsPointingUp = IsPointingUp
    };

    /// <summary>
    /// Rotates the piece clockwise by 120 degrees
    /// </summary>
    public void Rotate()
    {
        (Value1, Value2, Value3) = (Value3, Value1, Value2);
    }

    /// <summary>
    /// Gets the edge for a specific side as a tuple of (val1, val2)
    /// </summary>
    public (int val1, int val2) GetEdge(TriominoEdge edge)
    {
        return edge switch
        {
            TriominoEdge.Right => IsPointingUp ? (Value1, Value2) : (Value2, Value3),
            TriominoEdge.Left => (Value3, Value1),
            TriominoEdge.Bottom when IsPointingUp => (Value2, Value3),
            TriominoEdge.Top when !IsPointingUp => (Value1, Value2),
            TriominoEdge.Bottom => throw new InvalidOperationException("Pointing-down triangles have no bottom edge"),
            TriominoEdge.Top => throw new InvalidOperationException("Pointing-up triangles have no top edge"),
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };
    }

    /// <summary>
    /// Checks if two edges match (values must be reversed for adjacent pieces).
    /// Delegates to GameRules for the actual matching logic.
    /// </summary>
    public static bool EdgeMatches((int val1, int val2) edge1, (int val1, int val2) edge2)
        => GameRules.EdgesMatch(edge1, edge2);

    /// <summary>
    /// Returns true if this is a "triple" piece (all values are the same)
    /// </summary>
    public bool IsTriple => Value1 == Value2 && Value2 == Value3;

    /// <summary>
    /// Gets the point value of this piece (sum of all corners).
    /// Delegates to GameRules for consistency.
    /// </summary>
    public int PointValue => GameRules.CalculateBaseScore(this);

    public override string ToString() => $"[{Value1}-{Value2}-{Value3}]";
}

/// <summary>
/// Represents the edges of a triomino piece
/// </summary>
public enum TriominoEdge
{
    Top,    // Only valid for pointing-down triangles
    Bottom, // Only valid for pointing-up triangles
    Left,
    Right
}
