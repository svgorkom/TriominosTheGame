namespace Triominos.Core.Models;

using Triominos.Core.Interfaces;

/// <summary>
/// Represents the draw pile (pool) of pieces.
/// </summary>
public class DrawPile : IDrawPile
{
    private readonly List<IPiece> _pieces = [];
    private readonly Random _random;

    public IReadOnlyList<IPiece> Pieces => _pieces;
    public int Count => _pieces.Count;
    public bool HasPieces => _pieces.Count > 0;

    public DrawPile(Random? random = null)
    {
        _random = random ?? new Random();
    }

    /// <summary>
    /// Generates all unique triomino pieces and shuffles them
    /// </summary>
    internal void Initialize()
    {
        _pieces.Clear();
        
        int id = 0;
        for (int i = 0; i <= 5; i++)
        {
            for (int j = i; j <= 5; j++)
            {
                for (int k = j; k <= 5; k++)
                {
                    _pieces.Add(new Piece(id++, i, j, k));
                }
            }
        }

        Shuffle();
    }

    /// <summary>
    /// Shuffles the draw pile using Fisher-Yates algorithm
    /// </summary>
    internal void Shuffle()
    {
        int n = _pieces.Count;
        while (n > 1)
        {
            n--;
            int k = _random.Next(n + 1);
            (_pieces[k], _pieces[n]) = (_pieces[n], _pieces[k]);
        }
    }

    /// <summary>
    /// Draws a piece from the top of the pile
    /// </summary>
    internal IPiece? DrawPiece()
    {
        if (_pieces.Count == 0)
            return null;

        var piece = _pieces[^1];
        _pieces.RemoveAt(_pieces.Count - 1);
        return piece;
    }

    /// <summary>
    /// Removes a specific piece from the pile
    /// </summary>
    internal bool RemovePiece(IPiece piece)
    {
        return _pieces.Remove(piece);
    }

    /// <summary>
    /// Clears the draw pile
    /// </summary>
    internal void Clear()
    {
        _pieces.Clear();
    }
}
