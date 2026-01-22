using System.Collections.ObjectModel;
using System.Windows.Input;
using Triominos.Core;
using Triominos.Core.Events;
using Triominos.Core.Interfaces;
using Triominos.Core.Services;
using Triominos.UI.Models;
using GamePhase = Triominos.Core.Interfaces.GamePhase;

namespace Triominos.UI.ViewModels;

/// <summary>
/// WPF-specific ViewModel that wraps the core GameEngine.
/// This provides WPF-friendly collections and commands while delegating
/// all game logic to the UI-agnostic GameEngine.
/// </summary>
public class GameViewModel : ViewModelBase
{
    private readonly GameEngine _gameEngine;
    
    private string _statusMessage = "Welcome! Select the number of players to begin.";
    private string _selectionHint = "";
    private int _numberOfPlayers = 2;
    private GridPosition? _hoveredPosition;
    private TriominoPieceViewModel? _selectedPieceVm;

    // Observable collections for WPF binding
    public ObservableCollection<PlacedPieceViewModel> PlacedPieces { get; } = [];
    public ObservableCollection<GridPosition> ValidPlacements { get; } = [];
    public ObservableCollection<PlayerViewModel> Players { get; } = [];
    public ObservableCollection<TriominoPieceViewModel> DrawPilePieces { get; } = [];
    public ObservableCollection<TriominoPieceViewModel> AvailablePieces { get; } = [];

    public GameViewModel()
    {
        _gameEngine = new GameEngine(GridRows, GridCols);
        
        // Subscribe to engine events
        _gameEngine.GameStateChanged += OnGameStateChanged;
        _gameEngine.PiecePlaced += OnPiecePlaced;
        _gameEngine.PlayerChanged += OnPlayerChanged;
        _gameEngine.GameEnded += OnGameEnded;
        _gameEngine.PieceSelectionChanged += OnPieceSelectionChanged;

        // Initialize commands
        StartGameCommand = new RelayCommand(ExecuteStartGame, () => IsSetupPhase);
        ResetGameCommand = new RelayCommand(ExecuteResetGame);
        ClearBoardCommand = new RelayCommand(ExecuteResetGame);
        SelectPieceCommand = new RelayCommand(ExecuteSelectPiece);
        SelectFromPoolCommand = new RelayCommand(ExecuteSelectFromPool);
        PlacePieceCommand = new RelayCommand(ExecutePlacePiece, CanExecutePlacePiece);
        RotateCommand = new RelayCommand(ExecuteRotate, () => HasSelectedPiece);
        AddSelectedToRackCommand = new RelayCommand(ExecuteAddToRack, CanExecuteAddToRack);
        EndTurnCommand = new RelayCommand(ExecuteEndTurn, () => IsPlayingPhase);

        UpdateHints();
    }

    #region Properties - Game State

    public bool IsSetupPhase => _gameEngine.CurrentPhase == GamePhase.Setup;
    public bool IsPlayingPhase => _gameEngine.CurrentPhase == GamePhase.Playing;
    public bool IsGameOver => _gameEngine.CurrentPhase == GamePhase.GameOver;
    public bool IsFirstMove => _gameEngine.IsFirstMove;
    public int Score => _gameEngine.TotalScore;
    public int PiecesPlacedCount => _gameEngine.Board.PiecesPlaced;

    public int NumberOfPlayers
    {
        get => _numberOfPlayers;
        set
        {
            if (Core.GameRules.IsValidPlayerCount(value))
                SetProperty(ref _numberOfPlayers, value);
        }
    }

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

    #region Properties - Players

    public PlayerViewModel? CurrentPlayerVm => Players.FirstOrDefault(p => p.IsCurrentPlayer);
    public string CurrentPlayerName => _gameEngine.CurrentPlayer?.Name ?? "No Player";

    #endregion

    #region Properties - Pieces

    public TriominoPieceViewModel? SelectedPiece
    {
        get => _selectedPieceVm;
        private set
        {
            if (_selectedPieceVm != null)
                _selectedPieceVm.IsSelected = false;

            if (SetProperty(ref _selectedPieceVm, value))
            {
                if (_selectedPieceVm != null)
                    _selectedPieceVm.IsSelected = true;

                OnPropertyChanged(nameof(HasSelectedPiece));
                OnPropertyChanged(nameof(SelectedPieceText));
                OnPropertyChanged(nameof(IsSelectingFromPool));
            }
        }
    }

    public bool HasSelectedPiece => _selectedPieceVm != null;
    public bool IsSelectingFromPool => _gameEngine.IsSelectingFromPool;

    public string SelectedPieceText => _selectedPieceVm != null
        ? $"Selected: {_selectedPieceVm}" + (IsSelectingFromPool ? " (from pool)" : "")
        : "Selected: None";

    public int DrawPileCount => DrawPilePieces.Count;
    public bool HasDrawPile => DrawPilePieces.Count > 0;

