using System.Collections.ObjectModel;

namespace Triominos.Models;

/// <summary>
/// Represents a player in the Triominos game
/// </summary>
public class Player
{
    public int Id { get; }
    public string Name { get; set; }
    public int Score { get; set; }
    public ObservableCollection<TriominoPiece> Rack { get; } = [];

    public Player(int id, string name)
    {
        Id = id;
        Name = name;
        Score = 0;
    }

    /// <summary>
    /// Adds a piece to the player's rack
    /// </summary>
    public void AddPiece(TriominoPiece piece)
    {
        Rack.Add(piece);
    }

    /// <summary>
    /// Removes a piece from the player's rack
    /// </summary>
    public bool RemovePiece(TriominoPiece piece)
    {
        return Rack.Remove(piece);
    }

    /// <summary>
    /// Clears all pieces from the player's rack
    /// </summary>
    public void ClearRack()
    {
        Rack.Clear();
    }

    public override string ToString() => Name;
}
