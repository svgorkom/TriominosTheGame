namespace Triominos.UI.Models;

using Triominos.Core.Interfaces;

/// <summary>
/// UI-layer wrapper for a triomino piece.
/// Contains WPF-specific properties while delegating to the core piece.
/// </summary>
public class TriominoPiece
{
    private readonly IPiece _corePiece;

    public int Id => _corePiece.Id;
    public int Value1 => _corePiece.Value1;
    public int Value2 => _corePiece.Value2;
    public int Value3 => _corePiece.Value3;
    public bool IsPointingUp => _corePiece.IsPointingUp;
    public bool IsTriple => _corePiece.IsTriple;
    public int PointValue => _corePiece.PointValue;

    public IPiece CorePiece => _corePiece;

    public TriominoPiece(IPiece corePiece)
    {
        _corePiece = corePiece;
    }

    public TriominoPiece Clone() => new(_corePiece.Clone());

    public void Rotate() => _corePiece.Rotate();

    public override string ToString() => _corePiece.ToString() ?? $"[{Value1}-{Value2}-{Value3}]";

    public override bool Equals(object? obj) => obj is TriominoPiece other && Id == other.Id;
    public override int GetHashCode() => Id.GetHashCode();
}
