using Triominos.Core.Tiles;

namespace Triominos.Core.Game;

/// <summary>A player: name, score and the tiles in their rack.</summary>
public sealed class Player
{
    private readonly List<Tile> _rack;

    internal Player(string name, IEnumerable<Tile> rack)
    {
        Name = name;
        _rack = [.. rack];
    }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }

    /// <summary>Gets the score; it can be negative.</summary>
    public int Score { get; internal set; }

    /// <summary>Gets how many hints the player took this game.</summary>
    public int HintsUsed { get; internal set; }

    /// <summary>Gets how many hints the player may still take this game.</summary>
    public int HintsLeft => Scoring.MaxHintsPerGame - HintsUsed;

    /// <summary>Gets the tiles in the rack.</summary>
    public IReadOnlyList<Tile> Rack => _rack;

    /// <summary>Gets the sum of the values of the tiles in the rack.</summary>
    public int RackValue => _rack.Sum(t => t.Value);

    internal Tile? FindTile(int tileId) => _rack.Find(t => t.Id == tileId);

    internal void AddTile(Tile tile) => _rack.Add(tile);

    internal void RemoveTile(Tile tile) => _rack.Remove(tile);
}
