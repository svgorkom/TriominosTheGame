using System.Collections.ObjectModel;
using System.Windows.Input;
using Triominos.Models;

namespace Triominos.ViewModels;

/// <summary>
/// Main ViewModel for the Triominos game
/// </summary>
public class GameViewModel : ViewModelBase
{
    private readonly GameState _gameState = new();
    private TriominoPieceViewModel? _selectedPiece;
    private string _statusMessage = "Select a piece from the right panel to begin";
    private string _selectionHint = "Click a piece to select it, then click on the board to place it";
    private GridPosition? _hoveredPosition;

    public GameViewModel()
    {
        AvailablePieces = [];
        PlacedPieces = [];
        ValidPlacements = [];

        SelectPieceCommand = new RelayCommand(OnSelectPiece);
        PlacePieceCommand = new RelayCommand(OnPlacePiece, CanPlacePiece);
        RotateCommand = new RelayCommand(OnRotate, () => SelectedPiece != null);
        ResetGameCommand = new RelayCommand(OnResetGame);
        ClearBoardCommand = new RelayCommand(OnClearBoard);

        GeneratePieces();
        UpdateHints();
    }

    #region Properties

    public ObservableCollection<TriominoPieceViewModel> AvailablePieces { get; }
    public ObservableCollection<PlacedPieceViewModel> PlacedPieces { get; }
    public ObservableCollection<GridPosition> ValidPlacements { get; }

    public TriominoPieceViewModel? SelectedPiece
    {
        get => _selectedPiece;
        private set
        {
            if (_selectedPiece != null)
                _selectedPiece.IsSelected = false;

            if (SetProperty(ref _selectedPiece, value))
            {
                if (_selectedPiece != null)
                    _selectedPiece.IsSelected = true;

                OnPropertyChanged(nameof(SelectedPieceText));
                OnPropertyChanged(nameof(HasSelectedPiece));
                UpdateValidPlacements();
                UpdateHints();
            }
        }
    }

    public bool HasSelectedPiece => _selectedPiece != null;

    public string SelectedPieceText => _selectedPiece != null
        ? $"Selected: {_selectedPiece}"
        : "Selected: None";

    public int Score => _gameState.Score;
    public int PiecesPlacedCount => _gameState.PiecesPlaced;
    public bool IsFirstMove => _gameState.IsFirstMove;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SelectionHint
    {
        get => _selectionHint;
        private set => SetProperty(ref _selectionHint, value);
    }

    public GridPosition? HoveredPosition
    {
        get => _hoveredPosition;
        set => SetProperty(ref _hoveredPosition, value);
    }

    public int GridRows => 12;
    public int GridCols => 24;
    public double CellSize => 50;

    #endregion

    #region Commands

    public ICommand SelectPieceCommand { get; }
    public ICommand PlacePieceCommand { get; }
    public ICommand RotateCommand { get; }
    public ICommand ResetGameCommand { get; }
    public ICommand ClearBoardCommand { get; }

    #endregion

    #region Command Handlers

    private void OnSelectPiece(object? parameter)
    {
        if (parameter is not TriominoPieceViewModel piece)
            return;

        if (SelectedPiece == piece)
        {
            SelectedPiece = null;
            StatusMessage = "Select a piece from the right panel";
        }
        else
        {
            SelectedPiece = piece;
            StatusMessage = IsFirstMove
                ? "First move! Place your piece anywhere on the board"
                : "Click on a highlighted cell to place the piece (green = valid)";
        }
    }

    private bool CanPlacePiece(object? parameter) => SelectedPiece != null;