    #endregion

    #region Commands

    public ICommand StartGameCommand { get; }
    public ICommand ResetGameCommand { get; }
    public ICommand ClearBoardCommand { get; }
    public ICommand SelectPieceCommand { get; }
    public ICommand SelectFromPoolCommand { get; }
    public ICommand PlacePieceCommand { get; }
    public ICommand RotateCommand { get; }
    public ICommand AddSelectedToRackCommand { get; }
    public ICommand EndTurnCommand { get; }

    #endregion

    #region Command Implementations

    private void ExecuteStartGame()
    {
        GameResult result = _gameEngine.StartGame(NumberOfPlayers);
        if (result.Success)
        {
            SyncFromEngine();
            StatusMessage = $"Game started! {CurrentPlayerName}'s turn.";
        }
        else
        {
            StatusMessage = result.Message;
        }
    }

    private void ExecuteResetGame()
    {
        _gameEngine.ResetGame();
        ClearAllCollections();
        SelectedPiece = null;
        StatusMessage = "Game reset - select number of players and start a new game";
        NotifyAllPropertiesChanged();
        UpdateHints();
    }

    private void ExecuteSelectPiece(object? parameter)
    {
        if (parameter is not TriominoPieceViewModel pieceVm) return;
        
        // Check if we should add from pool instead
        if (CanExecuteAddToRack())
        {
            ExecuteAddToRack();
            return;
        }

        // Find the matching IPiece in the current player's rack
        IPlayer? currentPlayer = _gameEngine.CurrentPlayer;
        if (currentPlayer == null) return;

        IPiece? piece = currentPlayer.Rack.FirstOrDefault(p => p.Id == pieceVm.Piece.Id);
        if (piece == null)
        {
            StatusMessage = "Warning: Piece not found in rack!";
            return;
        }

        GameResult result = _gameEngine.SelectPieceFromRack(piece);
        if (result.Success)
        {
            SelectedPiece = _gameEngine.SelectedPiece != null ? pieceVm : null;
            UpdateValidPlacements();
            UpdateSelectionStatus();
        }
        else
        {
            StatusMessage = result.Message;
        }
        
        CommandManager.InvalidateRequerySuggested();
    }

    private void ExecuteSelectFromPool(object? parameter)
    {
        if (parameter is not TriominoPieceViewModel pieceVm) return;

        IPiece? piece = _gameEngine.DrawPile.Pieces.FirstOrDefault(p => p.Id == pieceVm.Piece.Id);
        if (piece == null)
        {
            StatusMessage = "Warning: Piece not found in pool!";
            return;
        }

        GameResult result = _gameEngine.SelectPieceFromPool(piece);
        if (result.Success)
        {
            SelectedPiece = _gameEngine.SelectedPiece != null ? pieceVm : null;
            UpdateValidPlacements();
            UpdateSelectionStatus();
        }
        else
        {
            StatusMessage = result.Message;
        }
        
        CommandManager.InvalidateRequerySuggested();
    }

    private bool CanExecutePlacePiece(object? parameter) 
        => HasSelectedPiece && IsPlayingPhase;

    private void ExecutePlacePiece(object? parameter)
    {
        if (parameter is not GridPosition position) return;

        GameResult result = _gameEngine.PlacePiece(position.Row, position.Col);
        StatusMessage = result.Success 
            ? $"OK - {CurrentPlayerName}: {result.Message}"
            : $"Error: {result.Message}";
    }

    private void ExecuteRotate()
    {
        if (_selectedPieceVm == null) return;

        GameResult result = _gameEngine.RotatePiece();
        if (result.Success)
        {
            _selectedPieceVm.Rotate();
            StatusMessage = result.Message;
            UpdateValidPlacements();
        }
    }

    private bool CanExecuteAddToRack() 
        => HasSelectedPiece && IsSelectingFromPool && IsPlayingPhase;

    private void ExecuteAddToRack()
    {
        GameResult result = _gameEngine.AddSelectedPieceToRack();
        StatusMessage = result.Message;
    }

    private void ExecuteEndTurn()
    {
        GameResult result = _gameEngine.EndTurn();
        StatusMessage = result.Message;
    }

    #endregion

