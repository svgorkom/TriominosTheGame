using Triominos.Core.Game;

namespace Triominos.Web.Play;

/// <summary>
/// The difficulty levels as the setup screen shows them, and the help each level gives in the UI.
/// The house rules of each level are in <see cref="Difficulties"/>.
/// </summary>
public static class DifficultyChoices
{
    /// <summary>Returns the Dutch name of a level.</summary>
    /// <param name="difficulty">The level.</param>
    public static string Label(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => "Makkelijk",
        Difficulty.Normal => "Normaal",
        Difficulty.Hard => "Moeilijk",
        Difficulty.VeryHard => "Zeer moeilijk",
        _ => difficulty.ToString(),
    };

    /// <summary>Returns a short Dutch description of what a level changes.</summary>
    /// <param name="difficulty">The level.</param>
    public static string Description(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy =>
            "5 gratis hints, stenen die passen zijn gemarkeerd, je ziet waar een steen past en wat hij oplevert, "
            + "de steen draait vanzelf goed, de stenen van de computer liggen open en je mag je laatste zet terugnemen.",
        Difficulty.Normal => "De gewone regels met 3 hints van 10 punten. Geen klok.",
        Difficulty.Hard => "1 hint van 15 punten en 60 seconden per beurt. Is de tijd op, dan trek je een steen.",
        Difficulty.VeryHard =>
            "Geen hints, geen oplichtende plekken, 8 minuten bedenktijd voor het hele spel (daarna 15 seconden per beurt) "
            + "en een steen die niet past kost 5 punten.",
        _ => string.Empty,
    };

    /// <summary>Gets whether the open cells light up when a tile is picked up; not at the hardest level.</summary>
    /// <param name="difficulty">The level.</param>
    public static bool ShowsOpenCells(Difficulty difficulty) => difficulty != Difficulty.VeryHard;

    /// <summary>
    /// Gets whether the UI helps the player: tiles that fit are marked, the cells where a tile fits
    /// light up brighter with their points, the tile turns by itself, and computer racks are open.
    /// Only at the easiest level.
    /// </summary>
    /// <param name="difficulty">The level.</param>
    public static bool Assists(Difficulty difficulty) => difficulty == Difficulty.Easy;
}
