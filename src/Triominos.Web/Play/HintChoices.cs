namespace Triominos.Web.Play;

/// <summary>The values the setup screen offers for the hint rules; a stored value outside them is not used.</summary>
public static class HintChoices
{
    /// <summary>Gets the numbers of hints per player the setup screen offers; 0 turns hints off.</summary>
    public static IReadOnlyList<int> Counts { get; } = [0, 1, 2, 3, 4, 5];

    /// <summary>Gets the penalties the setup screen offers; 0 makes a hint free.</summary>
    public static IReadOnlyList<int> Penalties { get; } = [0, 5, 10, 15, 20];

    /// <summary>Returns the Dutch text for a number of hints.</summary>
    /// <param name="count">Hints per player.</param>
    public static string CountLabel(int count) => count == 0 ? "Uit" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Returns the Dutch text for a penalty.</summary>
    /// <param name="penalty">Points a hint costs.</param>
    public static string PenaltyLabel(int penalty) => penalty == 0 ? "Gratis" : $"{penalty} punten";
}
