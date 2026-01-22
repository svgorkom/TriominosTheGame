using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Triominos.UI.ViewModels;

namespace Triominos.UI.Controls;

/// <summary>
/// MVVM-compatible game board control for displaying and interacting with the triangular grid
/// </summary>
public class GameBoardControl : Canvas
{
    private readonly Dictionary<(int row, int col), Polygon> _gridCells = new();

    #region Dependency Properties

    public static readonly DependencyProperty GridRowsProperty =
        DependencyProperty.Register(nameof(GridRows), typeof(int), typeof(GameBoardControl),
            new PropertyMetadata(12, OnGridSizeChanged));

    public static readonly DependencyProperty GridColsProperty =
        DependencyProperty.Register(nameof(GridCols), typeof(int), typeof(GameBoardControl),
            new PropertyMetadata(24, OnGridSizeChanged));

    public static readonly DependencyProperty CellSizeProperty =
        DependencyProperty.Register(nameof(CellSize), typeof(double), typeof(GameBoardControl),
            new PropertyMetadata(50.0, OnGridSizeChanged));

    public static readonly DependencyProperty PlacedPiecesProperty =
        DependencyProperty.Register(nameof(PlacedPieces), typeof(ObservableCollection<PlacedPieceViewModel>), typeof(GameBoardControl),
            new PropertyMetadata(null, OnPlacedPiecesChanged));

    public static readonly DependencyProperty ValidPlacementsProperty =
        DependencyProperty.Register(nameof(ValidPlacements), typeof(ObservableCollection<GridPosition>), typeof(GameBoardControl),
            new PropertyMetadata(null, OnValidPlacementsChanged));

    public static readonly DependencyProperty HoveredPositionProperty =
        DependencyProperty.Register(nameof(HoveredPosition), typeof(GridPosition), typeof(GameBoardControl),
            new PropertyMetadata(null, OnHoveredPositionChanged));

    public static readonly DependencyProperty CellClickCommandProperty =
        DependencyProperty.Register(nameof(CellClickCommand), typeof(ICommand), typeof(GameBoardControl),
            new PropertyMetadata(null));

    public int GridRows
    {
        get => (int)GetValue(GridRowsProperty);
        set => SetValue(GridRowsProperty, value);
    }

    public int GridCols
    {
        get => (int)GetValue(GridColsProperty);
        set => SetValue(GridColsProperty, value);
    }

