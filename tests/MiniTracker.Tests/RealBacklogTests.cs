using MiniTracker.Api.Backlog;

namespace MiniTracker.Tests;

/// <summary>
/// Parses the bundled sample BACKLOG.md fixture — a small but structurally complete file covering
/// both test-case table shapes, the "### Validation" heading, an epic with zero stories, and all 7
/// status values. Structural counts are stable since the fixture is static, so they're asserted
/// directly.
/// </summary>
public class RealBacklogTests
{
    private static readonly string[] Vocabulary =
    {
        "Not Yet Started", "Under Review", "Refined", "In Progress", "Vendor Test", "Done", "On Hold"
    };

    private static string SampleBacklog() => File.ReadAllText(TestBacklogLocator.Resolve());

    [Fact]
    public void Finds_all_epics_with_expected_story_counts()
    {
        var board = BacklogParser.Parse(SampleBacklog());

        var byNum = board.Epics.ToDictionary(e => e.Number, e => e.Stories.Count);
        Assert.Equal(2, byNum[0]);  // Epic 0 — Tracker Tooling (US-01, US-02)
        Assert.Equal(3, byNum[1]);  // Epic 1 — Sample Application (US-03 … US-05)
        Assert.Equal(2, byNum[2]);  // Epic 2 — Automation (US-06, US-07)
        Assert.Equal(0, byNum[3]);  // Epic 3 — Future Work (no stories yet)
        Assert.Equal(2, byNum[4]);  // Epic 4 — Scaling (US-08, US-09)
    }

    [Fact]
    public void Total_of_9_stories_all_well_formed()
    {
        var stories = BacklogParser.Parse(SampleBacklog()).Epics.SelectMany(e => e.Stories).ToList();

        Assert.Equal(9, stories.Count);
        Assert.All(stories, s =>
        {
            Assert.Matches(@"^US-\d+$", s.Code);
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.Contains(s.Status.Label, Vocabulary);
            Assert.True(s.StatusLine >= 0, $"{s.Code} has no status line");
        });
    }

    [Fact]
    public void All_7_statuses_are_represented()
    {
        var stories = BacklogParser.Parse(SampleBacklog()).Epics.SelectMany(e => e.Stories).ToList();

        Assert.All(Vocabulary, label => Assert.Contains(stories, s => s.Status.Label == label));
    }

    [Fact]
    public void Parses_both_test_case_table_shapes_and_validation_heading()
    {
        var stories = BacklogParser.Parse(SampleBacklog()).Epics.SelectMany(e => e.Stories)
            .ToDictionary(s => s.Code);

        // Shape B: "| ID | Description | Status | Notes |"  (US-01)
        Assert.Contains(stories["US-01"].TestCases, tc => tc.Id == "TC-01-01");
        // Shape A: "| TC | Verifies | Scenario | Status |"  (US-03)
        Assert.Contains(stories["US-03"].TestCases, tc => tc.Id == "TC-03-1");
        // "### Validation" heading instead of "### Test Cases"  (US-04)
        Assert.Contains(stories["US-04"].TestCases, tc => tc.Id == "TC-04-01");
    }
}
