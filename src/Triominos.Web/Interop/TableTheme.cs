namespace Triominos.Web.Interop;

/// <summary>
/// The look of the table, the menus and the HUD. The names in lower case are the theme names of the
/// scene (<c>scene/src/theme.ts</c>) and the <c>data-theme</c> values in <c>app.css</c>.
/// </summary>
public enum TableTheme
{
    Classic,
    Modern,
    Playful,
    Midnight,
    Porcelain,
}

/// <summary>Maps the theme setting to what the scene and the style sheet take.</summary>
public static class TableThemes
{
    /// <summary>Gets every theme, in the order the settings show them.</summary>
    public static IReadOnlyList<TableTheme> All { get; } = Enum.GetValues<TableTheme>();

    /// <summary>Returns the name of a theme for the scene's <c>setTheme</c> and for <c>data-theme</c>.</summary>
    public static string Name(TableTheme theme) => theme switch
    {
        TableTheme.Classic => "classic",
        TableTheme.Modern => "modern",
        TableTheme.Playful => "playful",
        TableTheme.Midnight => "midnight",
        TableTheme.Porcelain => "porcelain",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };

    /// <summary>Returns the Dutch name of a theme.</summary>
    public static string Label(TableTheme theme) => theme switch
    {
        TableTheme.Classic => "Klassiek",
        TableTheme.Modern => "Modern",
        TableTheme.Playful => "Speels",
        TableTheme.Midnight => "Middernacht",
        TableTheme.Porcelain => "Porselein",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };
}
