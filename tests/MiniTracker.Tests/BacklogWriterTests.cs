using MiniTracker.Api.Backlog;

namespace MiniTracker.Tests;

public class BacklogWriterTests
{
    private const string Md =
        "# Epic 0: Local Backlog Tracker\n" +
        "\n" +
        "## US-21 · Backlog Board — Read & Render\n" +
        "\n" +
        "> **Status**: 🔍 Under Review · **Skill**: `skills/backlog-tracker/SKILL.md` · **Release**: V0.1\n" +
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
        "| TC-21-02 | Badge matches status | ✅ Passed | |\n" +
        "\n" +
        "## US-22 · Something Else\n" +
        "\n" +
        "> **Status**: ⬜ Not Yet Started · **Release**: V0.1\n";

    private static int ChangedLines(string a, string b)
    {
        var la = a.Split('\n'); var lb = b.Split('\n');
        Assert.Equal(la.Length, lb.Length); // no lines added/removed
        return la.Where((t, i) => t != lb[i]).Count();
    }

    [Fact]
    public void SetStoryStatus_swaps_only_the_status_token()
    {
        var result = BacklogWriter.SetStoryStatus(Md, "US-21", new StatusToken("🔄", "In Progress"));

        Assert.Contains("> **Status**: 🔄 In Progress · **Skill**: `skills/backlog-tracker/SKILL.md` · **Release**: V0.1", result);
        Assert.DoesNotContain("🔍 Under Review", result);
        Assert.Equal(1, ChangedLines(Md, result));
    }

    [Fact]
    public void SetStoryStatus_targets_the_right_story()
    {
        var result = BacklogWriter.SetStoryStatus(Md, "US-22", new StatusToken("🔄", "In Progress"));

        Assert.Contains("## US-22 · Something Else\n\n> **Status**: 🔄 In Progress · **Release**: V0.1", result);
        Assert.Contains("## US-21 · Backlog Board — Read & Render\n\n> **Status**: 🔍 Under Review", result); // untouched
        Assert.Equal(1, ChangedLines(Md, result));
    }

    [Fact]
    public void SetTaskDone_flips_only_the_check_cell()
    {
        var result = BacklogWriter.SetTaskDone(Md, "US-21", "21.1", done: true);

        Assert.Contains("| 21.1 | Backlog parser | ✅ |", result);
        Assert.Contains("| 21.0 | Create detailed design | ✅ |", result); // untouched
        Assert.Equal(1, ChangedLines(Md, result));
    }

    [Fact]
    public void SetTestCaseStatus_replaces_only_the_status_cell()
    {
        var result = BacklogWriter.SetTestCaseStatus(Md, "US-21", "TC-21-01", new StatusToken("✅", "Passed"));

        Assert.Contains("| TC-21-01 | Board shows every epic | ✅ Passed | |", result);
        Assert.Contains("| TC-21-02 | Badge matches status | ✅ Passed | |", result); // untouched
        Assert.Equal(1, ChangedLines(Md, result));
    }

    [Fact]
    public void Preserves_crlf_line_endings()
    {
        var crlf = Md.Replace("\n", "\r\n");
        var result = BacklogWriter.SetTaskDone(crlf, "US-21", "21.1", done: true);

        Assert.Contains("\r\n", result);
        Assert.DoesNotContain("\n\n\n", result.Replace("\r", "")); // structure intact
        Assert.Contains("| 21.1 | Backlog parser | ✅ |\r\n", result);
    }
}
