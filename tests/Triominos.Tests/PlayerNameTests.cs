using Triominos.Core.Game;

namespace Triominos.Tests;

public class PlayerNameTests
{
    [Fact]
    public void Distinct_names_have_no_problem()
    {
        Assert.Null(PlayerNameCheck.Find(["Ann", "Bob", "Cleo"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_reported_with_its_position(string blank)
    {
        var issue = PlayerNameCheck.Find(["Ann", blank, "Cleo"]);

        Assert.Equal(new PlayerNameIssue(1, PlayerNameProblem.Blank), issue);
    }

    [Fact]
    public void A_repeated_name_is_reported_at_the_later_player_ignoring_case_and_whitespace()
    {
        var issue = PlayerNameCheck.Find(["Ann", "Bob", " ann "]);

        Assert.Equal(new PlayerNameIssue(2, PlayerNameProblem.Duplicate), issue);
    }

    [Fact]
    public void The_first_problem_wins()
    {
        var issue = PlayerNameCheck.Find(["Ann", "Ann", ""]);

        Assert.Equal(new PlayerNameIssue(1, PlayerNameProblem.Duplicate), issue);
    }

    [Theory]
    [InlineData("Ann", "")]
    [InlineData("Ann", "ANN")]
    public void Start_rejects_blank_and_repeated_names(string first, string second)
    {
        Assert.Throws<ArgumentException>(() => TriominosGame.Start([first, second], new Random(1)));
    }
}
