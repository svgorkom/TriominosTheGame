namespace Triominos.Core.Events;

using Triominos.Core.Interfaces;

/// <summary>
/// Event arguments for game state changes
/// </summary>
public class GameStateChangedEventArgs : EventArgs
{
    public GamePhase NewPhase { get; }
    public string Message { get; }

    public GameStateChangedEventArgs(GamePhase newPhase, string message)
    {
        NewPhase = newPhase;
        Message = message;
    }
}

/// <summary>
/// Event arguments for when a piece is placed
/// </summary>
public class PiecePlacedEventArgs : EventArgs
{
    public IPlacedPiece PlacedPiece { get; }
    public int PointsScored { get; }
    public string Message { get; }

    public PiecePlacedEventArgs(IPlacedPiece placedPiece, int pointsScored, string message)
    {
        PlacedPiece = placedPiece;
        PointsScored = pointsScored;
        Message = message;
    }
}

/// <summary>
/// Event arguments for when the current player changes
/// </summary>
public class PlayerChangedEventArgs : EventArgs
{
    public IPlayer? PreviousPlayer { get; }
    public IPlayer NewPlayer { get; }

    public PlayerChangedEventArgs(IPlayer? previousPlayer, IPlayer newPlayer)
    {
        PreviousPlayer = previousPlayer;
        NewPlayer = newPlayer;
    }
}

/// <summary>
/// Event arguments for when the game ends
/// </summary>
public class GameEndedEventArgs : EventArgs
{
    public IPlayer? Winner { get; }
    public GameEndReason Reason { get; }
    public IReadOnlyList<IPlayer> FinalStandings { get; }

    public GameEndedEventArgs(IPlayer? winner, GameEndReason reason, IReadOnlyList<IPlayer> finalStandings)
    {
        Winner = winner;
        Reason = reason;
        FinalStandings = finalStandings;
    }
}

/// <summary>
/// Event arguments for piece selection changes
/// </summary>
public class PieceSelectionChangedEventArgs : EventArgs
{
    public IPiece? SelectedPiece { get; }
    public bool IsFromPool { get; }

    public PieceSelectionChangedEventArgs(IPiece? selectedPiece, bool isFromPool)
    {
        SelectedPiece = selectedPiece;
        IsFromPool = isFromPool;
    }
}

/// <summary>
/// Reasons why a game might end
/// </summary>
public enum GameEndReason
{
    PlayerWentOut,
    AllPlayersBlocked,
    NoMoreMoves
}
