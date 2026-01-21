using System.Collections.ObjectModel;
using System.Windows.Input;
using Triominos.Models;

namespace Triominos.ViewModels;

/// <summary>
/// Game phase enumeration
/// </summary>
public enum GamePhase
{
    Setup,      // Selecting number of players
    Playing,    // Game in progress
    GameOver    // Game ended
}

/// <summary>
/// Main ViewModel for the Triominos game
/// </summary>
public class GameViewModel : ViewModelBase
{
    private readonly GameState _gameState = new();
    private readonly Random _random = new();
    
    private TriominoPieceViewModel? _selectedPiece;
    private string _statusMessage = "Welcome! Select the number of players to begin.";
    private string _selectionHint = "Each player will receive 7 randomly selected pieces.";
    private GridPosition? _hoveredPosition;
    private GamePhase _gamePhase = GamePhase.Setup;
    private int _numberOfPlayers = 2;
    private PlayerViewModel? _currentPlayerVm;
    private bool _isSelectingFromPool;

    public GameViewModel()
    {
        AvailablePieces = [];
        PlacedPieces = [];
        ValidPlacements = [];
        Players = [];
        DrawPilePieces = [];

        SelectPieceCommand = new RelayCommand(OnSelectPiece);
        SelectFromPoolCommand = new RelayCommand(OnSelectFromPool);
        PlacePieceCommand = new RelayCommand(OnPlacePiece, CanPlacePiece);
        RotateCommand = new RelayCommand(OnRotate, () => SelectedPiece != null);
        ResetGameCommand = new RelayCommand(OnResetGame);
        ClearBoardCommand = new RelayCommand(OnClearBoard);
        StartGameCommand = new RelayCommand(OnStartGame, () => GamePhase == GamePhase.Setup);
        EndTurnCommand = new RelayCommand(OnEndTurn, () => GamePhase == GamePhase.Playing);
        AddSelectedToRackCommand = new RelayCommand(OnAddSelectedToRack, CanAddSelectedToRack);

        UpdateHints();
    }

    #region Properties

    public ObservableCollection<TriominoPieceViewModel> AvailablePieces { get; }
    public ObservableCollection<PlacedPieceViewModel> PlacedPieces { get; }
    public ObservableCollection<GridPosition> ValidPlacements { get; }
    public ObservableCollection<PlayerViewModel> Players { get; }
    public ObservableCollection<TriominoPieceViewModel> DrawPilePieces { get; }

    public GamePhase GamePhase
    {
        get => _gamePhase;
        private set
        {
            if (SetProperty(ref _gamePhase, value))
            {
                OnPropertyChanged(nameof(IsSetupPhase));
                OnPropertyChanged(nameof(IsPlayingPhase));
                OnPropertyChanged(nameof(IsGameOver));
            }
        }
    }

    public bool IsSetupPhase => _gamePhase == GamePhase.Setup;
    public bool IsPlayingPhase => _gamePhase == GamePhase.Playing;
    public bool IsGameOver => _gamePhase == GamePhase.GameOver;

    public int NumberOfPlayers
    {
        get => _numberOfPlayers;
        set
        {
            if (GameRules.IsValidPlayerCount(value))
            {
                SetProperty(ref _numberOfPlayers, value);
            }
        }
    }

    public PlayerViewModel? CurrentPlayerVm
    {
        get => _currentPlayerVm;
        private set
        {
            if (_currentPlayerVm != null)
                _currentPlayerVm.IsCurrentPlayer = false;
            
            if (SetProperty(ref _currentPlayerVm, value))
            {
                if (_currentPlayerVm != null)
                    _currentPlayerVm.IsCurrentPlayer = true;
                
                OnPropertyChanged(nameof(CurrentPlayerName));
                UpdateAvailablePieces();
            }
        }
    }

    public string CurrentPlayerName => CurrentPlayerVm?.Name ?? "No Player";

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

    public bool IsSelectingFromPool => _isSelectingFromPool;

    public string SelectedPieceText => _selectedPiece != null
        ? $"Selected: {_selectedPiece}" + (_isSelectingFromPool ? " (from pool)" : "")
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

    public int DrawPileCount => DrawPilePieces.Count;

