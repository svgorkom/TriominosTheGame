namespace Triominos.Core.Models;

using Triominos.Core.Interfaces;

/// <summary>
/// Represents a player in the game.
/// </summary>
public class Player : IPlayer
{
    private readonly List<IPiece> _rack = [];
    private bool _isCurrentPlayer;

    public int Id { get; }
    public string Name { get; set; }
    public int Score { get; internal set; }
    
    public IReadOnlyList<IPiece> Rack => _rack;
    public int PiecesRemaining => _rack.Count;
    
    public bool IsCurrentPlayer
    {
        get => _isCurrentPlayer;
        internal set => _isCurrentPlayer = value;
    }

    public Player(int id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>
    /// Adds a piece to the player's rack
    /// </summary>
    internal void AddPiece(IPiece piece)
    {
        _rack.Add(piece);
    }

    /// <summary>
    /// Removes a piece from the player's rack
    /// </summary>
    internal bool RemovePiece(IPiece piece)
    {
        return _rack.Remove(piece);
    }

    /// <summary>
    /// Clears all pieces from the rack
    /// </summary>
    internal void ClearRack()
    {
        _rack.Clear();
    }

    /// <summary>
    /// Adds points to the player's score
    /// </summary>
    internal void AddScore(int points)
    {
        Score += points;
    }

    /// <summary>
    /// Resets the player's score
    /// </summary>
    internal void ResetScore()
    {
        Score = 0;
    }

    public override string ToString() => $"{Name} (Score: {Score}, Pieces: {PiecesRemaining})";
}