    #region Event Handlers

    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        StatusMessage = e.Message;
        NotifyAllPropertiesChanged();
        UpdateHints();
    }

    private void OnPiecePlaced(object? sender, PiecePlacedEventArgs e)
    {
        // Add to placed pieces collection
        IPlacedPiece placedPiece = e.PlacedPiece;
        var uiPiece = new TriominoPiece(placedPiece.Piece);
        PlacedPieces.Add(new PlacedPieceViewModel(uiPiece, placedPiece.Row, placedPiece.Col, CellSize));

        // Remove from pool if it was a pool piece
        if (SelectedPiece != null)
        {
            DrawPilePieces.Remove(SelectedPiece);
            CurrentPlayerVm?.RackPieces.Remove(SelectedPiece);
        }

        SelectedPiece = null;
        ValidPlacements.Clear();

        OnPropertyChanged(nameof(PiecesPlacedCount));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(IsFirstMove));
        OnPropertyChanged(nameof(DrawPileCount));
        OnPropertyChanged(nameof(HasDrawPile));
        
        CurrentPlayerVm?.RefreshScore();
    }

    private void OnPlayerChanged(object? sender, PlayerChangedEventArgs e)
    {
        // Update current player flags
        foreach (PlayerViewModel player in Players)
        {
            player.IsCurrentPlayer = player.CorePlayer.Id == e.NewPlayer.Id;
        }

        SelectedPiece = null;
        ValidPlacements.Clear();
        
        OnPropertyChanged(nameof(CurrentPlayerVm));
        OnPropertyChanged(nameof(CurrentPlayerName));
        UpdateHints();
    }

    private void OnGameEnded(object? sender, GameEndedEventArgs e)
    {
        StatusMessage = e.Winner != null 
            ? $"Congratulations! {e.Winner.Name} wins with {e.Winner.Score} points!"
            : "Game over!";
        NotifyAllPropertiesChanged();
    }

    private void OnPieceSelectionChanged(object? sender, PieceSelectionChangedEventArgs e)
    {
        // SelectedPiece is managed separately for WPF
        OnPropertyChanged(nameof(IsSelectingFromPool));
        UpdateValidPlacements();
        UpdateHints();
        CommandManager.InvalidateRequerySuggested();
    }

    #endregion

    #region Private Methods

    private void SyncFromEngine()
    {
        ClearAllCollections();

        // Sync players
        foreach (IPlayer player in _gameEngine.Players)
        {
            var playerVm = new PlayerViewModel(player);
            playerVm.IsCurrentPlayer = player.IsCurrentPlayer;
            Players.Add(playerVm);
        }

        // Sync draw pile
        foreach (IPiece piece in _gameEngine.DrawPile.Pieces)
        {
            DrawPilePieces.Add(new TriominoPieceViewModel(piece));
        }

        // Sync placed pieces
        foreach (IPlacedPiece placedPiece in _gameEngine.Board.PlacedPieces)
        {
            var uiPiece = new TriominoPiece(placedPiece.Piece);
            PlacedPieces.Add(new PlacedPieceViewModel(uiPiece, placedPiece.Row, placedPiece.Col, CellSize));
        }

        NotifyAllPropertiesChanged();
    }

    private void ClearAllCollections()
    {
        Players.Clear();
        PlacedPieces.Clear();
        DrawPilePieces.Clear();
        ValidPlacements.Clear();
        AvailablePieces.Clear();
    }

    private void UpdateValidPlacements()
    {
        ValidPlacements.Clear();

        if (_gameEngine.SelectedPiece == null) return;

        foreach (BoardPosition pos in _gameEngine.GetValidPlacements())
        {
            ValidPlacements.Add(new GridPosition(pos.Row, pos.Col));
        }
    }

    private void UpdateSelectionStatus()
    {
        if (IsFirstMove)
            StatusMessage = $"{CurrentPlayerName}: First move! Place your piece anywhere on the board";
        else if (IsSelectingFromPool)
            StatusMessage = $"{CurrentPlayerName}: Click on your rack to add from pool, or place on board";
        else
            StatusMessage = $"{CurrentPlayerName}: Click on a highlighted cell to place the piece";
    }

    private void UpdateHints()
    {
        SelectionHint = (_gameEngine.CurrentPhase, IsFirstMove, HasSelectedPiece, IsSelectingFromPool) switch
        {
            (GamePhase.Setup, _, _, _) => $"Select 1-{Core.GameRules.MaxPlayers} players. Each player will receive {Core.GameRules.PiecesPerPlayer} pieces.",
            (GamePhase.GameOver, _, _, _) => "Game over! Click 'Reset Game' to play again.",
            (GamePhase.Playing, _, true, true) => $"{CurrentPlayerName}: Click on your rack to add from pool, or place on board.",
            (GamePhase.Playing, _, true, false) => $"{CurrentPlayerName}: Place on a green cell. Edges must match.",
            _ => $"{CurrentPlayerName}: Click a piece to select it."
        };
    }

    private void NotifyAllPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsSetupPhase));
        OnPropertyChanged(nameof(IsPlayingPhase));
        OnPropertyChanged(nameof(IsGameOver));
        OnPropertyChanged(nameof(IsFirstMove));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(PiecesPlacedCount));
        OnPropertyChanged(nameof(DrawPileCount));
        OnPropertyChanged(nameof(HasDrawPile));
        OnPropertyChanged(nameof(CurrentPlayerVm));
        OnPropertyChanged(nameof(CurrentPlayerName));
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
    public bool IsPointingUp => Core.GameRules.IsPointingUp(Row, Col);
}