    public bool HasDrawPile => DrawPilePieces.Count > 0;

    #endregion

    #region Commands

    public ICommand SelectPieceCommand { get; }
    public ICommand SelectFromPoolCommand { get; }
    public ICommand PlacePieceCommand { get; }
    public ICommand RotateCommand { get; }
    public ICommand ResetGameCommand { get; }
    public ICommand ClearBoardCommand { get; }
    public ICommand StartGameCommand { get; }
    public ICommand EndTurnCommand { get; }
    public ICommand AddSelectedToRackCommand { get; }

    #endregion

    #region Command Handlers

    private void OnStartGame()
    {
        if (GamePhase != GamePhase.Setup)
            return;

        // Clear any previous game state
        _gameState.Clear();
        _gameState.ClearPlayers();
        Players.Clear();
        PlacedPieces.Clear();
        AvailablePieces.Clear();
        DrawPilePieces.Clear();
        SelectedPiece = null;
        _isSelectingFromPool = false;

        // Generate all pieces into the draw pile using GameRules
        GenerateDrawPile();

        // Place the first piece from the pool onto the board center
        PlaceInitialPieceFromPool();

        // Create players and deal pieces
        for (int i = 0; i < NumberOfPlayers; i++)
        {
            var player = new Player(i + 1, $"Player {i + 1}");
            _gameState.AddPlayer(player);
            var playerVm = new PlayerViewModel(player);
            
            // Deal pieces to each player using GameRules constant
            DealPiecesToPlayer(playerVm, GameRules.PiecesPerPlayer);
            
            Players.Add(playerVm);
        }

        // Set first player as current
        _gameState.SetCurrentPlayer(0);
        CurrentPlayerVm = Players[0];

        GamePhase = GamePhase.Playing;
        StatusMessage = $"Game started! {CurrentPlayerVm.Name}'s turn. Select a piece from your rack or the pool.";
        
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(PiecesPlacedCount));
        OnPropertyChanged(nameof(IsFirstMove));
        OnPropertyChanged(nameof(DrawPileCount));
        OnPropertyChanged(nameof(HasDrawPile));
        UpdateHints();
    }

    private void OnEndTurn()
    {
        AdvanceToNextPlayer();
    }

    private void OnSelectPiece(object? parameter)
    {
        if (parameter is not TriominoPieceViewModel piece)
            return;

        // Verify the piece belongs to the current player
        if (CurrentPlayerVm == null || !CurrentPlayerVm.RackPieces.Contains(piece))
        {
            StatusMessage = "Warning: You can only select pieces from your own rack!";
            return;
        }

        if (SelectedPiece == piece)
        {
            SelectedPiece = null;
            _isSelectingFromPool = false;
            OnPropertyChanged(nameof(IsSelectingFromPool));
            StatusMessage = $"{CurrentPlayerVm.Name}: Select a piece from your rack or the pool";
        }
        else
        {
            SelectedPiece = piece;
            _isSelectingFromPool = false;
            OnPropertyChanged(nameof(IsSelectingFromPool));
            OnPropertyChanged(nameof(SelectedPieceText));
            StatusMessage = IsFirstMove
                ? $"{CurrentPlayerVm.Name}: First move! Place your piece anywhere on the board"
                : $"{CurrentPlayerVm.Name}: Click on a highlighted cell to place the piece (green = valid)";
        }
        
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnSelectFromPool(object? parameter)
    {
        if (parameter is not TriominoPieceViewModel piece)
            return;

        if (CurrentPlayerVm == null)
            return;

        // Verify the piece is in the draw pile
        if (!DrawPilePieces.Contains(piece))
        {
            StatusMessage = "Warning: This piece is not in the pool!";
            return;
        }

        if (SelectedPiece == piece)
        {
            SelectedPiece = null;
            _isSelectingFromPool = false;
            OnPropertyChanged(nameof(IsSelectingFromPool));
            StatusMessage = $"{CurrentPlayerVm.Name}: Select a piece from your rack or the pool";
        }
        else
        {
            SelectedPiece = piece;
            _isSelectingFromPool = true;
            OnPropertyChanged(nameof(IsSelectingFromPool));
            OnPropertyChanged(nameof(SelectedPieceText));
            StatusMessage = IsFirstMove
                ? $"{CurrentPlayerVm.Name}: First move! Place your piece anywhere on the board (from pool)"
                : $"{CurrentPlayerVm.Name}: Click on a highlighted cell to place the piece (from pool). Right-click on rack to add to rack.";
        }
        
        CommandManager.InvalidateRequerySuggested();
    }

    private bool CanPlacePiece(object? parameter) => SelectedPiece != null && GamePhase == GamePhase.Playing;

    private void OnPlacePiece(object? parameter)
    {
        if (parameter is not GridPosition position || SelectedPiece == null || CurrentPlayerVm == null)
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
                
                // Remove piece from appropriate source
                if (_isSelectingFromPool)
                {
                    // Remove from pool
                    DrawPilePieces.Remove(SelectedPiece);
                    OnPropertyChanged(nameof(DrawPileCount));
                    OnPropertyChanged(nameof(HasDrawPile));
                }
                else
                {
                    // Remove from player's rack
                    CurrentPlayerVm.RemovePiece(SelectedPiece);
                }
                
                CurrentPlayerVm.RefreshScore();

                StatusMessage = $"OK - {CurrentPlayerVm.Name}: {result.Message}" + (_isSelectingFromPool ? " (from pool)" : "");
                SelectedPiece = null;
                _isSelectingFromPool = false;

                OnPropertyChanged(nameof(Score));
                OnPropertyChanged(nameof(PiecesPlacedCount));
                OnPropertyChanged(nameof(IsFirstMove));
                
                // Check if player has won using GameRules
                var gameEndResult = _gameState.CheckGameEnd(DrawPileCount);
                if (gameEndResult.IsGameOver)
                {
                    GamePhase = GamePhase.GameOver;
                    StatusMessage = $"Congratulations! {CurrentPlayerVm.Name} wins with {CurrentPlayerVm.Score} points!";
                    UpdateHints();
                    return;
                }

                // Advance to next player
                AdvanceToNextPlayer();
                UpdateHints();
                return;
            }
        }

        // All rotations failed
        StatusMessage = $"Error: {result?.Message ?? "Cannot place piece here"}";
        if (!IsFirstMove)
        {
            StatusMessage += " - Try rotating the piece or selecting a different position";
        }
    }

    private void OnRotate()
    {
        if (SelectedPiece == null)
        {
            StatusMessage = "Warning: Select a piece first to rotate it";
            return;
        }

        SelectedPiece.Rotate();
        StatusMessage = $"Rotated piece to {SelectedPiece}";
        OnPropertyChanged(nameof(SelectedPieceText));
        UpdateValidPlacements();
    }

    private void OnResetGame()
    {
        _gameState.Clear();
        _gameState.ClearPlayers();
        PlacedPieces.Clear();
        AvailablePieces.Clear();
        Players.Clear();
        DrawPilePieces.Clear();
        SelectedPiece = null;
        _isSelectingFromPool = false;
        CurrentPlayerVm = null;
        GamePhase = GamePhase.Setup;

        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(PiecesPlacedCount));
        OnPropertyChanged(nameof(IsFirstMove));
        OnPropertyChanged(nameof(DrawPileCount));
        OnPropertyChanged(nameof(HasDrawPile));

        StatusMessage = "Game reset - select number of players and start a new game";
        UpdateHints();
    }

    private void OnClearBoard()
    {
        OnResetGame();
        StatusMessage = "Board cleared - select number of players and start a new game";
    }

    private bool CanAddSelectedToRack() => 
        SelectedPiece != null && 
        _isSelectingFromPool && 
        CurrentPlayerVm != null && 
        GamePhase == GamePhase.Playing;

    private void OnAddSelectedToRack()
    {
        if (SelectedPiece == null || CurrentPlayerVm == null || !_isSelectingFromPool)
            return;

        var playerName = CurrentPlayerVm.Name;
        
        // Remove from pool and add to player's rack
        DrawPilePieces.Remove(SelectedPiece);
        CurrentPlayerVm.AddPieceViewModel(SelectedPiece);
        
        SelectedPiece = null;
        _isSelectingFromPool = false;

        OnPropertyChanged(nameof(DrawPileCount));
        OnPropertyChanged(nameof(HasDrawPile));
        
        StatusMessage = $"{playerName}: Added piece to rack from pool. Turn ends.";
        
        // Advance to the next player
        AdvanceToNextPlayer();
        UpdateHints();
    }

    #endregion

    #region Private Methods

    private void GenerateDrawPile()
    {
        // Use GameRules to generate and shuffle pieces
        var pieces = GameRules.GenerateAllPieces();
        GameRules.ShufflePieces(pieces, _random);
        
        // Add to observable collection
        foreach (var piece in pieces)
        {
            DrawPilePieces.Add(new TriominoPieceViewModel(piece));
        }
        
        OnPropertyChanged(nameof(DrawPileCount));
        OnPropertyChanged(nameof(HasDrawPile));
    }

    private void DealPiecesToPlayer(PlayerViewModel playerVm, int count)
    {
        for (int i = 0; i < count && DrawPilePieces.Count > 0; i++)
        {
            var pieceVm = DrawPilePieces[^1];
            DrawPilePieces.RemoveAt(DrawPilePieces.Count - 1);
            playerVm.AddPiece(pieceVm.Piece);
        }
        OnPropertyChanged(nameof(DrawPileCount));
        OnPropertyChanged(nameof(HasDrawPile));
    }

    /// <summary>
    /// Places an initial piece from the pool onto the center of the board
    /// </summary>
    private void PlaceInitialPieceFromPool()
    {
        if (DrawPilePieces.Count == 0)
            return;

        // Take a piece from the pool
        var initialPieceVm = DrawPilePieces[^1];
        DrawPilePieces.RemoveAt(DrawPilePieces.Count - 1);
        
        var piece = initialPieceVm.Piece;
        
        // Place at the center of the board
        int centerRow = GridRows / 2;
        int centerCol = GridCols / 2;
        
        // Try to place the piece
        var result = _gameState.TryPlacePiece(piece, centerRow, centerCol);
        
        if (result.Success)
        {
            var placedVm = new PlacedPieceViewModel(piece, centerRow, centerCol, CellSize);
            PlacedPieces.Add(placedVm);
        }
        
        OnPropertyChanged(nameof(DrawPileCount));
        OnPropertyChanged(nameof(HasDrawPile));
        OnPropertyChanged(nameof(PiecesPlacedCount));
        OnPropertyChanged(nameof(IsFirstMove));
    }

    private void UpdateAvailablePieces()
    {
        AvailablePieces.Clear();
        
        if (CurrentPlayerVm != null)
        {
            foreach (var piece in CurrentPlayerVm.RackPieces)
            {
                AvailablePieces.Add(piece);
            }
        }
    }

    private void AdvanceToNextPlayer()
    {
        if (Players.Count == 0)
            return;

        _gameState.NextTurn();
        int nextIndex = _gameState.CurrentPlayerIndex;
        CurrentPlayerVm = Players[nextIndex];
        SelectedPiece = null;
        _isSelectingFromPool = false;

        StatusMessage = $"{CurrentPlayerVm.Name}'s turn. Select a piece from your rack or the pool.";
        UpdateHints();
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
        SelectionHint = (GamePhase, IsFirstMove, SelectedPiece, _isSelectingFromPool) switch
        {
            (GamePhase.Setup, _, _, _) => $"Select 1-{GameRules.MaxPlayers} players. Each player will receive {GameRules.PiecesPerPlayer} randomly selected pieces.",
            (GamePhase.GameOver, _, _, _) => "Game over! Click 'Reset Game' to play again.",
            (GamePhase.Playing, _, not null, true) => $"{CurrentPlayerVm?.Name}: Click on your rack or a rack piece to add from pool, or place on board.",
            (GamePhase.Playing, _, not null, false) => $"{CurrentPlayerVm?.Name}: Place on a green cell. Edges must match adjacent pieces. Use Rotate to change orientation.",
            _ => $"{CurrentPlayerVm?.Name}: Click a piece from your rack or the pool to select it. Match edge values with adjacent pieces to place it."
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
    public bool IsPointingUp => GameRules.IsPointingUp(Row, Col);
}
