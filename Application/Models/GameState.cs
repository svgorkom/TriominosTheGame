namespace Triominos.Models;

/// <summary>
/// Manages the game state, board, and player turns for Triominos.
/// Uses GameRules for all rule validation and scoring.
/// </summary>
public class GameState
{
    private readonly Dictionary<(int row, int col), PlacedTriomino> _board = new();
    private readonly List<Player> _players = [];
    private int _currentPlayerIndex;

    public int Score { get; private set; }
    public int PiecesPlaced => _board.Count;
    public bool IsFirstMove => _board.Count == 0;

    /// <summary>
    /// Gets all players in the game
    /// </summary>
    public IReadOnlyList<Player> Players => _players;

    /// <summary>
    /// Gets the current player
    /// </summary>
    public Player? CurrentPlayer => _players.Count > 0 ? _players[_currentPlayerIndex] : null;

    /// <summary>
    /// Gets the index of the current player
    /// </summary>
    public int CurrentPlayerIndex => _currentPlayerIndex;

    /// <summary>
    /// Gets whether the game is in multiplayer mode
    /// </summary>
    public bool IsMultiplayer => _players.Count > 1;

    /// <summary>
    /// Adds a player to the game
    /// </summary>
    public void AddPlayer(Player player)
    {
        if (_players.Count >= GameRules.MaxPlayers)
            throw new InvalidOperationException($"Maximum of {GameRules.MaxPlayers} players allowed");
        
        _players.Add(player);
    }

    /// <summary>
    /// Clears all players from the game
    /// </summary>
    public void ClearPlayers()
    {
        _players.Clear();
        _currentPlayerIndex = 0;
    }

    /// <summary>
    /// Advances to the next player's turn
    /// </summary>
    public void NextTurn()
    {
        if (_players.Count > 0)
        {
            _currentPlayerIndex = (_currentPlayerIndex + 1) % _players.Count;
        }
    }

    /// <summary>
    /// Sets the current player by index
    /// </summary>
    public void SetCurrentPlayer(int playerIndex)
    {
        if (playerIndex >= 0 && playerIndex < _players.Count)
        {
            _currentPlayerIndex = playerIndex;
        }
    }

    /// <summary>
    /// Attempts to place a piece at the specified position
    /// </summary>
    public PlacementResult TryPlacePiece(TriominoPiece piece, int row, int col)
    {
        // Set the correct orientation based on grid position using GameRules
        piece.IsPointingUp = GameRules.IsPointingUp(row, col);

        if (_board.ContainsKey((row, col)))
        {
            return new PlacementResult(false, "Cell is already occupied", 0);
        }

        if (IsFirstMove)
        {
            return PlacePieceInternal(piece, row, col, isFirstMove: true);
        }

        var validationResult = ValidatePlacement(piece, row, col);
        if (!validationResult.IsValid)
        {
            return new PlacementResult(false, validationResult.Message, 0);
        }

        return PlacePieceInternal(piece, row, col, isFirstMove: false, validationResult.MatchingEdges);
    }

    private PlacementResult PlacePieceInternal(TriominoPiece piece, int row, int col, bool isFirstMove, int matchingEdges = 0)
    {
        var placedPiece = new PlacedTriomino(piece.Clone(), row, col);
        _board[(row, col)] = placedPiece;

        bool completesHexagon = CheckHexagonCompletion(row, col);
        
        // Use GameRules for score calculation
        int points = GameRules.CalculatePlacementScore(piece, isFirstMove, matchingEdges, completesHexagon);
        Score += points;

        // Update current player's score if in multiplayer mode
        if (CurrentPlayer != null)
        {
            CurrentPlayer.Score += points;
        }

        string message = GameRules.GetPlacementMessage(piece, points, isFirstMove, matchingEdges, completesHexagon);
        return new PlacementResult(true, message, points);
    }

