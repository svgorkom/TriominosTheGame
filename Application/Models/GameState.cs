namespace Triominos.Models;

/// <summary>
/// Manages the game state, scoring, and rule validation for Triominos
/// </summary>
public class GameState
{
    private readonly Dictionary<(int row, int col), PlacedTriomino> _board = new();

    public int Score { get; private set; }
    public int PiecesPlaced => _board.Count;
    public bool IsFirstMove => _board.Count == 0;

    /// <summary>
    /// Attempts to place a piece at the specified position
    /// </summary>
    public PlacementResult TryPlacePiece(TriominoPiece piece, int row, int col)
    {
        // Set the correct orientation based on grid position
        piece.IsPointingUp = (row + col) % 2 == 0;

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

        int points = CalculateScore(piece, isFirstMove, matchingEdges, row, col);
        Score += points;

        return new PlacementResult(true, GetPlacementMessage(piece, points, isFirstMove), points);
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

            if (TriominoPiece.EdgeMatches(thisEdge, otherEdge))
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

    private int CalculateScore(TriominoPiece piece, bool isFirstMove, int matchingEdges, int row, int col)
    {
        int points = piece.PointValue;

        if (isFirstMove && piece.IsTriple)
        {
            points += 10;
        }

        // Bridge bonus for matching 2+ edges
        if (matchingEdges >= 2)
        {
            points += 40;
        }

        if (CheckHexagonCompletion(row, col))
        {
            points += 50;
        }

        return points;
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

    private static string GetPlacementMessage(TriominoPiece piece, int points, bool isFirstMove)
    {
        var message = $"Placed {piece} for {points} points";

        if (isFirstMove && piece.IsTriple)
        {
            message += " (Triple bonus!)";
        }
        else if (points > piece.PointValue)
        {
            message += " (Bonus!)";
        }

        return message;
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
                    testPiece.IsPointingUp = (adj.row + adj.col) % 2 == 0;
                    
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
    }
}

/// <summary>
/// Result of a placement attempt
/// </summary>
public record PlacementResult(bool Success, string Message, int PointsScored);
