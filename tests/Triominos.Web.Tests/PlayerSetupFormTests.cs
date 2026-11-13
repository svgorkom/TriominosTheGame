using AngleSharp.Dom;
using Bunit;
using Triominos.Web.Play;
using Triominos.Web.Screens;

namespace Triominos.Web.Tests;

public class PlayerSetupFormTests : BunitContext
{
    private IReadOnlyList<PlayerSetup>? _started;

    private IRenderedComponent<PlayerSetupForm> RenderForm(IReadOnlyList<PlayerSetup>? players = null) =>
        Render<PlayerSetupForm>(parameters =>
        {
            parameters.Add(form => form.OnStart, (IReadOnlyList<PlayerSetup> chosen) => _started = chosen);
            if (players is not null)
            {
                parameters.Add(form => form.Players, players);
            }
        });

    private static int Rows(IRenderedComponent<PlayerSetupForm> cut) => cut.FindAll(".setup-player").Count;

    private static IElement AddButton(IRenderedComponent<PlayerSetupForm> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Speler toevoegen");

    private static IElement StartButton(IRenderedComponent<PlayerSetupForm> cut) => cut.Find("button.primary");

    private static string?[] Names(IRenderedComponent<PlayerSetupForm> cut) =>
        [.. cut.FindAll(".name-input").Select(input => input.GetAttribute("value"))];

    [Fact]
    public void It_starts_with_the_two_default_players_and_does_not_let_either_go()
    {
        var cut = RenderForm();

        Assert.Equal(2, Rows(cut));
        Assert.Equal(new[] { "Speler 1", "Speler 2" }, Names(cut));
        Assert.All(cut.FindAll(".remove"), remove => Assert.True(remove.HasAttribute("disabled")));
        Assert.False(AddButton(cut).HasAttribute("disabled"));
        Assert.False(StartButton(cut).HasAttribute("disabled"));
        Assert.Equal(string.Empty, cut.Find(".problem").TextContent);
    }

    [Fact]
    public void Players_can_be_added_up_to_four_with_their_own_names_and_colours()
    {
        var cut = RenderForm();

        AddButton(cut).Click();
        AddButton(cut).Click();

        Assert.Equal(4, Rows(cut));
        Assert.Equal(new[] { "Speler 1", "Speler 2", "Speler 3", "Speler 4" }, Names(cut));
        Assert.True(AddButton(cut).HasAttribute("disabled"));
        Assert.All(cut.FindAll(".remove"), remove => Assert.False(remove.HasAttribute("disabled")));
        var colours = cut.FindAll(".swatch-button.selected").Select(b => b.GetAttribute("style")).Distinct().ToArray();
        Assert.Equal(4, colours.Length);
    }

    [Fact]
    public void Players_can_be_removed_down_to_two()
    {
        var cut = RenderForm();
        AddButton(cut).Click();
        AddButton(cut).Click();

        cut.FindAll(".remove")[3].Click();
        Assert.Equal(3, Rows(cut));
        Assert.False(AddButton(cut).HasAttribute("disabled"));

        cut.FindAll(".remove")[2].Click();
        Assert.Equal(2, Rows(cut));
        Assert.All(cut.FindAll(".remove"), remove => Assert.True(remove.HasAttribute("disabled")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_stops_the_game_from_starting(string name)
    {
        var cut = RenderForm();

        cut.FindAll(".name-input")[1].Input(name);

        Assert.Equal("Geef elke speler een naam.", cut.Find(".problem").TextContent);
        Assert.True(StartButton(cut).HasAttribute("disabled"));
        Assert.Contains("invalid", cut.FindAll(".setup-player")[1].ClassList);
    }

    [Theory]
    [InlineData("Speler 1")]
    [InlineData("speler 1")]
    [InlineData("  SPELER 1  ")]
    public void A_repeated_name_stops_the_game_from_starting(string name)
    {
        var cut = RenderForm();

        cut.FindAll(".name-input")[1].Input(name);

        Assert.Equal("Elke speler heeft een eigen naam nodig.", cut.Find(".problem").TextContent);
        Assert.True(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Fixing_a_name_lets_the_game_start_with_trimmed_names_in_turn_order()
    {
        var cut = RenderForm();
        cut.FindAll(".name-input")[1].Input("");
        Assert.True(StartButton(cut).HasAttribute("disabled"));

        cut.FindAll(".name-input")[1].Input("  Bea ");
        StartButton(cut).Click();

        Assert.NotNull(_started);
        Assert.Equal(new[] { "Speler 1", "Bea" }, _started.Select(p => p.Name));
        Assert.Equal(new[] { PlayerColors.All[0], PlayerColors.All[1] }, _started.Select(p => p.Color));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Two_to_four_players_can_start(int count)
    {
        var players = Enumerable.Range(0, count).Select(i => new PlayerSetup($"Naam {i}", PlayerColors.All[i + 1])).ToArray();
        var cut = RenderForm(players);

        Assert.Equal(count, Rows(cut));
        StartButton(cut).Click();

        Assert.Equal(players, _started);
    }

    [Fact]
    public void A_colour_in_use_cannot_be_taken_by_another_player_but_can_be_swapped_for_a_free_one()
    {
        var cut = RenderForm();
        var first = cut.FindAll(".setup-player")[0];

        // Player 2 has the second colour, so player 1 cannot take it; their own colour stays selectable.
        Assert.True(first.QuerySelectorAll(".swatch-button")[1].HasAttribute("disabled"));
        Assert.False(first.QuerySelectorAll(".swatch-button")[0].HasAttribute("disabled"));

        first.QuerySelectorAll(".swatch-button")[2].Click();

        var selected = cut.FindAll(".setup-player")[0].QuerySelectorAll(".swatch-button.selected");
        Assert.Single(selected);
        Assert.Equal(PlayerColors.Label(PlayerColors.All[2]), selected[0].GetAttribute("aria-label"));
    }

    [Fact]
    public void A_name_is_at_most_sixteen_characters()
    {
        var cut = RenderForm();

        Assert.All(cut.FindAll(".name-input"), input => Assert.Equal(PlayerSetup.MaxNameLength.ToString(), input.GetAttribute("maxlength")));
    }
}