    private (bool IsValid, string Message, int MatchingEdges) ValidatePlacement(TriominoPiece piece, int row, int col)
    {
        var tempPlaced = new PlacedTriomino(piece, row, col);
        var adjacentPositions = tempPlaced.GetAdjacentPositions().ToList();

        int matchingEdges = 0;
        bool hasAdjacentPiece = false;

        foreach (var (adjRow, adjCol) in adjacentPositions)
        {
            if (!_board.TryGetValue((adjRow, adjCol), out var adjacentPiece))
                continue;

            hasAdjacentPiece = true;

            var thisEdge = tempPlaced.GetEdgeFacing(adjRow, adjCol);
            var otherEdge = adjacentPiece.GetEdgeFacing(row, col);

            // Use GameRules for edge matching
            if (GameRules.EdgesMatch(thisEdge, otherEdge))
            {
                matchingEdges++;
            }
            else
            {
                return (false, $"Edge mismatch! Your edge [{thisEdge.val1}-{thisEdge.val2}] doesn't match [{otherEdge.val1}-{otherEdge.val2}]", 0);
            }
        }

        if (!hasAdjacentPiece)
        {
            return (false, "Piece must be placed adjacent to an existing piece", 0);
        }

        return (true, "Valid placement", matchingEdges);
    }

    private bool CheckHexagonCompletion(int row, int col)
    {
        // Check if placing this piece completes a hexagon (6 triangles)
        foreach (var center in GetPotentialHexagonCenters(row, col))
        {
            if (IsHexagonComplete(center.row, center.col))
            {
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<(int row, int col)> GetPotentialHexagonCenters(int row, int col)
    {
        yield return (row, col);
        yield return (row - 1, col);
        yield return (row + 1, col);
        yield return (row, col - 1);
        yield return (row, col + 1);
    }

    private bool IsHexagonComplete(int centerRow, int centerCol)
    {
        (int row, int col)[] positions =
        [
            (centerRow, centerCol),
            (centerRow, centerCol - 1),
            (centerRow, centerCol + 1),
            (centerRow - 1, centerCol),
            (centerRow - 1, centerCol - 1),
            (centerRow - 1, centerCol + 1)
        ];

        return positions.All(p => _board.ContainsKey(p));
    }

    /// <summary>
    /// Gets all valid placement positions for a given piece (tries all rotations)
    /// </summary>
    public IEnumerable<(int row, int col)> GetValidPlacements(TriominoPiece piece, int gridRows, int gridCols)
    {
        if (IsFirstMove)
        {
            yield return (gridRows / 2, gridCols / 2);
            yield break;
        }

        var checkedPositions = new HashSet<(int, int)>();

        foreach (var placed in _board.Values)
        {
            foreach (var adj in placed.GetAdjacentPositions())
            {
                if (checkedPositions.Contains(adj) || _board.ContainsKey(adj))
                    continue;

                if (adj.row < 0 || adj.row >= gridRows || adj.col < 0 || adj.col >= gridCols)
                    continue;

                checkedPositions.Add(adj);

                // Try all 3 rotations to see if any works
                for (int r = 0; r < 3; r++)
                {
                    var testPiece = piece.Clone();
                    testPiece.IsPointingUp = GameRules.IsPointingUp(adj.row, adj.col);
                    
                    // Apply rotations
                    for (int i = 0; i < r; i++)
                    {
                        testPiece.Rotate();
                    }
                    
                    var result = ValidatePlacement(testPiece, adj.row, adj.col);
                    if (result.IsValid)
                    {
                        yield return adj;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if the game should end
    /// </summary>
    public GameEndResult CheckGameEnd(int poolCount)
    {
        return GameRules.CheckGameOver(_players, poolCount);
    }

    /// <summary>
    /// Gets a placed piece at the specified position
    /// </summary>
    public PlacedTriomino? GetPieceAt(int row, int col) => _board.GetValueOrDefault((row, col));

    /// <summary>
    /// Clears the game state
    /// </summary>
    public void Clear()
    {
        _board.Clear();
        Score = 0;
        _currentPlayerIndex = 0;
        
        // Reset player scores but keep players
        foreach (var player in _players)
        {
            player.Score = 0;
        }
    }
}

/// <summary>
/// Result of a placement attempt
/// </summary>
public record PlacementResult(bool Success, string Message, int PointsScored);
