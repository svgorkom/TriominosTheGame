using Triominos.Core.Ai;

namespace Triominos.Web.Play;

/// <summary>The Dutch names of the computer levels, for the setup screen.</summary>
public static class ComputerLevels
{
    /// <summary>Gets every level, easiest first.</summary>
    public static IReadOnlyList<ComputerLevel> All { get; } = Enum.GetValues<ComputerLevel>();

    /// <summary>Returns the Dutch name of a level.</summary>
    public static string Label(ComputerLevel level) => level switch
    {
        ComputerLevel.Easy => "Makkelijk",
        ComputerLevel.Normal => "Normaal",
        ComputerLevel.Strong => "Sterk",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
