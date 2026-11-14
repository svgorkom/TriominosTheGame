namespace Triominos.Core.Game;

/// <summary>What is wrong with a player name.</summary>
public enum PlayerNameProblem
{
    /// <summary>The name is empty or only whitespace.</summary>
    Blank,

    /// <summary>Another player earlier in the turn order has the same name.</summary>
    Duplicate,
}

/// <summary>A problem with the name of one player.</summary>
/// <param name="Index">Position of the player in the turn order, starting at 0.</param>
/// <param name="Problem">What is wrong.</param>
public readonly record struct PlayerNameIssue(int Index, PlayerNameProblem Problem);

/// <summary>
/// Checks the names a game starts with. Names are shown in messages and on the score panel, so
/// every player needs a name that tells them apart.
/// </summary>
public static class PlayerNameCheck
{
    /// <summary>
    /// Finds the first problem in a list of names. Names are compared without surrounding
    /// whitespace and without regard to case.
    /// </summary>
    /// <param name="names">The names in turn order.</param>
    /// <returns>The first problem, or null when every name is usable.</returns>
    public static PlayerNameIssue? Find(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(names[i]))
            {
                return new PlayerNameIssue(i, PlayerNameProblem.Blank);
            }

            if (!seen.Add(names[i].Trim()))
            {
                return new PlayerNameIssue(i, PlayerNameProblem.Duplicate);
            }
        }

        return null;
    }
}
