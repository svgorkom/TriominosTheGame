using AngleSharp.Dom;
using Bunit;
using Triominos.Core.Ai;
using Triominos.Core.Game;
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

    private static IElement Toggle(IRenderedComponent<PlayerSetupForm> cut, int row) => cut.FindAll(".kind-toggle")[row];

    [Fact]
    public void Every_seat_starts_as_a_human_and_the_only_human_cannot_become_a_computer()
    {
        var cut = RenderForm();

        Assert.Equal(new[] { "Mens", "Mens" }, cut.FindAll(".kind-toggle").Select(t => t.TextContent.Trim()));

        Toggle(cut, 1).Click();

        // Seat 1 is now the only human: it can neither switch nor be removed.
        Assert.Equal(new[] { "Mens", "Computer" }, cut.FindAll(".kind-toggle").Select(t => t.TextContent.Trim()));
        Assert.True(Toggle(cut, 0).HasAttribute("disabled"));
        Assert.False(Toggle(cut, 1).HasAttribute("disabled"));
    }

    [Fact]
    public void Switching_to_the_computer_renames_a_default_name_and_switching_back_restores_one()
    {
        var cut = RenderForm();

        Toggle(cut, 1).Click();
        Assert.Equal(new[] { "Speler 1", "Computer" }, Names(cut));

        Toggle(cut, 1).Click();
        Assert.Equal(new[] { "Speler 1", "Speler 2" }, Names(cut));
    }

    [Fact]
    public void A_name_the_player_typed_stays_when_the_seat_switches()
    {
        var cut = RenderForm();
        cut.FindAll(".name-input")[1].Input("Bea");

        Toggle(cut, 1).Click();

        Assert.Equal(new[] { "Speler 1", "Bea" }, Names(cut));
    }

    [Fact]
    public void Several_computers_get_their_own_names()
    {
        var cut = RenderForm();
        AddButton(cut).Click();
        AddButton(cut).Click();

        Toggle(cut, 1).Click();
        Toggle(cut, 2).Click();
        Toggle(cut, 3).Click();

        Assert.Equal(new[] { "Speler 1", "Computer", "Computer 2", "Computer 3" }, Names(cut));
        Assert.Equal(string.Empty, cut.Find(".problem").TextContent);
        Assert.False(StartButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void The_only_human_cannot_be_removed_but_a_computer_can()
    {
        var cut = RenderForm();
        AddButton(cut).Click();
        Toggle(cut, 1).Click();
        Toggle(cut, 2).Click();

        // Seat 1 is the only human, so its remove button is off; the computers' are on.
        Assert.True(cut.FindAll(".remove")[0].HasAttribute("disabled"));
        Assert.False(cut.FindAll(".remove")[1].HasAttribute("disabled"));

        cut.FindAll(".remove")[1].Click();

        Assert.Equal(2, Rows(cut));
    }

    [Fact]
    public void The_chosen_kind_is_handed_on_when_the_game_starts()
    {
        var cut = RenderForm();
        Toggle(cut, 1).Click();

        StartButton(cut).Click();

        Assert.NotNull(_started);
        Assert.Equal(new[] { false, true }, _started.Select(p => p.IsComputer));
    }

    [Fact]
    public void Players_of_the_last_game_come_back_with_their_kind()
    {
        var cut = RenderForm([new("Ann", PlayerColors.All[0]), new("Computer", PlayerColors.All[1], IsComputer: true)]);

        Assert.Equal(new[] { "Mens", "Computer" }, cut.FindAll(".kind-toggle").Select(t => t.TextContent.Trim()));
    }

    private static string? SelectedLevel(IElement select) => select.QuerySelector("option[selected]")?.GetAttribute("value");

    [Fact]
    public void Only_a_computer_has_a_level_and_it_starts_at_normal()
    {
        var cut = RenderForm();
        Assert.Empty(cut.FindAll(".level-select"));

        Toggle(cut, 1).Click();

        var select = cut.Find(".level-select");
        Assert.Equal(new[] { "Makkelijk", "Normaal", "Sterk" }, select.QuerySelectorAll("option").Select(o => o.TextContent.Trim()));
        Assert.Equal(nameof(ComputerLevel.Normal), SelectedLevel(select));

        Toggle(cut, 1).Click();

        Assert.Empty(cut.FindAll(".level-select"));
    }

    [Fact]
    public void The_chosen_level_is_handed_on_when_the_game_starts()
    {
        var cut = RenderForm();
        AddButton(cut).Click();
        Toggle(cut, 1).Click();
        Toggle(cut, 2).Click();

        cut.FindAll(".level-select")[0].Change(nameof(ComputerLevel.Easy));
        cut.FindAll(".level-select")[1].Change(nameof(ComputerLevel.Strong));
        StartButton(cut).Click();

        Assert.NotNull(_started);
        Assert.Equal(new[] { ComputerLevel.Normal, ComputerLevel.Easy, ComputerLevel.Strong }, _started.Select(p => p.Level));
    }

    [Fact]
    public void Players_of_the_last_game_come_back_with_their_level()
    {
        var cut = RenderForm([new("Ann", PlayerColors.All[0]), new("Bot", PlayerColors.All[1], IsComputer: true, Level: ComputerLevel.Strong)]);

        Assert.Equal(nameof(ComputerLevel.Strong), SelectedLevel(cut.Find(".level-select")));
    }

    // --- Hints -----------------------------------------------------------------------------------

    private const string CountSelect = "select[aria-label='Aantal hints per speler']";
    private const string PenaltySelect = "select[aria-label='Kosten per hint']";

    private static string Selected(IRenderedComponent<PlayerSetupForm> cut, string select) =>
        cut.FindAll($"{select} option").Single(o => o.HasAttribute("selected")).TextContent;

    [Fact]
    public void The_hint_choices_start_at_the_defaults()
    {
        var cut = RenderForm();

        Assert.Equal(Scoring.DefaultHintsPerPlayer.ToString(), Selected(cut, CountSelect));
        Assert.Equal($"{Scoring.DefaultHintPenalty} punten", Selected(cut, PenaltySelect));
    }

    [Fact]
    public void The_chosen_hint_rules_are_handed_over_just_before_the_game_starts()
    {
        HintRules? chosen = null;
        var cut = Render<PlayerSetupForm>(parameters =>
        {
            parameters.Add(form => form.OnStart, (IReadOnlyList<PlayerSetup> _) => Assert.NotNull(chosen));
            parameters.Add(form => form.HintsChanged, (HintRules rules) => chosen = rules);
        });

        cut.Find(CountSelect).Change("1");
        cut.Find(PenaltySelect).Change("5");
        StartButton(cut).Click();

        Assert.Equal(new HintRules(1, 5), chosen);
    }

    [Fact]
    public void Hints_off_switches_the_cost_choice_off()
    {
        var cut = RenderForm();

        cut.Find(CountSelect).Change("0");

        Assert.Equal("Uit", Selected(cut, CountSelect));
        Assert.True(cut.Find(PenaltySelect).HasAttribute("disabled"));
    }

    [Fact]
    public void The_form_starts_with_the_hint_rules_it_is_given()
    {
        var cut = Render<PlayerSetupForm>(parameters => parameters.Add(form => form.Hints, new HintRules(5, 0)));

        Assert.Equal("5", Selected(cut, CountSelect));
        Assert.Equal("Gratis", Selected(cut, PenaltySelect));
    }
}
