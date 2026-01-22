namespace Triominos.Core;

using Triominos.Core.Interfaces;
using Triominos.Core.Models;

/// <summary>
/// Centralized game rules and configuration for Triominos.
/// All game constants and rule logic are defined here.
/// This is completely UI-agnostic.
/// </summary>
public static class GameRules
{
    #region Game Constants

    public const int MaxPlayers = 4;
    public const int MinPlayers = 1;
    public const int PiecesPerPlayer = 7;
    public const int MaxPieceValue = 5;
    public const int MinPieceValue = 0;
    public const int TotalPieces = 56;
    public const int DefaultBoardRows = 12;
    public const int DefaultBoardCols = 24;

    #endregion

    #region Scoring Constants

    public const int FirstMoveTripleBonus = 10;
    public const int BridgeBonus = 40;
    public const int HexagonBonus = 50;
    public const int DrawPenalty = 5;
    public const int MaxDrawsPerTurn = 3;
    public const int GoingOutBonus = 25;

    #endregion

    #region Validation Rules

    public static bool IsValidPlayerCount(int count) 
        => count >= MinPlayers && count <= MaxPlayers;

    public static bool IsPointingUp(int row, int col) 
        => (row + col) % 2 == 0;

    public static bool EdgesMatch((int val1, int val2) edge1, (int val1, int val2) edge2) 
        => edge1.val1 == edge2.val2 && edge1.val2 == edge2.val1;

    #endregion

    #region Scoring Calculations

    public static int CalculateBaseScore(IPiece piece) 
        => piece.Value1 + piece.Value2 + piece.Value3;

    public static int CalculatePlacementScore(
        IPiece piece,
        bool isFirstMove,
        int matchingEdges,
        bool completesHexagon)
    {
        int points = CalculateBaseScore(piece);

        if (isFirstMove && piece.IsTriple)
            points += FirstMoveTripleBonus;

        if (matchingEdges >= 2)
            points += BridgeBonus;

        if (completesHexagon)
            points += HexagonBonus;

        return points;
    }

    public static string GetPlacementMessage(
        IPiece piece, 
        int points, 
        bool isFirstMove, 
        int matchingEdges, 
        bool completedHexagon)
    {
        var message = $"Placed {piece} for {points} points";

        if (isFirstMove && piece.IsTriple)
            message += " (Triple bonus!)";
        else if (completedHexagon)
            message += " (Hexagon bonus!)";
        else if (matchingEdges >= 2)
            message += " (Bridge bonus!)";

        return message;
    }

    #endregion

    #region Placement Validation

    /// <summary>
    /// Validates if a piece can be placed at the specified position
    /// </summary>
    public static PlacementValidationResult ValidatePlacement(
        IPiece piece,
        int row,
        int col,
        GameBoard board,
        bool isFirstMove)
    {
        // Set orientation
        if (piece is Piece mutablePiece)
        {
            mutablePiece.IsPointingUp = IsPointingUp(row, col);
        }

        if (board.IsOccupied(row, col))
        {
            return PlacementValidationResult.Failed("Cell is already occupied");
        }

        if (isFirstMove)
        {
            return PlacementValidationResult.Valid(0);
        }

        var tempPlaced = new PlacedPiece(piece, row, col);
        var adjacentPositions = tempPlaced.GetAdjacentPositions().ToList();

        int matchingEdges = 0;
        bool hasAdjacentPiece = false;

        foreach (var (adjRow, adjCol) in adjacentPositions)
        {
            var adjacentPiece = board.GetPlacedPieceAt(adjRow, adjCol);
            if (adjacentPiece == null)
                continue;

            hasAdjacentPiece = true;

            var thisEdge = tempPlaced.GetEdgeFacing(adjRow, adjCol);
            var otherEdge = adjacentPiece.GetEdgeFacing(row, col);

            if (EdgesMatch(thisEdge, otherEdge))
            {
                matchingEdges++;
            }
            else
            {
                return PlacementValidationResult.Failed(
                    $"Edge mismatch! Your edge [{thisEdge.val1}-{thisEdge.val2}] doesn't match [{otherEdge.val1}-{otherEdge.val2}]");
            }
        }

        if (!hasAdjacentPiece)
        {
            return PlacementValidationResult.Failed("Piece must be placed adjacent to an existing piece");
        }

        return PlacementValidationResult.Valid(matchingEdges);
    }

    /// <summary>
    /// Checks if placing a piece completes a hexagon
    /// </summary>
    public static bool CheckHexagonCompletion(int row, int col, GameBoard board)
    {
        foreach (var center in GetPotentialHexagonCenters(row, col))
        {
            if (IsHexagonComplete(center.row, center.col, board))
                return true;
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

    private static bool IsHexagonComplete(int centerRow, int centerCol, GameBoard board)
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

        return positions.All(p => board.IsOccupied(p.row, p.col));
    }

    #endregion

    #region Valid Placements Query

    /// <summary>
    /// Gets all valid placement positions for a piece
    /// </summary>
    public static IEnumerable<(int row, int col)> GetValidPlacements(
        IPiece piece,
        GameBoard board)
    {
        if (board.PiecesPlaced == 0)
        {
            yield return (board.Rows / 2, board.Cols / 2);
            yield break;
        }

        var checkedPositions = new HashSet<(int, int)>();

        foreach (var placedPiece in board.PlacedPieces.OfType<PlacedPiece>())
        {
            foreach (var adj in placedPiece.GetAdjacentPositions())
            {
                if (checkedPositions.Contains(adj) || board.IsOccupied(adj.row, adj.col))
                    continue;

                if (!board.IsValidPosition(adj.row, adj.col))
                    continue;

                checkedPositions.Add(adj);

                // Try all 3 rotations
                for (int r = 0; r < 3; r++)
                {
                    var testPiece = (Piece)piece.Clone();
                    testPiece.IsPointingUp = IsPointingUp(adj.row, adj.col);
                    
                    for (int i = 0; i < r; i++)
                        testPiece.Rotate();
                    
                    var result = ValidatePlacement(testPiece, adj.row, adj.col, board, false);
                    if (result.IsValid)
                    {
                        yield return adj;
                        break;
                    }
                }
            }
        }
    }

    #endregion
}

/// <summary>
/// Result of validating a piece placement
/// </summary>
public record PlacementValidationResult(bool IsValid, string Message, int MatchingEdges)
{
    public static PlacementValidationResult Valid(int matchingEdges) 
        => new(true, "Valid placement", matchingEdges);
    
    public static PlacementValidationResult Failed(string message) 
        => new(false, message, 0);
}
