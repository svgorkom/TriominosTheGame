namespace Triominos.Core.Services;

using Triominos.Core.Events;
using Triominos.Core.Interfaces;
using Triominos.Core.Models;

/// <summary>
/// The main game engine that manages all game logic.
/// This is completely UI-agnostic and can be used with any presentation layer.
/// </summary>
public class GameEngine : IGameEngine
{
    private readonly List<Player> _players = [];
    private readonly GameBoard _board;
    private readonly DrawPile _drawPile;
    private readonly Random _random;

    private int _currentPlayerIndex;
    private IPiece? _selectedPiece;
    private bool _isSelectingFromPool;
    private GamePhase _currentPhase = GamePhase.Setup;
    private int _totalScore;

    #region Properties

    public GamePhase CurrentPhase => _currentPhase;
    public IReadOnlyList<IPlayer> Players => _players;
    public IPlayer? CurrentPlayer => _players.Count > 0 ? _players[_currentPlayerIndex] : null;
    public IGameBoard Board => _board;
    public IDrawPile DrawPile => _drawPile;
    public IPiece? SelectedPiece => _selectedPiece;
    public bool IsSelectingFromPool => _isSelectingFromPool;
    public bool IsFirstMove => _board.PiecesPlaced == 0;
    public int TotalScore => _totalScore;

    #endregion

    #region Events

    public event EventHandler<GameStateChangedEventArgs>? GameStateChanged;
    public event EventHandler<PiecePlacedEventArgs>? PiecePlaced;
    public event EventHandler<PlayerChangedEventArgs>? PlayerChanged;
    public event EventHandler<GameEndedEventArgs>? GameEnded;
    public event EventHandler<PieceSelectionChangedEventArgs>? PieceSelectionChanged;

    #endregion

    public GameEngine(int boardRows = 12, int boardCols = 24, Random? random = null)
    {
        _random = random ?? new Random();
        _board = new GameBoard(boardRows, boardCols);
        _drawPile = new DrawPile(_random);
    }

    #region Game Flow Operations

    public GameResult StartGame(int numberOfPlayers)
    {
        if (_currentPhase != GamePhase.Setup)
            return new GameResult(false, "Game has already started");

        if (!GameRules.IsValidPlayerCount(numberOfPlayers))
            return new GameResult(false, $"Player count must be between {GameRules.MinPlayers} and {GameRules.MaxPlayers}");

        // Initialize
        _players.Clear();
        _board.Clear();
        _drawPile.Initialize();
        _selectedPiece = null;
        _isSelectingFromPool = false;
        _totalScore = 0;
        _currentPlayerIndex = 0;

        // Place initial piece
        PlaceInitialPiece();

        // Create players and deal pieces
        for (int i = 0; i < numberOfPlayers; i++)
        {
            var player = new Player(i + 1, $"Player {i + 1}");
            DealPiecesToPlayer(player, GameRules.PiecesPerPlayer);
            _players.Add(player);
        }

        // Set first player as current
        if (_players.Count > 0)
        {
            _players[0].IsCurrentPlayer = true;
        }

        _currentPhase = GamePhase.Playing;
        
        OnGameStateChanged(GamePhase.Playing, $"Game started! {CurrentPlayer?.Name}'s turn.");
        
        return new GameResult(true, "Game started successfully");
    }

    public void ResetGame()
    {
        _players.Clear();
        _board.Clear();
        _drawPile.Clear();
        _selectedPiece = null;
        _isSelectingFromPool = false;
        _totalScore = 0;
        _currentPlayerIndex = 0;
        _currentPhase = GamePhase.Setup;

        OnGameStateChanged(GamePhase.Setup, "Game reset");
    }

    public GameResult EndTurn()
    {
        if (_currentPhase != GamePhase.Playing)
            return new GameResult(false, "Game is not in progress");

        AdvanceToNextPlayer();
        return new GameResult(true, $"{CurrentPlayer?.Name}'s turn");
    }

    #endregion

    #region Piece Operations

