namespace Triominos.Core.Game;

/// <summary>Scoring and setup constants of the published Triominos rules.</summary>
public static class Scoring
{
    /// <summary>Fewest players.</summary>
    public const int MinPlayers = 2;

    /// <summary>Most players.</summary>
    public const int MaxPlayers = 4;

    /// <summary>Bonus for starting with a triple.</summary>
    public const int StartTripleBonus = 10;

    /// <summary>Extra bonus for starting with 0-0-0, on top of <see cref="StartTripleBonus"/>.</summary>
    public const int StartTripleZeroBonus = 30;

    /// <summary>Bonus for a bridge.</summary>
    public const int BridgeBonus = 40;

    /// <summary>Penalty per tile drawn.</summary>
    public const int DrawPenalty = 5;

    /// <summary>Most tiles a player may draw in one turn.</summary>
    public const int MaxDrawsPerTurn = 3;

    /// <summary>Penalty for passing: after three draws, or with an empty pool.</summary>
    public const int PassPenalty = 10;

    /// <summary>Penalty for asking for a hint: the same as passing, so a hint is never cheaper than giving up the turn.</summary>
    public const int HintPenalty = 10;

    /// <summary>Most hints one player may take in a game.</summary>
    public const int MaxHintsPerGame = 3;

    /// <summary>Bonus for playing your last tile, on top of the tile values left in other racks.</summary>
    public const int GoingOutBonus = 25;

    /// <summary>Gets the number of tiles dealt to each player.</summary>
    public static int TilesPerPlayer(int playerCount) => playerCount == 2 ? 9 : 7;

    /// <summary>Gets the bonus for completing 0 to 3 hexagons with one tile (50, 60, 70).</summary>
    public static int HexagonBonus(int hexagons)
    {
        return hexagons switch
        {
            0 => 0,
            1 => 50,
            2 => 60,
            3 => 70,
            _ => throw new ArgumentOutOfRangeException(nameof(hexagons), hexagons, "A tile completes at most three hexagons."),
        };
    }
}

/// <summary>Points earned by one placement.</summary>
/// <param name="TileValue">Sum of the tile's corners.</param>
/// <param name="StartBonus">Bonus for the starting tile.</param>
/// <param name="BridgeBonus">Bonus for a bridge.</param>
/// <param name="HexagonBonus">Bonus for completed hexagons.</param>
public readonly record struct ScoreBreakdown(int TileValue, int StartBonus, int BridgeBonus, int HexagonBonus)
{
    /// <summary>Gets the total points.</summary>
    public int Total => TileValue + StartBonus + BridgeBonus + HexagonBonus;
}
