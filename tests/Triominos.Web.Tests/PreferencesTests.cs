using Triominos.Core.Ai;
using Triominos.Core.Game;
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

    [Fact]
    public async Task A_computer_player_comes_back_as_a_computer_and_a_human_is_stored_as_before()
    {
        PlayerSetup[] players =
        [
            new("Ann", PlayerColor.Ruby),
            new("Computer", PlayerColor.Sapphire, IsComputer: true),
        ];

        Assert.Equal("Ruby:Ann,Sapphire:Computer:computer", Storage.Preferences.FormatPlayers(players));

        await Preferences.SavePlayersAsync(players);

        Assert.Equal(players, await Preferences.LoadPlayersAsync());
    }

    [Fact]
    public async Task Players_stored_before_the_computer_existed_load_as_humans()
    {
        _store.Values[Storage.Preferences.PlayersKey] = "Ruby:Ann,Sapphire:Bob";

        var players = await Preferences.LoadPlayersAsync();

        Assert.Equal(new[] { "Ann", "Bob" }, players.Select(p => p.Name));
        Assert.All(players, p => Assert.False(p.IsComputer));
    }

    [Theory]
    [InlineData("Ruby:Ann:computer,Sapphire:Bob:computer")]
    [InlineData("Ruby:Ann,Sapphire:Bob:human")]
    public async Task Stored_players_without_a_human_or_with_an_unknown_kind_are_the_default(string stored)
    {
        _store.Values[Storage.Preferences.PlayersKey] = stored;

        Assert.Null(Storage.Preferences.ParsePlayers(stored));
        Assert.Equal(PlayerSetup.Default, await Preferences.LoadPlayersAsync());
    }

    [Theory]
    [InlineData(AnimationSpeed.Slow, 1167)]
    [InlineData(AnimationSpeed.Normal, 700)]
    [InlineData(AnimationSpeed.Fast, 350)]
    [InlineData(AnimationSpeed.VeryFast, 175)]
    public void The_computer_waits_shorter_at_a_higher_animation_speed(AnimationSpeed speed, int milliseconds)
    {
        Assert.Equal(milliseconds, Math.Round(AnimationSpeeds.ComputerPause(speed).TotalMilliseconds));
    }

    [Fact]
    public async Task The_level_of_a_computer_comes_back_and_the_normal_level_is_not_written()
    {
        PlayerSetup[] players =
        [
            new("Ann", PlayerColor.Ruby),
            new("Bot 1", PlayerColor.Sapphire, IsComputer: true, Level: ComputerLevel.Easy),
            new("Bot 2", PlayerColor.Coral, IsComputer: true, Level: ComputerLevel.Strong),
            new("Bot 3", PlayerColor.Rose, IsComputer: true),
        ];

        Assert.Equal(
            "Ruby:Ann,Sapphire:Bot%201:computer:easy,Coral:Bot%202:computer:strong,Rose:Bot%203:computer",
            Storage.Preferences.FormatPlayers(players));

        await Preferences.SavePlayersAsync(players);

        Assert.Equal(players, await Preferences.LoadPlayersAsync());
    }

    [Theory]
    [InlineData("Ruby:Ann,Sapphire:Bob:computer:expert")]
    [InlineData("Ruby:Ann,Sapphire:Bob:computer:1")]
    [InlineData("Ruby:Ann,Sapphire:Bob:computer:easy:more")]
    [InlineData("Ruby:Ann,Sapphire:Bob:human:easy")]
    public async Task Stored_players_with_an_unknown_level_are_the_default(string stored)
    {
        _store.Values[Storage.Preferences.PlayersKey] = stored;

        Assert.Null(Storage.Preferences.ParsePlayers(stored));
        Assert.Equal(PlayerSetup.Default, await Preferences.LoadPlayersAsync());
    }

    [Fact]
    public void The_levels_have_dutch_names_in_order()
    {
        Assert.Equal(new[] { "Makkelijk", "Normaal", "Sterk" }, ComputerLevels.All.Select(ComputerLevels.Label));
    }

    [Fact]
    public async Task Nothing_stored_gives_the_default_hint_rules()
    {
        Assert.Equal(HintRules.Default, await Preferences.LoadHintsAsync());
    }

    [Fact]
    public async Task Saved_hint_rules_come_back()
    {
        await Preferences.SaveHintsAsync(new HintRules(5, 0));

        Assert.Equal(new HintRules(5, 0), await Preferences.LoadHintsAsync());
    }

    [Theory]
    [InlineData("3")]
    [InlineData("3:10:1")]
    [InlineData("x:10")]
    [InlineData("-1:10")]
    [InlineData("6:10")]
    [InlineData("3:7")]
    [InlineData("3:-5")]
    [InlineData("")]
    public async Task Damaged_hint_rules_fall_back_to_the_defaults(string stored)
    {
        _store.Values[Storage.Preferences.HintsKey] = stored;

        Assert.Equal(HintRules.Default, await Preferences.LoadHintsAsync());
    }
}
