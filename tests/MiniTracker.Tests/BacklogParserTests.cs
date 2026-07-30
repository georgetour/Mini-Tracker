using MiniTracker.Api.Backlog;

namespace MiniTracker.Tests;

public class BacklogParserTests
{
    // A representative slice: one epic, one story with a status line (emoji+label+skill+release),
    // a Tasks table (done + not-done), and a Test Cases table (Not Run + Passed).
    private const string Sample =
        "# Epic 0: Local Backlog Tracker\n" +
        "\n" +
        "## US-21 · Backlog Board — Read & Render\n" +
        "\n" +
        "> **Status**: 🔍 Under Review · **Skill**: `skills/backlog-tracker/SKILL.md` · **Release**: V0.1\n" +
        "> **Depends on**: nothing\n" +
        "\n" +
        "### Tasks\n" +
        "\n" +
        "| # | Task | ✓ |\n" +
        "|---|------|---|\n" +
        "| 21.0 | Create detailed design | ✅ |\n" +
        "| 21.1 | Backlog parser | ⬜ |\n" +
        "\n" +
        "### Test Cases\n" +
        "\n" +
        "| ID | Description | Status | Notes |\n" +
        "|----|-------------|--------|-------|\n" +
        "| TC-21-01 | Board shows every epic | ⬜ Not Run | |\n" +
        "| TC-21-02 | Badge matches status | ✅ Passed | |\n";

    [Fact]
    public void Parses_epic_number_and_title()
    {
        var board = BacklogParser.Parse(Sample);

        var epic = Assert.Single(board.Epics);
        Assert.Equal(0, epic.Number);
        Assert.Equal("Local Backlog Tracker", epic.Title);
    }

    [Fact]
    public void Parses_story_heading_status_skill_and_release()
    {
        var story = Assert.Single(BacklogParser.Parse(Sample).Epics[0].Stories);

        Assert.Equal("US-21", story.Code);
        Assert.Equal("Backlog Board — Read & Render", story.Title);
        Assert.Equal("🔍", story.Status.Emoji);
        Assert.Equal("Under Review", story.Status.Label);
        Assert.Equal("V0.1", story.Release);
        Assert.Equal("skills/backlog-tracker/SKILL.md", story.SkillPath);
    }

    [Fact]
    public void Parses_tasks_with_done_state()
    {
        var story = BacklogParser.Parse(Sample).Epics[0].Stories[0];

        Assert.Equal(2, story.Tasks.Count);
        Assert.Equal("21.0", story.Tasks[0].Id);
        Assert.Equal("Create detailed design", story.Tasks[0].Text);
        Assert.True(story.Tasks[0].Done);
        Assert.Equal("21.1", story.Tasks[1].Id);
        Assert.False(story.Tasks[1].Done);
    }

    [Fact]
    public void Parses_test_cases_with_status_by_header_column()
    {
        var story = BacklogParser.Parse(Sample).Epics[0].Stories[0];

        Assert.Equal(2, story.TestCases.Count);
        Assert.Equal("TC-21-01", story.TestCases[0].Id);
        Assert.Equal("⬜", story.TestCases[0].Status.Emoji);
        Assert.Equal("Not Run", story.TestCases[0].Status.Label);
        Assert.Equal("✅", story.TestCases[1].Status.Emoji);
        Assert.Equal("Passed", story.TestCases[1].Status.Label);
    }

    [Fact]
    public void Records_line_indices_for_write_back()
    {
        var lines = Sample.Replace("\r\n", "\n").Split('\n');
        var story = BacklogParser.Parse(Sample).Epics[0].Stories[0];

        Assert.StartsWith("> **Status**", lines[story.StatusLine].Trim());
        Assert.StartsWith("| 21.0", lines[story.Tasks[0].Line].TrimStart());
        Assert.StartsWith("| TC-21-01", lines[story.TestCases[0].Line].TrimStart());
    }
}