    public GameResult SelectPieceFromRack(IPiece piece)
    {
        if (_currentPhase != GamePhase.Playing)
            return new GameResult(false, "Game is not in progress");

        var currentPlayer = CurrentPlayer as Player;
        if (currentPlayer == null)
            return new GameResult(false, "No current player");

        if (!currentPlayer.Rack.Contains(piece))
            return new GameResult(false, "Piece is not in your rack");

        // Toggle selection
        if (_selectedPiece == piece)
        {
            DeselectPiece();
            return new GameResult(true, "Piece deselected");
        }

        _selectedPiece = piece;
        _isSelectingFromPool = false;
        
        OnPieceSelectionChanged(piece, false);
        
        return new GameResult(true, "Piece selected from rack");
    }

    public GameResult SelectPieceFromPool(IPiece piece)
    {
        if (_currentPhase != GamePhase.Playing)
            return new GameResult(false, "Game is not in progress");

        if (!_drawPile.Pieces.Contains(piece))
            return new GameResult(false, "Piece is not in the pool");

        // Toggle selection
        if (_selectedPiece == piece)
        {
            DeselectPiece();
            return new GameResult(true, "Piece deselected");
        }

        _selectedPiece = piece;
        _isSelectingFromPool = true;
        
        OnPieceSelectionChanged(piece, true);
        
        return new GameResult(true, "Piece selected from pool");
    }

    public void DeselectPiece()
    {
        _selectedPiece = null;
        _isSelectingFromPool = false;
        OnPieceSelectionChanged(null, false);
    }

    public GameResult RotatePiece()
    {
        if (_selectedPiece == null)
            return new GameResult(false, "No piece selected");

        _selectedPiece.Rotate();
        OnPieceSelectionChanged(_selectedPiece, _isSelectingFromPool);
        
        return new GameResult(true, $"Rotated to {_selectedPiece}");
    }

    public GameResult PlacePiece(int row, int col)
    {
        if (_currentPhase != GamePhase.Playing)
            return new GameResult(false, "Game is not in progress");

        if (_selectedPiece == null)
            return new GameResult(false, "No piece selected");

        var currentPlayer = CurrentPlayer as Player;
        if (currentPlayer == null)
            return new GameResult(false, "No current player");

        // Try all rotations
        for (int rotation = 0; rotation < 3; rotation++)
        {
            var pieceToPlace = (Piece)_selectedPiece.Clone();
            
            for (int r = 0; r < rotation; r++)
                pieceToPlace.Rotate();

            pieceToPlace.IsPointingUp = GameRules.IsPointingUp(row, col);

            var validation = GameRules.ValidatePlacement(pieceToPlace, row, col, _board, IsFirstMove);
            
            if (validation.IsValid)
            {
                // Place the piece
                var placedPiece = _board.PlacePiece(pieceToPlace, row, col);
                
                // Calculate score
                bool completesHexagon = GameRules.CheckHexagonCompletion(row, col, _board);
                int points = GameRules.CalculatePlacementScore(
                    pieceToPlace, IsFirstMove, validation.MatchingEdges, completesHexagon);
                
                _totalScore += points;
                currentPlayer.AddScore(points);

                // Remove piece from source
                if (_isSelectingFromPool)
                {
                    _drawPile.RemovePiece(_selectedPiece);
                }
                else
                {
                    currentPlayer.RemovePiece(_selectedPiece);
                }

                string message = GameRules.GetPlacementMessage(
                    pieceToPlace, points, IsFirstMove, validation.MatchingEdges, completesHexagon);

                _selectedPiece = null;
                _isSelectingFromPool = false;

                OnPiecePlaced(placedPiece, points, message);
                OnPieceSelectionChanged(null, false);

                // Check for game end
                if (currentPlayer.PiecesRemaining == 0)
                {
                    EndGame(currentPlayer, GameEndReason.PlayerWentOut);
                    return new GameResult(true, message, points);
                }

                AdvanceToNextPlayer();
                return new GameResult(true, message, points);
            }
        }

        return new GameResult(false, "Cannot place piece here - edges don't match");
    }

    public GameResult AddSelectedPieceToRack()
    {
        if (_currentPhase != GamePhase.Playing)
            return new GameResult(false, "Game is not in progress");

        if (_selectedPiece == null)
            return new GameResult(false, "No piece selected");

        if (!_isSelectingFromPool)
            return new GameResult(false, "Can only add pool pieces to rack");

        var currentPlayer = CurrentPlayer as Player;
        if (currentPlayer == null)
            return new GameResult(false, "No current player");

        // Remove from pool and add to rack
        _drawPile.RemovePiece(_selectedPiece);
        currentPlayer.AddPiece(_selectedPiece);

        var playerName = currentPlayer.Name;
        _selectedPiece = null;
        _isSelectingFromPool = false;

        OnPieceSelectionChanged(null, false);
        AdvanceToNextPlayer();

        return new GameResult(true, $"{playerName}: Added piece to rack. Turn ends.");
    }

