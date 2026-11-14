using Triominos.Core.Game;

namespace Triominos.Web.Play;

/// <summary>A player as chosen on the setup screen: a name and a colour.</summary>
/// <param name="Name">Display name, already trimmed.</param>
/// <param name="Color">The colour that marks the player.</param>
public sealed record PlayerSetup(string Name, PlayerColor Color)
{
    /// <summary>Longest name the setup screen accepts; longer names do not fit the score panel.</summary>
    public const int MaxNameLength = 16;

    /// <summary>Gets the players a first game starts with: the fewest players the rules allow.</summary>
    public static IReadOnlyList<PlayerSetup> Default { get; } =
        [.. Enumerable.Range(0, Scoring.MinPlayers).Select(i => new PlayerSetup($"Speler {i + 1}", PlayerColors.All[i]))];
}
