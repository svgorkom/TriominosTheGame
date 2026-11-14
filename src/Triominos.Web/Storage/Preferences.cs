using System.Globalization;
using Triominos.Core.Ai;
using Triominos.Core.Game;
using Triominos.Web.Interop;
using Triominos.Web.Play;

namespace Triominos.Web.Storage;

/// <summary>The settings that are kept between visits.</summary>
/// <param name="Quality">Rendering quality.</param>
/// <param name="Speed">Animation speed.</param>
/// <param name="SoundOn">Whether the sounds are on.</param>
/// <param name="Volume">Volume, 0 to 100.</param>
/// <param name="Theme">Look of the table, the menus and the HUD.</param>
public sealed record PlaySettings(SceneQuality Quality, AnimationSpeed Speed, bool SoundOn, int Volume, TableTheme Theme)
{
    /// <summary>Gets the settings a first visit starts with.</summary>
    public static PlaySettings Default { get; } = new(SceneQuality.Medium, AnimationSpeed.Normal, true, 60, TableTheme.Classic);
}

/// <summary>
/// Reads and writes the settings and the last players. Each value has a key of its own and a value
/// that is missing, damaged or out of range falls back to the default of that value alone.
/// </summary>
public sealed class Preferences(IPreferenceStore store)
{
    internal const string QualityKey = "triominos.quality";
    internal const string SpeedKey = "triominos.speed";
    internal const string SoundKey = "triominos.sound";
    internal const string VolumeKey = "triominos.volume";

    /// <summary>The key of the theme; <c>index.html</c> reads it too, for the loading screen.</summary>
    internal const string ThemeKey = "triominos.theme";
    internal const string PlayersKey = "triominos.players";
    private const string ComputerMark = "computer";

    /// <summary>Loads the settings.</summary>
    public async Task<PlaySettings> LoadSettingsAsync()
    {
        var fallback = PlaySettings.Default;
        return new PlaySettings(
            ParseEnum(await store.GetAsync(QualityKey), fallback.Quality),
            ParseEnum(await store.GetAsync(SpeedKey), fallback.Speed),
            ParseSound(await store.GetAsync(SoundKey), fallback.SoundOn),
            ParseVolume(await store.GetAsync(VolumeKey), fallback.Volume),
            ParseEnum(await store.GetAsync(ThemeKey), fallback.Theme));
    }

    /// <summary>Stores the settings.</summary>
    /// <param name="settings">The settings.</param>
    public async Task SaveSettingsAsync(PlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await store.SetAsync(QualityKey, settings.Quality.ToString());
        await store.SetAsync(SpeedKey, settings.Speed.ToString());
        await store.SetAsync(SoundKey, settings.SoundOn ? "on" : "off");
        await store.SetAsync(VolumeKey, settings.Volume.ToString(CultureInfo.InvariantCulture));
        await store.SetAsync(ThemeKey, settings.Theme.ToString());
    }

    /// <summary>Loads the players of the last game; the default players when there are none or they are not valid.</summary>
    public async Task<IReadOnlyList<PlayerSetup>> LoadPlayersAsync() =>
        ParsePlayers(await store.GetAsync(PlayersKey)) ?? PlayerSetup.Default;

    /// <summary>Stores the players of the game that starts.</summary>
    /// <param name="players">The players in turn order.</param>
    public async Task SavePlayersAsync(IReadOnlyList<PlayerSetup> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        await store.SetAsync(PlayersKey, FormatPlayers(players));
    }

    /// <summary>
    /// Writes players as <c>Color:name</c> items separated by commas; names are escaped. A computer
    /// player gets a third part, <c>Color:name:computer</c>, so a stored human stays as it was, and
    /// a fourth, <c>easy</c> or <c>strong</c>, unless it plays at the normal level.
    /// </summary>
    internal static string FormatPlayers(IReadOnlyList<PlayerSetup> players) =>
        string.Join(',', players.Select(p => $"{p.Color}:{Uri.EscapeDataString(p.Name)}{ComputerPart(p)}"));

    private static string ComputerPart(PlayerSetup player)
    {
        if (!player.IsComputer)
        {
            return string.Empty;
        }

        return player.Level == ComputerLevel.Normal ? $":{ComputerMark}" : $":{ComputerMark}:{player.Level.ToString().ToLowerInvariant()}";
    }

    private static bool TryParseLevel(string text, out ComputerLevel level)
    {
        switch (text)
        {
            case "easy":
                level = ComputerLevel.Easy;
                return true;
            case "normal":
                level = ComputerLevel.Normal;
                return true;
            case "strong":
                level = ComputerLevel.Strong;
                return true;
            default:
                level = ComputerLevel.Normal;
                return false;
        }
    }

    /// <summary>
    /// Reads players written by <see cref="FormatPlayers"/>. Returns null unless there are 2 to 4
    /// players with valid names (see <see cref="PlayerNameCheck"/>), short enough names, different,
    /// known colours and at least one human.
    /// </summary>
    internal static IReadOnlyList<PlayerSetup>? ParsePlayers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var players = new List<PlayerSetup>();
        foreach (var item in text.Split(','))
        {
            var parts = item.Split(':');
            var level = ComputerLevel.Normal;
            if (parts.Length is < 2 or > 4
                || !Enum.TryParse<PlayerColor>(parts[0], out var color)
                || !Enum.IsDefined(color)
                || (parts.Length >= 3 && parts[2] != ComputerMark)
                || (parts.Length == 4 && !TryParseLevel(parts[3], out level)))
            {
                return null;
            }

            players.Add(new PlayerSetup(Uri.UnescapeDataString(parts[1]), color, parts.Length >= 3, level));
        }

        var names = players.Select(p => p.Name).ToArray();
        var valid = players.Count is >= Scoring.MinPlayers and <= Scoring.MaxPlayers
            && names.All(n => n == n.Trim() && n.Length <= PlayerSetup.MaxNameLength)
            && PlayerNameCheck.Find(names) is null
            && players.Select(p => p.Color).Distinct().Count() == players.Count
            && players.Any(p => !p.IsComputer);
        return valid ? players : null;
    }

    internal static T ParseEnum<T>(string? text, T fallback)
        where T : struct, Enum =>
        Enum.TryParse<T>(text, out var value) && Enum.IsDefined(value) ? value : fallback;

    internal static bool ParseSound(string? text, bool fallback) => text switch
    {
        "on" => true,
        "off" => false,
        _ => fallback,
    };

    internal static int ParseVolume(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var volume) && volume is >= 0 and <= 100 ? volume : fallback;
}
