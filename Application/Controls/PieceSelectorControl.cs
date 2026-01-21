using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Triominos.ViewModels;

namespace Triominos.Controls;

/// <summary>
/// MVVM-compatible piece selector control for displaying and selecting available triomino pieces
/// </summary>
public class PieceSelectorControl : Border
{
    private readonly WrapPanel _piecesPanel = new() { Orientation = Orientation.Horizontal };

    #region Dependency Properties

    public static readonly DependencyProperty PiecesProperty =
        DependencyProperty.Register(nameof(Pieces), typeof(ObservableCollection<TriominoPieceViewModel>), typeof(PieceSelectorControl),
            new PropertyMetadata(null, OnPiecesChanged));

    public static readonly DependencyProperty SelectedPieceProperty =
        DependencyProperty.Register(nameof(SelectedPiece), typeof(TriominoPieceViewModel), typeof(PieceSelectorControl),
            new PropertyMetadata(null, OnSelectedPieceChanged));

    public static readonly DependencyProperty SelectCommandProperty =
        DependencyProperty.Register(nameof(SelectCommand), typeof(ICommand), typeof(PieceSelectorControl),
            new PropertyMetadata(null, OnSelectCommandChanged));

    public static readonly DependencyProperty PieceSizeProperty =
        DependencyProperty.Register(nameof(PieceSize), typeof(double), typeof(PieceSelectorControl),
            new PropertyMetadata(50.0, OnPieceSizeChanged));

    public ObservableCollection<TriominoPieceViewModel>? Pieces
    {
        get => (ObservableCollection<TriominoPieceViewModel>?)GetValue(PiecesProperty);
        set => SetValue(PiecesProperty, value);
    }

    public TriominoPieceViewModel? SelectedPiece
    {
        get => (TriominoPieceViewModel?)GetValue(SelectedPieceProperty);
        set => SetValue(SelectedPieceProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public double PieceSize
    {
        get => (double)GetValue(PieceSizeProperty);
        set => SetValue(PieceSizeProperty, value);
    }

    #endregion

    public PieceSelectorControl()
    {
        Background = new SolidColorBrush(Color.FromRgb(50, 50, 55));
        BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 90));
        BorderThickness = new Thickness(2);
        CornerRadius = new CornerRadius(5);
        Padding = new Thickness(10);

        Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _piecesPanel
        };
    }

    #region Property Changed Handlers

    private static void OnPiecesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PieceSelectorControl control) return;

        if (e.OldValue is ObservableCollection<TriominoPieceViewModel> oldCollection)
        {
            oldCollection.CollectionChanged -= control.OnPiecesCollectionChanged;
        }

        if (e.NewValue is ObservableCollection<TriominoPieceViewModel> newCollection)
        {
            newCollection.CollectionChanged += control.OnPiecesCollectionChanged;
            control.RefreshPieces();
        }
    }

    private static void OnSelectedPieceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PieceSelectorControl control)
        {
            control.UpdateSelection();
        }
    }

    private static void OnSelectCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PieceSelectorControl control)
        {
            // Update command on all existing piece controls
            foreach (var pieceControl in control._piecesPanel.Children.OfType<TriominoControl>())
            {
                pieceControl.SelectCommand = control.SelectCommand;
            }
        }
    }

    private static void OnPieceSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PieceSelectorControl control)
        {
            control.RefreshPieces();
        }
    }

    #endregion

    private void OnPiecesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems != null:
                int index = e.NewStartingIndex;
                foreach (TriominoPieceViewModel piece in e.NewItems)
                {
                    _piecesPanel.Children.Insert(index++, CreatePieceControl(piece));
                }
                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                foreach (TriominoPieceViewModel piece in e.OldItems)
                {
                    var control = _piecesPanel.Children
                        .OfType<TriominoControl>()
                        .FirstOrDefault(c => c.PieceViewModel == piece);

                    if (control != null)
                    {
                        _piecesPanel.Children.Remove(control);
                    }
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                RefreshPieces();
                break;
        }
    }

    private void RefreshPieces()
    {
        _piecesPanel.Children.Clear();

        if (Pieces == null) return;

        foreach (var piece in Pieces)
        {
            _piecesPanel.Children.Add(CreatePieceControl(piece));
        }
    }

    private TriominoControl CreatePieceControl(TriominoPieceViewModel pieceVm) => new()
    {
        PieceViewModel = pieceVm,
        Piece = pieceVm.Piece,
        PieceSize = PieceSize,
        IsSelectable = true,
        SelectCommand = SelectCommand,
        Margin = new Thickness(5),
        IsHighlighted = pieceVm == SelectedPiece
    };

    private void UpdateSelection()
    {
        foreach (var control in _piecesPanel.Children.OfType<TriominoControl>())
        {
            control.IsHighlighted = control.PieceViewModel == SelectedPiece;
        }
    }
}
