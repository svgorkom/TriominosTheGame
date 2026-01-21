namespace Triominos.Models;

/// <summary>
/// Centralized game rules and configuration for Triominos.
/// All game constants and rule logic are defined here.
/// </summary>
public static class GameRules
{
    #region Game Constants

    /// <summary>
    /// Maximum number of players allowed in a game
    /// </summary>
    public const int MaxPlayers = 4;

    /// <summary>
    /// Minimum number of players allowed in a game
    /// </summary>
    public const int MinPlayers = 1;

    /// <summary>
    /// Number of pieces each player receives at the start
    /// </summary>
    public const int PiecesPerPlayer = 7;

    /// <summary>
    /// Maximum value on any corner of a triomino piece (0-5)
    /// </summary>
    public const int MaxPieceValue = 5;

    /// <summary>
    /// Minimum value on any corner of a triomino piece
    /// </summary>
    public const int MinPieceValue = 0;

    /// <summary>
    /// Total number of unique triomino pieces in a standard set
    /// </summary>
    public const int TotalPieces = 56;

    #endregion

    #region Scoring Rules

    /// <summary>
    /// Bonus points for playing a triple piece on the first move
    /// </summary>
    public const int FirstMoveTripleBonus = 10;

    /// <summary>
    /// Bonus points for bridging (matching 2+ edges)
    /// </summary>
    public const int BridgeBonus = 40;

    /// <summary>
    /// Bonus points for completing a hexagon
    /// </summary>
    public const int HexagonBonus = 50;

    /// <summary>
    /// Penalty points for drawing from the pool (per piece drawn)
    /// </summary>
    public const int DrawPenalty = 5;

    /// <summary>
    /// Maximum number of pieces a player can draw in one turn
    /// </summary>
    public const int MaxDrawsPerTurn = 3;

    /// <summary>
    /// Bonus points for going out (emptying your rack)
    /// </summary>
    public const int GoingOutBonus = 25;

    #endregion

    #region Validation Rules

    /// <summary>
    /// Validates if a player count is valid
    /// </summary>
    public static bool IsValidPlayerCount(int playerCount)
        => playerCount >= MinPlayers && playerCount <= MaxPlayers;

    /// <summary>
    /// Determines if a piece can be placed at a given position (orientation check)
    /// </summary>
    public static bool IsPointingUp(int row, int col)
        => (row + col) % 2 == 0;

    /// <summary>
    /// Checks if two edges match according to Triominos rules.
    /// Edges match when the values are reversed (like fitting puzzle pieces).
    /// </summary>
    public static bool EdgesMatch((int val1, int val2) edge1, (int val1, int val2) edge2)
        => edge1.val1 == edge2.val2 && edge1.val2 == edge2.val1;

    /// <summary>
    /// Validates if piece values are within allowed range
    /// </summary>
    public static bool IsValidPieceValue(int value)
        => value >= MinPieceValue && value <= MaxPieceValue;

    #endregion

    #region Scoring Calculations

    /// <summary>
    /// Calculates the base score for a piece (sum of all corner values)
    /// </summary>
    public static int CalculateBaseScore(TriominoPiece piece)
        => piece.Value1 + piece.Value2 + piece.Value3;

    /// <summary>
    /// Calculates the total score for placing a piece
    /// </summary>
    public static int CalculatePlacementScore(
        TriominoPiece piece,
        bool isFirstMove,
        int matchingEdges,
        bool completesHexagon)
    {
        int points = CalculateBaseScore(piece);

        // First move triple bonus
        if (isFirstMove && piece.IsTriple)
        {
            points += FirstMoveTripleBonus;
        }

        // Bridge bonus for matching 2+ edges
        if (matchingEdges >= 2)
        {
            points += BridgeBonus;
        }

        // Hexagon completion bonus
        if (completesHexagon)
        {
            points += HexagonBonus;
        }

        return points;
    }

    /// <summary>
    /// Calculates the final score bonus for a player going out
    /// </summary>
    public static int CalculateGoingOutBonus(IEnumerable<Player> otherPlayers)
    {
        // Player gets the sum of remaining pieces from all other players, plus the going out bonus
        int bonus = GoingOutBonus;
        foreach (var player in otherPlayers)
        {
            foreach (var piece in player.Rack)
            {
                bonus += CalculateBaseScore(piece);
            }
        }
        return bonus;
    }

    #endregion

    #region Turn Rules

    /// <summary>
    /// Determines if a player can draw from the pool
    /// </summary>
    public static bool CanDrawFromPool(int poolCount, int drawsThisTurn)
        => poolCount > 0 && drawsThisTurn < MaxDrawsPerTurn;

    /// <summary>
    /// Determines if a player must pass (no valid moves and can't draw)
    /// </summary>
    public static bool MustPass(bool hasValidMoves, int poolCount, int drawsThisTurn)
        => !hasValidMoves && !CanDrawFromPool(poolCount, drawsThisTurn);

    /// <summary>
    /// Determines if the game is over
    /// </summary>
    public static GameEndResult CheckGameOver(IReadOnlyList<Player> players, int poolCount)
    {
        // Check if any player has emptied their rack
        var winner = players.FirstOrDefault(p => p.Rack.Count == 0);
        if (winner != null)
        {
            return new GameEndResult(true, GameEndReason.PlayerWentOut, winner);
        }

        // Check if all players are blocked (no valid moves and empty pool)
        // This would need to be checked by the caller with board state
        
        return new GameEndResult(false, GameEndReason.None, null);
    }

    #endregion

    #region Piece Generation

    /// <summary>
    /// Generates all unique triomino pieces for a standard game set
    /// </summary>
    public static List<TriominoPiece> GenerateAllPieces()
    {
        var pieces = new List<TriominoPiece>();
        int id = 0;

        for (int i = MinPieceValue; i <= MaxPieceValue; i++)
        {
            for (int j = i; j <= MaxPieceValue; j++)
            {
                for (int k = j; k <= MaxPieceValue; k++)
                {
                    pieces.Add(new TriominoPiece(id++, i, j, k));
                }
            }
        }

        return pieces;
    }

    /// <summary>
    /// Shuffles a list of pieces using Fisher-Yates algorithm
    /// </summary>
    public static void ShufflePieces(List<TriominoPiece> pieces, Random random)
    {
        int n = pieces.Count;
        while (n > 1)
        {
            n--;
            int k = random.Next(n + 1);
            (pieces[k], pieces[n]) = (pieces[n], pieces[k]);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets a descriptive message for a placement result
    /// </summary>
    public static string GetPlacementMessage(TriominoPiece piece, int points, bool isFirstMove, int matchingEdges, bool completedHexagon)
    {
        var message = $"Placed {piece} for {points} points";

        if (isFirstMove && piece.IsTriple)
        {
            message += " (Triple bonus!)";
        }
        else if (completedHexagon)
        {
            message += " (Hexagon bonus!)";
        }
        else if (matchingEdges >= 2)
        {
            message += " (Bridge bonus!)";
        }

        return message;
    }

    #endregion
}

/// <summary>
/// Represents the result of checking if the game has ended
/// </summary>
public record GameEndResult(bool IsGameOver, GameEndReason Reason, Player? Winner);

/// <summary>
/// Reasons why a game might end
/// </summary>
public enum GameEndReason
{
    None,
    PlayerWentOut,
    AllPlayersBlocked,
    NoMoreMoves
}
