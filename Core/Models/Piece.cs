namespace Triominos.Core.Models;

using Triominos.Core.Interfaces;

/// <summary>
/// Core implementation of a triomino piece.
/// This is UI-agnostic and can be used by any presentation layer.
/// </summary>
public class Piece : IPiece
{
    public int Id { get; }
    public int Value1 { get; private set; }
    public int Value2 { get; private set; }
    public int Value3 { get; private set; }
    public bool IsPointingUp { get; set; } = true;

    public bool IsTriple => Value1 == Value2 && Value2 == Value3;
    public int PointValue => Value1 + Value2 + Value3;

    public Piece(int id, int v1, int v2, int v3)
    {
        Id = id;
        Value1 = v1;
        Value2 = v2;
        Value3 = v3;
    }

    private Piece(Piece other)
    {
        Id = other.Id;
        Value1 = other.Value1;
        Value2 = other.Value2;
        Value3 = other.Value3;
        IsPointingUp = other.IsPointingUp;
    }

    public IPiece Clone() => new Piece(this);

    public void Rotate()
    {
        (Value1, Value2, Value3) = (Value3, Value1, Value2);
    }

    /// <summary>
    /// Gets the edge values for a specific side
    /// </summary>
    public (int val1, int val2) GetEdge(PieceEdge edge)
    {
        return edge switch
        {
            PieceEdge.Right => IsPointingUp ? (Value1, Value2) : (Value2, Value3),
            PieceEdge.Left => (Value3, Value1),
            PieceEdge.Bottom when IsPointingUp => (Value2, Value3),
            PieceEdge.Top when !IsPointingUp => (Value1, Value2),
            _ => throw new InvalidOperationException($"Invalid edge {edge} for piece orientation")
        };
    }

    public override string ToString() => $"[{Value1}-{Value2}-{Value3}]";

    public override bool Equals(object? obj) => obj is Piece other && Id == other.Id;
    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// Represents the edges of a triomino piece
/// </summary>
public enum PieceEdge
{
    Top,    // Only valid for pointing-down triangles
    Bottom, // Only valid for pointing-up triangles
    Left,
    Right
}
