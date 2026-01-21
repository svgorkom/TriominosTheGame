using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Triominos.Models;
using Triominos.ViewModels;

namespace Triominos.Controls;

/// <summary>
/// A visual control representing a triomino piece.
/// Supports both MVVM binding and direct instantiation.
/// </summary>
public class TriominoControl : Canvas
{
    private Polygon? _triangle;

    #region Dependency Properties

    public static readonly DependencyProperty PieceProperty =
        DependencyProperty.Register(nameof(Piece), typeof(TriominoPiece), typeof(TriominoControl),
            new PropertyMetadata(null, OnPieceChanged));

    public static readonly DependencyProperty PieceViewModelProperty =
        DependencyProperty.Register(nameof(PieceViewModel), typeof(TriominoPieceViewModel), typeof(TriominoControl),
            new PropertyMetadata(null, OnPieceViewModelChanged));

    public static readonly DependencyProperty PieceSizeProperty =
        DependencyProperty.Register(nameof(PieceSize), typeof(double), typeof(TriominoControl),
            new PropertyMetadata(60.0, OnSizeChanged));

    public static readonly DependencyProperty IsSelectableProperty =
        DependencyProperty.Register(nameof(IsSelectable), typeof(bool), typeof(TriominoControl),
            new PropertyMetadata(true, OnIsSelectableChanged));

    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(TriominoControl),
            new PropertyMetadata(false, OnIsHighlightedChanged));

    public static readonly DependencyProperty SelectCommandProperty =
        DependencyProperty.Register(nameof(SelectCommand), typeof(ICommand), typeof(TriominoControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ShowNumbersProperty =
        DependencyProperty.Register(nameof(ShowNumbers), typeof(bool), typeof(TriominoControl),
            new PropertyMetadata(true, OnShowNumbersChanged));

    public static readonly DependencyProperty DropToRackCommandProperty =
        DependencyProperty.Register(nameof(DropToRackCommand), typeof(ICommand), typeof(TriominoControl),
            new PropertyMetadata(null));

    public TriominoPiece? Piece
    {
        get => (TriominoPiece?)GetValue(PieceProperty);
        set => SetValue(PieceProperty, value);
    }

    public TriominoPieceViewModel? PieceViewModel
    {
        get => (TriominoPieceViewModel?)GetValue(PieceViewModelProperty);
        set => SetValue(PieceViewModelProperty, value);
    }

    public double PieceSize
    {
        get => (double)GetValue(PieceSizeProperty);
        set => SetValue(PieceSizeProperty, value);
    }

    public bool IsSelectable
    {
        get => (bool)GetValue(IsSelectableProperty);
        set => SetValue(IsSelectableProperty, value);
    }

    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public bool ShowNumbers
    {
        get => (bool)GetValue(ShowNumbersProperty);
        set => SetValue(ShowNumbersProperty, value);
    }

    public ICommand? DropToRackCommand
    {
        get => (ICommand?)GetValue(DropToRackCommandProperty);
        set => SetValue(DropToRackCommandProperty, value);
    }

    #endregion

    public event EventHandler<TriominoPiece>? PieceSelected;

    public TriominoControl()
    {
        Loaded += OnLoaded;
    }

    // Legacy constructor for backward compatibility
    public TriominoControl(TriominoPiece piece, double size = 60, bool isSelectable = true) : this()
    {
        Piece = piece;
        PieceSize = size;
        IsSelectable = isSelectable;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CreateVisual();
        SetupInteraction();
    }

    private static void OnPieceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TriominoControl control && control.IsLoaded)
        {
            control.CreateVisual();
        }
    }

    private static void OnPieceViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TriominoControl control && e.NewValue is TriominoPieceViewModel vm)
        {
            control.Piece = vm.Piece;
        }
    }

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TriominoControl control && control.IsLoaded)
        {
            control.CreateVisual();
        }
    }

    private static void OnIsSelectableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TriominoControl control)
        {
            control.SetupInteraction();
        }
    }

    private static void OnIsHighlightedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TriominoControl control)
        {
            control.UpdateHighlight();
        }
    }

    private static void OnShowNumbersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TriominoControl control && control.IsLoaded)
        {
            control.CreateVisual();
        }
    }

    private void SetupInteraction()
    {
        MouseEnter -= OnMouseEnter;
        MouseLeave -= OnMouseLeave;
        MouseLeftButtonDown -= OnMouseClick;

        if (IsSelectable)
        {
            Cursor = Cursors.Hand;
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;
            MouseLeftButtonDown += OnMouseClick;
        }
        else
        {
            Cursor = Cursors.Arrow;
        }
    }

    private void CreateVisual()
    {
        var piece = Piece;
        if (piece == null) return;

        var size = PieceSize;
        var height = size * 0.866;

        Children.Clear();
        Width = size;
        Height = height;

        var points = piece.IsPointingUp
            ? new PointCollection
            {
                new Point(size / 2, 0),
                new Point(size, height),
                new Point(0, height)
            }
            : new PointCollection
            {
                new Point(0, 0),
                new Point(size, 0),
                new Point(size / 2, height)
            };

        _triangle = new Polygon
        {
            Points = points,
            Fill = GetPieceColor(piece),
            Stroke = Brushes.DarkSlateGray,
            StrokeThickness = 2
        };

        Children.Add(_triangle);
        if (ShowNumbers)
        {
            AddValueLabels(size, height, piece);
        }
        UpdateHighlight();
    }

    private void AddValueLabels(double size, double height, TriominoPiece piece)
    {
        double fontSize = size * 0.22;
        double pullFactor = 0.35;

        double cx, cy;
        (double x, double y)[] vertices;

        if (piece.IsPointingUp)
        {
            cx = size / 2;
            cy = height * 2 / 3;
            vertices = [(size / 2, 0), (size, height), (0, height)];
        }
        else
        {
            cx = size / 2;
            cy = height / 3;
            vertices = [(0, 0), (size, 0), (size / 2, height)];
        }

        int[] values = [piece.Value1, piece.Value2, piece.Value3];
        double[] xOffsets = piece.IsPointingUp
            ? [-fontSize / 2, -fontSize, 0]
            : [0, -fontSize, -fontSize / 2];
        double[] yOffsets = piece.IsPointingUp
            ? [0, -fontSize, -fontSize]
            : [0, 0, -fontSize];

        for (int i = 0; i < 3; i++)
        {
            var label = CreateLabel(values[i].ToString(), fontSize);
            double labelX = vertices[i].x + (cx - vertices[i].x) * pullFactor + xOffsets[i];
            double labelY = vertices[i].y + (cy - vertices[i].y) * pullFactor + yOffsets[i];
            SetLeft(label, labelX);
            SetTop(label, labelY);
            Children.Add(label);
        }
    }

    private static TextBlock CreateLabel(string text, double fontSize) => new()
    {
        Text = text,
        FontSize = fontSize,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White
    };

    private static Brush GetPieceColor(TriominoPiece piece)
    {
        int sum = piece.PointValue;
        return sum switch
        {
            <= 3 => new SolidColorBrush(Color.FromRgb(70, 130, 180)),   // Steel blue
            <= 6 => new SolidColorBrush(Color.FromRgb(60, 179, 113)),   // Medium sea green
            <= 9 => new SolidColorBrush(Color.FromRgb(218, 165, 32)),   // Goldenrod
            <= 12 => new SolidColorBrush(Color.FromRgb(205, 92, 92)),   // Indian red
            _ => new SolidColorBrush(Color.FromRgb(138, 43, 226))       // Blue violet
        };
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (_triangle != null && !IsHighlighted)
        {
            _triangle.StrokeThickness = 3;
            _triangle.Stroke = Brushes.Yellow;
        }
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_triangle != null && !IsHighlighted)
        {
            _triangle.StrokeThickness = 2;
            _triangle.Stroke = Brushes.DarkSlateGray;
        }
    }

    private void OnMouseClick(object sender, MouseButtonEventArgs e)
    {
        // First, check if we should drop a piece to the rack
        // This happens when a pool piece is selected and user clicks on a rack piece
        if (DropToRackCommand?.CanExecute(null) == true)
        {
            DropToRackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Otherwise, normal selection behavior
        if (Piece != null)
        {
            PieceSelected?.Invoke(this, Piece);
        }

        if (SelectCommand?.CanExecute(PieceViewModel) == true)
        {
            SelectCommand.Execute(PieceViewModel);
        }
    }

    private void UpdateHighlight()
    {
        if (_triangle == null) return;

        if (IsHighlighted)
        {
            _triangle.Stroke = Brushes.Lime;
            _triangle.StrokeThickness = 4;
        }
        else
        {
            _triangle.Stroke = Brushes.DarkSlateGray;
            _triangle.StrokeThickness = 2;
        }
    }

    // Legacy method for backward compatibility
    public void SetHighlighted(bool highlighted) => IsHighlighted = highlighted;
}