    private void OnPlacePiece(object? parameter)
    {
        if (parameter is not GridPosition position || SelectedPiece == null)
            return;

        var piece = SelectedPiece.Piece;
        PlacementResult? result = null;

        // Try all rotations to find one that works
        for (int rotation = 0; rotation < 3; rotation++)
        {
            // Clone fresh for each attempt since TryPlacePiece modifies the piece
            var pieceToPlace = piece.Clone();
            
            // Apply rotations
            for (int r = 0; r < rotation; r++)
            {
                pieceToPlace.Rotate();
            }
            
            result = _gameState.TryPlacePiece(pieceToPlace, position.Row, position.Col);

            if (result.Success)
            {
                // Use the successfully placed piece for the visual
                var placedVm = new PlacedPieceViewModel(pieceToPlace, position.Row, position.Col, CellSize);
                PlacedPieces.Add(placedVm);
                AvailablePieces.Remove(SelectedPiece);

                StatusMessage = $"? {result.Message}";
                SelectedPiece = null;

                OnPropertyChanged(nameof(Score));
                OnPropertyChanged(nameof(PiecesPlacedCount));
                OnPropertyChanged(nameof(IsFirstMove));
                UpdateHints();
                return;
            }
        }

        // All rotations failed
        StatusMessage = $"? {result?.Message ?? "Cannot place piece here"}";
        if (!IsFirstMove)
        {
            StatusMessage += " - Try rotating the piece or selecting a different position";
        }
    }

    private void OnRotate()
    {
        if (SelectedPiece == null)
        {
            StatusMessage = "? Select a piece first to rotate it";
            return;
        }

        SelectedPiece.Rotate();
        StatusMessage = $"? Rotated piece to {SelectedPiece}";
        OnPropertyChanged(nameof(SelectedPieceText));
        UpdateValidPlacements();
    }

    private void OnResetGame()
    {
        _gameState.Clear();
        PlacedPieces.Clear();
        AvailablePieces.Clear();
        SelectedPiece = null;
        GeneratePieces();

        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(PiecesPlacedCount));
        OnPropertyChanged(nameof(IsFirstMove));

        StatusMessage = "Game reset - select a piece to begin";
        UpdateHints();
    }

    private void OnClearBoard()
    {
        OnResetGame();
        StatusMessage = "Board cleared - select a piece to begin";
    }

    #endregion

    #region Private Methods

    private void GeneratePieces()
    {
        int id = 0;
        for (int i = 0; i <= 5; i++)
        {
            for (int j = i; j <= 5; j++)
            {
                for (int k = j; k <= 5; k++)
                {
                    var piece = new TriominoPiece(id++, i, j, k);
                    AvailablePieces.Add(new TriominoPieceViewModel(piece));
                }
            }
        }
    }

    private void UpdateValidPlacements()
    {
        ValidPlacements.Clear();

        if (SelectedPiece == null)
            return;

        foreach (var (row, col) in _gameState.GetValidPlacements(SelectedPiece.Piece, GridRows, GridCols))
        {
            ValidPlacements.Add(new GridPosition(row, col));
        }
    }

    private void UpdateHints()
    {
        SelectionHint = (IsFirstMove, SelectedPiece) switch
        {
            (true, _) => "Select any piece to start. Triple pieces (e.g., 5-5-5) give bonus points on the first move!",
            (false, not null) => "Place on a green cell. Edges must match adjacent pieces. Use Rotate to change orientation.",
            _ => "Click a piece to select it. Match two edge values with adjacent pieces to place it."
        };
    }

    #endregion
}

/// <summary>
/// Represents a grid position on the game board
/// </summary>
public record GridPosition(int Row, int Col);

/// <summary>
/// ViewModel for a placed piece on the board
/// </summary>
public class PlacedPieceViewModel(TriominoPiece piece, int row, int col, double cellSize) : ViewModelBase
{
    public TriominoPiece Piece { get; } = piece;
    public int Row { get; } = row;
    public int Col { get; } = col;
    public double CellSize { get; } = cellSize;
    public double X { get; } = col * cellSize / 2;
    public double Y { get; } = row * cellSize * 0.866;
    public bool IsPointingUp => (Row + Col) % 2 == 0;
}
