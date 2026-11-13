namespace Triominos.Web.Play;

/// <summary>The colour a player marks their name with on the score panel and the handover screen.</summary>
public enum PlayerColor
{
    Ruby,
    Sapphire,
    Amethyst,
    Coral,
    Turquoise,
    Rose,
}

/// <summary>The palette players choose from: how each colour looks and what it is called.</summary>
public static class PlayerColors
{
    /// <summary>Gets every colour, in the order the setup screen offers them.</summary>
    public static IReadOnlyList<PlayerColor> All { get; } = Enum.GetValues<PlayerColor>();

    /// <summary>Returns the CSS colour of a player colour.</summary>
    public static string Hex(PlayerColor color) => color switch
    {
        PlayerColor.Ruby => "#d64550",
        PlayerColor.Sapphire => "#4f86f7",
        PlayerColor.Amethyst => "#a573e0",
        PlayerColor.Coral => "#f0914f",
        PlayerColor.Turquoise => "#36c2b4",
        PlayerColor.Rose => "#e87bae",
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };

    /// <summary>Returns the Dutch name of a player colour.</summary>
    public static string Label(PlayerColor color) => color switch
    {
        PlayerColor.Ruby => "Robijn",
        PlayerColor.Sapphire => "Saffier",
        PlayerColor.Amethyst => "Amethist",
        PlayerColor.Coral => "Koraal",
        PlayerColor.Turquoise => "Turkoois",
        PlayerColor.Rose => "Roze",
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
    };
}