    public double CellSize
    {
        get => (double)GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    public ObservableCollection<PlacedPieceViewModel>? PlacedPieces
    {
        get => (ObservableCollection<PlacedPieceViewModel>?)GetValue(PlacedPiecesProperty);
        set => SetValue(PlacedPiecesProperty, value);
    }

    public ObservableCollection<GridPosition>? ValidPlacements
    {
        get => (ObservableCollection<GridPosition>?)GetValue(ValidPlacementsProperty);
        set => SetValue(ValidPlacementsProperty, value);
    }

    public GridPosition? HoveredPosition
    {
        get => (GridPosition?)GetValue(HoveredPositionProperty);
        set => SetValue(HoveredPositionProperty, value);
    }

    public ICommand? CellClickCommand
    {
        get => (ICommand?)GetValue(CellClickCommandProperty);
        set => SetValue(CellClickCommandProperty, value);
    }

    #endregion

    public GameBoardControl()
    {
        Background = new SolidColorBrush(Color.FromRgb(40, 40, 45));
        ClipToBounds = true;

        Loaded += (_, _) => InitializeBoard();
        MouseLeftButtonDown += OnBoardClick;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => HoveredPosition = null;
    }

    #region Property Changed Handlers

    private static void OnGridSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GameBoardControl { IsLoaded: true } control)
        {
            control.InitializeBoard();
        }
    }

    private static void OnPlacedPiecesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GameBoardControl control) return;

        if (e.OldValue is ObservableCollection<PlacedPieceViewModel> oldCollection)
        {
            oldCollection.CollectionChanged -= control.OnPlacedPiecesCollectionChanged;
        }

        if (e.NewValue is ObservableCollection<PlacedPieceViewModel> newCollection)
        {
            newCollection.CollectionChanged += control.OnPlacedPiecesCollectionChanged;
            control.RefreshPlacedPieces();
        }
    }

    private static void OnValidPlacementsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GameBoardControl control) return;

        if (e.OldValue is ObservableCollection<GridPosition> oldCollection)
        {
            oldCollection.CollectionChanged -= control.OnValidPlacementsCollectionChanged;
        }

        if (e.NewValue is ObservableCollection<GridPosition> newCollection)
        {
            newCollection.CollectionChanged += control.OnValidPlacementsCollectionChanged;
            control.UpdateHighlights();
        }
    }

    private static void OnHoveredPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GameBoardControl control)
        {
            control.UpdateHighlights();
        }
    }

    #endregion

    #region Collection Changed Handlers

    private void OnPlacedPiecesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (PlacedPieceViewModel piece in e.NewItems)
            {
                AddPieceVisual(piece);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RefreshPlacedPieces();
        }
    }

    private void OnValidPlacementsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateHighlights();
    }

    #endregion

    #region Board Rendering

    private void InitializeBoard()
    {
        Width = GridCols * CellSize / 2 + CellSize / 2;
        Height = GridRows * CellSize * 0.866;

        Children.Clear();
        _gridCells.Clear();

        DrawGrid();
        RefreshPlacedPieces();
        UpdateHighlights();
    }

    private void DrawGrid()
    {
        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                bool isPointingUp = (row + col) % 2 == 0;
                Polygon triangle = CreateGridTriangle(row, col, isPointingUp);
                _gridCells[(row, col)] = triangle;
                Children.Add(triangle);
            }
        }
    }

    private Polygon CreateGridTriangle(int row, int col, bool isPointingUp)
    {
        double x = col * CellSize / 2;
        double y = row * CellSize * 0.866;
        double height = CellSize * 0.866;

        PointCollection points = isPointingUp
            ? new PointCollection
            {
                new Point(x + CellSize / 2, y),
                new Point(x + CellSize, y + height),
                new Point(x, y + height)
            }
            : new PointCollection
            {
                new Point(x, y),
                new Point(x + CellSize, y),
                new Point(x + CellSize / 2, y + height)
            };

        return new Polygon
        {
            Points = points,
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromRgb(70, 70, 80)),
            StrokeThickness = 1
        };
    }

    private void RefreshPlacedPieces()
    {
        foreach (TriominoControl piece in Children.OfType<TriominoControl>().ToList())
        {
            Children.Remove(piece);
        }

        if (PlacedPieces == null) return;

        foreach (PlacedPieceViewModel piece in PlacedPieces)
        {
            AddPieceVisual(piece);
        }
    }

    private void AddPieceVisual(PlacedPieceViewModel placedPiece)
    {
        // The piece already has the correct orientation from the core model
        var control = new TriominoControl
        {
            Piece = placedPiece.Piece,
            PieceSize = CellSize,
            IsSelectable = false
        };

        SetLeft(control, placedPiece.X);
        SetTop(control, placedPiece.Y);
        Children.Add(control);
    }

    private void UpdateHighlights()
    {
        HashSet<GridPosition> validSet = ValidPlacements?.ToHashSet() ?? [];

        foreach (((int row, int col), Polygon? polygon) in _gridCells)
        {
            var gridPos = new GridPosition(row, col);
            bool isValid = validSet.Contains(gridPos);
            bool isHovered = HoveredPosition == gridPos;

            polygon.Fill = (isValid, isHovered) switch
            {
                (true, true) => new SolidColorBrush(Color.FromArgb(120, 100, 255, 100)),
                (true, false) => new SolidColorBrush(Color.FromArgb(60, 100, 255, 100)),
                (false, true) => new SolidColorBrush(Color.FromArgb(80, 255, 100, 100)),
                _ => Brushes.Transparent
            };
        }
    }

    #endregion

    #region Mouse Interaction

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point pos = e.GetPosition(this);
        (int row, int col) = GetGridPosition(pos);

        if (row >= 0 && row < GridRows && col >= 0 && col < GridCols)
        {
            HoveredPosition = new GridPosition(row, col);
        }
    }

    private void OnBoardClick(object sender, MouseButtonEventArgs e)
    {
        Point pos = e.GetPosition(this);
        (int row, int col) = GetGridPosition(pos);

        if (row < 0 || row >= GridRows || col < 0 || col >= GridCols)
            return;

        var position = new GridPosition(row, col);
        if (CellClickCommand?.CanExecute(position) == true)
        {
            CellClickCommand.Execute(position);
        }
    }

    private (int row, int col) GetGridPosition(Point pos)
    {
        double height = CellSize * 0.866;

        int row = (int)(pos.Y / height);
        int col = (int)(pos.X / (CellSize / 2));

        row = Math.Clamp(row, 0, GridRows - 1);
        col = Math.Clamp(col, 0, GridCols - 1);

        // Refine position based on triangle diagonal edges
        double localX = pos.X - (col * CellSize / 2);
        double localY = pos.Y - (row * height);
        bool shouldBeUp = (row + col) % 2 == 0;

        if (shouldBeUp)
        {
            double centerX = CellSize / 2;

            if (localX < centerX)
            {
                // Left edge diagonal
                double edgeY = height - (localX / centerX) * height;
                if (localY < edgeY && col > 0)
                    col--;
            }
            else
            {
                // Right edge diagonal
                double edgeY = ((localX - centerX) / centerX) * height;
                if (localY < edgeY && col < GridCols - 1)
                    col++;
            }
        }

        return (row, Math.Clamp(col, 0, GridCols - 1));
    }

    #endregion
}