    #endregion

    #region Query Operations

    public IEnumerable<BoardPosition> GetValidPlacements()
    {
        if (_selectedPiece == null)
            yield break;

        foreach (var (row, col) in GameRules.GetValidPlacements(_selectedPiece, _board))
        {
            yield return new BoardPosition(row, col);
        }
    }

    public bool CanPlaceAt(int row, int col)
    {
        if (_selectedPiece == null)
            return false;

        // Try all rotations
        for (int rotation = 0; rotation < 3; rotation++)
        {
            var testPiece = (Piece)_selectedPiece.Clone();
            for (int r = 0; r < rotation; r++)
                testPiece.Rotate();

            testPiece.IsPointingUp = GameRules.IsPointingUp(row, col);
            
            var result = GameRules.ValidatePlacement(testPiece, row, col, _board, IsFirstMove);
            if (result.IsValid)
                return true;
        }

        return false;
    }

    #endregion

    #region Private Methods

    private void PlaceInitialPiece()
    {
        var piece = _drawPile.DrawPiece();
        if (piece == null) return;

        int centerRow = _board.Rows / 2;
        int centerCol = _board.Cols / 2;

        if (piece is Piece mutablePiece)
        {
            mutablePiece.IsPointingUp = GameRules.IsPointingUp(centerRow, centerCol);
        }

        var placedPiece = _board.PlacePiece(piece, centerRow, centerCol);
        int points = GameRules.CalculateBaseScore(piece);
        _totalScore += points;

        OnPiecePlaced(placedPiece, points, $"Initial piece placed: {piece}");
    }

    private void DealPiecesToPlayer(Player player, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var piece = _drawPile.DrawPiece();
            if (piece == null) break;
            player.AddPiece(piece);
        }
    }

    private void AdvanceToNextPlayer()
    {
        if (_players.Count == 0) return;

        var previousPlayer = CurrentPlayer;
        
        if (previousPlayer is Player prevPlayer)
            prevPlayer.IsCurrentPlayer = false;

        _currentPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
        
        if (CurrentPlayer is Player newPlayer)
            newPlayer.IsCurrentPlayer = true;

        _selectedPiece = null;
        _isSelectingFromPool = false;

        OnPlayerChanged(previousPlayer, CurrentPlayer!);
        OnPieceSelectionChanged(null, false);
    }

    private void EndGame(IPlayer? winner, GameEndReason reason)
    {
        _currentPhase = GamePhase.GameOver;
        
        var standings = _players.OrderByDescending(p => p.Score).ToList<IPlayer>();
        
        OnGameEnded(winner, reason, standings);
        OnGameStateChanged(GamePhase.GameOver, 
            winner != null ? $"{winner.Name} wins!" : "Game over!");
    }

    #endregion

    #region Event Raisers

    protected virtual void OnGameStateChanged(GamePhase phase, string message)
    {
        GameStateChanged?.Invoke(this, new GameStateChangedEventArgs(phase, message));
    }

    protected virtual void OnPiecePlaced(IPlacedPiece piece, int points, string message)
    {
        PiecePlaced?.Invoke(this, new PiecePlacedEventArgs(piece, points, message));
    }

    protected virtual void OnPlayerChanged(IPlayer? previous, IPlayer current)
    {
        PlayerChanged?.Invoke(this, new PlayerChangedEventArgs(previous, current));
    }

    protected virtual void OnGameEnded(IPlayer? winner, GameEndReason reason, IReadOnlyList<IPlayer> standings)
    {
        GameEnded?.Invoke(this, new GameEndedEventArgs(winner, reason, standings));
    }

    protected virtual void OnPieceSelectionChanged(IPiece? piece, bool fromPool)
    {
        PieceSelectionChanged?.Invoke(this, new PieceSelectionChangedEventArgs(piece, fromPool));
    }

    #endregion
}
