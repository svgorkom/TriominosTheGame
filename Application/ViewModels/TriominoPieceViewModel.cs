using Triominos.Models;

namespace Triominos.ViewModels;

/// <summary>
/// ViewModel wrapper for a triomino piece, providing bindable properties
/// </summary>
public class TriominoPieceViewModel(TriominoPiece piece) : ViewModelBase
{
    private bool _isSelected;

    public TriominoPiece Piece { get; } = piece;

    public int Id => Piece.Id;
    public int Value1 => Piece.Value1;
    public int Value2 => Piece.Value2;
    public int Value3 => Piece.Value3;
    public bool IsPointingUp => Piece.IsPointingUp;
    public bool IsTriple => Piece.IsTriple;
    public int PointValue => Piece.PointValue;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void Rotate()
    {
        Piece.Rotate();
        OnPropertyChanged(nameof(Value1));
        OnPropertyChanged(nameof(Value2));
        OnPropertyChanged(nameof(Value3));
    }

    public override string ToString() => Piece.ToString();
}
