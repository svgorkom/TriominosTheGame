using Triominos.Web.Interop;
using Triominos.Web.Play;
using Triominos.Web.Storage;

namespace Triominos.Web.Tests;

public class PreferencesTests
{
    private readonly MemoryStore _store = new();

    private Preferences Preferences => new(_store);

    [Fact]
    public async Task Nothing_stored_gives_the_default_settings_and_players()
    {
        Assert.Equal(PlaySettings.Default, await Preferences.LoadSettingsAsync());
        Assert.Equal(PlayerSetup.Default, await Preferences.LoadPlayersAsync());
    }

    [Fact]
    public async Task Saved_settings_come_back()
    {
        var settings = new PlaySettings(SceneQuality.High, AnimationSpeed.VeryFast, false, 35);

        await Preferences.SaveSettingsAsync(settings);

        Assert.Equal(settings, await Preferences.LoadSettingsAsync());
    }

    [Fact]
    public async Task A_damaged_value_falls_back_to_its_own_default_and_leaves_the_others_alone()
    {
        _store.Values[Storage.Preferences.QualityKey] = "Gigantic";
        _store.Values[Storage.Preferences.SpeedKey] = "Fast";
        _store.Values[Storage.Preferences.SoundKey] = "maybe";
        _store.Values[Storage.Preferences.VolumeKey] = "150";

        var settings = await Preferences.LoadSettingsAsync();

        Assert.Equal(new PlaySettings(PlaySettings.Default.Quality, AnimationSpeed.Fast, PlaySettings.Default.SoundOn, PlaySettings.Default.Volume), settings);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("60", 60)]
    [InlineData("100", 100)]
    [InlineData("101", 60)]
    [InlineData("-5", 60)]
    [InlineData("6 0", 60)]
    [InlineData("loud", 60)]
    [InlineData("", 60)]
    [InlineData(null, 60)]
    public void The_volume_must_be_a_whole_number_from_0_to_100(string? stored, int expected)
    {
        Assert.Equal(expected, Storage.Preferences.ParseVolume(stored, 60));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("medium")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_quality_is_the_default(string? stored)
    {
        Assert.Equal(SceneQuality.Medium, Storage.Preferences.ParseEnum(stored, SceneQuality.Medium));
    }

    [Fact]
    public async Task Players_come_back_with_names_that_hold_separators_and_accents()
    {
        PlayerSetup[] players =
        [
            new("Zoë: de, eerste", PlayerColor.Coral),
            new("100% Bea", PlayerColor.Rose),
            new("Cleo", PlayerColor.Turquoise),
        ];

        await Preferences.SavePlayersAsync(players);

        Assert.Equal(players, await Preferences.LoadPlayersAsync());
    }

    [Theory]
    [InlineData("Ruby:Ann")]
    [InlineData("Ruby:A,Sapphire:B,Amethyst:C,Coral:D,Turquoise:E")]
    [InlineData("Ruby:Ann,Ruby:Bob")]
    [InlineData("Ruby:Ann,Sapphire:ann")]
    [InlineData("Ruby:Ann,Purple:Bob")]
    [InlineData("99:Ann,Sapphire:Bob")]
    [InlineData("Ruby:Ann,Sapphire:")]
    [InlineData("Ruby:Ann,Sapphire:ABCDEFGHIJKLMNOPQ")]
    [InlineData("Ruby:Ann,Sapphire:%20Bob")]
    [InlineData("Ruby:Ann,Sapphire:Bob:Extra")]
    [InlineData("Ruby,Sapphire")]
    [InlineData("not what we wrote")]
    [InlineData("")]
    public async Task Players_that_are_damaged_or_not_allowed_are_the_default(string stored)
    {
        _store.Values[Storage.Preferences.PlayersKey] = stored;

        Assert.Null(Storage.Preferences.ParsePlayers(stored));
        Assert.Equal(PlayerSetup.Default, await Preferences.LoadPlayersAsync());
    }

    [Fact]
    public void Two_to_four_players_are_accepted()
    {
        for (var count = 2; count <= 4; count++)
        {
            var players = Enumerable.Range(0, count).Select(i => new PlayerSetup($"Speler {i + 1}", PlayerColors.All[i])).ToArray();

            Assert.Equal(players, Storage.Preferences.ParsePlayers(Storage.Preferences.FormatPlayers(players)));
        }
    }
}
