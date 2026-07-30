using MiniTracker.Api.Backlog;

namespace MiniTracker.Tests;

public class BacklogGeneratorTests
{
    private const string Md =
        "# Backlog\n" +
        "\n" +
        "# Epic 0: Tooling\n" +
        "\n" +
        "## US-01 · First Story\n" +
        "\n" +
        "> **Status**: ⬜ Not Yet Started · **Release**: V1\n" +
        "\n" +
        "### Tasks\n" +
        "\n" +
        "| # | Task | ✓ |\n" +
        "|---|------|---|\n" +
        "| 1.0 | Do it | ⬜ |\n" +
        "\n" +
        "### Test Cases\n" +
        "\n" +
        "| ID | Description | Status | Notes |\n" +
        "|----|-------------|--------|-------|\n" +
        "| TC-01-01 | Check it | ⬜ Not Run | |\n" +
        "\n" +
        "---\n" +
        "\n" +
        "# Epic 1: Application\n" +
        "\n" +
        "## US-02 · Second Story\n" +
        "\n" +
        "> **Status**: ✅ Done · **Release**: V1\n" +
        "\n" +
        "### Tasks\n" +
        "\n" +
        "| # | Task | ✓ |\n" +
        "|---|------|---|\n" +
        "| 2.0 | Done already | ✅ |\n" +
        "\n" +
        "### Test Cases\n" +
        "\n" +
        "| ID | Description | Status | Notes |\n" +
        "|----|-------------|--------|-------|\n" +
        "| TC-02-01 | Verified | ✅ Passed | |\n";

    // ---------- AddEpic ----------

    [Fact]
    public void AddEpic_appends_a_parseable_epic()
    {
        var result = BacklogGenerator.AddEpic(Md, 2, "Reporting");

        var board = BacklogParser.Parse(result);
        Assert.Equal(3, board.Epics.Count);
        var added = board.Epics.Single(e => e.Number == 2);
        Assert.Equal("Reporting", added.Title);
        Assert.Empty(added.Stories);
    }

    [Fact]
    public void AddEpic_leaves_every_existing_line_untouched()
    {
        var result = BacklogGenerator.AddEpic(Md, 2, "Reporting");

        // Append-only: the original text must still be a prefix of the result.
        Assert.StartsWith(Md.TrimEnd('\n'), result);
    }

    [Fact]
    public void AddEpic_rejects_a_duplicate_number()
    {
        var ex = Assert.Throws<BacklogValidationException>(() => BacklogGenerator.AddEpic(Md, 1, "Clash"));
        Assert.Contains("already exists", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddEpic_rejects_a_blank_title(string title)
    {
        Assert.Throws<BacklogValidationException>(() => BacklogGenerator.AddEpic(Md, 5, title));
    }

    [Fact]
    public void AddEpic_rejects_a_number_out_of_range()
    {
        Assert.Throws<BacklogValidationException>(() => BacklogGenerator.AddEpic(Md, -1, "Nope"));
        Assert.Throws<BacklogValidationException>(() => BacklogGenerator.AddEpic(Md, 1000, "Nope"));
    }

    // ---------- AddStory ----------

    [Fact]
    public void AddStory_adds_to_the_named_epic()
    {
        var result = BacklogGenerator.AddStory(Md, 0, "US-03", "Third Story", "V2", null);

        var board = BacklogParser.Parse(result);
        var epic0 = board.Epics.Single(e => e.Number == 0);
        Assert.Equal(2, epic0.Stories.Count);
        var added = epic0.Stories.Single(s => s.Code == "US-03");
        Assert.Equal("Third Story", added.Title);
        Assert.Equal("V2", added.Release);
        Assert.Equal("Not Yet Started", added.Status.Label);
    }

    [Fact]
    public void AddStory_lands_in_the_first_epic_not_the_last()
    {
        var result = BacklogGenerator.AddStory(Md, 0, "US-03", "Third Story", null, null);

        var board = BacklogParser.Parse(result);
        Assert.Contains(board.Epics.Single(e => e.Number == 0).Stories, s => s.Code == "US-03");
        Assert.DoesNotContain(board.Epics.Single(e => e.Number == 1).Stories, s => s.Code == "US-03");
    }

    [Fact]
    public void AddStory_records_the_skill_path_when_given()
    {
        var result = BacklogGenerator.AddStory(Md, 1, "US-04", "With Skill", "V1", "skills/area/SKILL.md");

        var story = BacklogParser.Parse(result).Epics.SelectMany(e => e.Stories).Single(s => s.Code == "US-04");
        Assert.Equal("skills/area/SKILL.md", story.SkillPath);
    }

    [Fact]
    public void AddStory_omits_the_skill_field_when_not_given()
    {
        var result = BacklogGenerator.AddStory(Md, 1, "US-04", "No Skill", "V1", null);

        var story = BacklogParser.Parse(result).Epics.SelectMany(e => e.Stories).Single(s => s.Code == "US-04");
        Assert.Null(story.SkillPath);
    }

    [Fact]
    public void AddStory_gives_the_new_story_a_task_and_a_test_case()
    {
        var result = BacklogGenerator.AddStory(Md, 0, "US-03", "Third Story", null, null);

        var story = BacklogParser.Parse(result).Epics.SelectMany(e => e.Stories).Single(s => s.Code == "US-03");
        Assert.NotEmpty(story.Tasks);
        Assert.NotEmpty(story.TestCases);
    }

    [Fact]
    public void AddStory_keeps_existing_stories_intact()
    {
        var result = BacklogGenerator.AddStory(Md, 0, "US-03", "Third Story", null, null);

        var board = BacklogParser.Parse(result);
        var existing = board.Epics.SelectMany(e => e.Stories).Single(s => s.Code == "US-02");
        Assert.Equal("Done", existing.Status.Label);
        Assert.Equal("Second Story", existing.Title);
    }

    [Fact]
    public void AddStory_rejects_a_duplicate_code()
    {
        var ex = Assert.Throws<BacklogValidationException>(
            () => BacklogGenerator.AddStory(Md, 0, "US-01", "Clash", null, null));
        Assert.Contains("already used", ex.Message);
    }

    [Fact]
    public void AddStory_rejects_an_unknown_epic()
    {
        var ex = Assert.Throws<BacklogValidationException>(
            () => BacklogGenerator.AddStory(Md, 9, "US-03", "Orphan", null, null));
        Assert.Contains("no epic 9", ex.Message);
    }

    [Theory]
    [InlineData("us-03")]
    [InlineData("US3")]
    [InlineData("STORY-03")]
    [InlineData("US-")]
    public void AddStory_rejects_a_malformed_code(string code)
    {
        Assert.Throws<BacklogValidationException>(() => BacklogGenerator.AddStory(Md, 0, code, "Bad code", null, null));
    }

    [Fact]
    public void AddStory_rejects_a_blank_title()
    {
        Assert.Throws<BacklogValidationException>(() => BacklogGenerator.AddStory(Md, 0, "US-03", "  ", null, null));
    }

    [Fact]
    public void AddStory_preserves_crlf_line_endings()
    {
        var crlf = Md.Replace("\n", "\r\n");

        var result = BacklogGenerator.AddStory(crlf, 0, "US-03", "Third Story", null, null);

        Assert.Contains("\r\n", result);
        Assert.DoesNotContain("\n\n\n", result.Replace("\r", ""));
        Assert.Contains("US-03", BacklogParser.Parse(result).Epics
            .SelectMany(e => e.Stories).Select(s => s.Code));
    }

    // ---------- RenameEpic ----------

    [Fact]
    public void RenameEpic_changes_only_the_title()
    {
        var result = BacklogGenerator.RenameEpic(Md, 1, "Delivery");

        var board = BacklogParser.Parse(result);
        Assert.Equal("Delivery", board.Epics.Single(e => e.Number == 1).Title);
        Assert.Equal("Tooling", board.Epics.Single(e => e.Number == 0).Title);
        Assert.Single(board.Epics.Single(e => e.Number == 1).Stories);
    }

    [Fact]
    public void RenameEpic_rejects_an_unknown_epic()
    {
        var e = Assert.Throws<BacklogValidationException>(() => BacklogGenerator.RenameEpic(Md, 9, "Nope"));
        Assert.Contains("no epic 9", e.Message);
    }

    [Fact]
    public void RenameEpic_rejects_an_empty_title()
    {
        Assert.Throws<BacklogValidationException>(() => BacklogGenerator.RenameEpic(Md, 1, "   "));
    }

    // ---------- RemoveStory ----------

    [Fact]
    public void RemoveStory_removes_the_story_and_its_tables()
    {
        var withTwo = BacklogGenerator.AddStory(Md, 0, "US-03", "Third Story", null, null);

        var result = BacklogGenerator.RemoveStory(withTwo, "US-03");

        var board = BacklogParser.Parse(result);
        Assert.DoesNotContain("US-03", board.Epics.SelectMany(e => e.Stories).Select(s => s.Code));
        Assert.DoesNotContain("Third Story", result);
        Assert.DoesNotContain("TC-03-01", result);
    }

    [Fact]
    public void RemoveStory_keeps_the_following_story_intact()
    {
        var result = BacklogGenerator.RemoveStory(Md, "US-01");

        var board = BacklogParser.Parse(result);
        Assert.Equal(2, board.Epics.Count);
        Assert.Empty(board.Epics.Single(e => e.Number == 0).Stories);
        var kept = board.Epics.Single(e => e.Number == 1).Stories.Single();
        Assert.Equal("US-02", kept.Code);
        Assert.Equal("Done", kept.Status.Label);
        Assert.Single(kept.TestCases);
    }

    [Fact]
    public void RemoveStory_does_not_leave_a_growing_blank_gap()
    {
        var result = BacklogGenerator.RemoveStory(Md, "US-01");

        Assert.DoesNotContain("\n\n\n", result);
    }

    [Fact]
    public void RemoveStory_rejects_an_unknown_code()
    {
        var e = Assert.Throws<BacklogValidationException>(() => BacklogGenerator.RemoveStory(Md, "US-99"));
        Assert.Contains("no story US-99", e.Message);
    }

    // ---------- RemoveEpic ----------

    [Fact]
    public void RemoveEpic_removes_the_epic_and_its_stories()
    {
        var result = BacklogGenerator.RemoveEpic(Md, 0);

        var board = BacklogParser.Parse(result);
        Assert.Single(board.Epics);
        Assert.Equal(1, board.Epics[0].Number);
        Assert.DoesNotContain("US-01", result);
        Assert.DoesNotContain("Tooling", result);
    }

    [Fact]
    public void RemoveEpic_removes_the_last_epic_without_disturbing_the_first()
    {
        var result = BacklogGenerator.RemoveEpic(Md, 1);

        var board = BacklogParser.Parse(result);
        Assert.Single(board.Epics);
        Assert.Equal("Tooling", board.Epics[0].Title);
        Assert.Equal("US-01", board.Epics[0].Stories.Single().Code);
        Assert.DoesNotContain("\n\n\n", result);
    }

    [Fact]
    public void RemoveEpic_rejects_an_unknown_number()
    {
        Assert.Throws<BacklogValidationException>(() => BacklogGenerator.RemoveEpic(Md, 9));
    }

    // ---------- SetStorySkill ----------

    [Fact]
    public void SetStorySkill_adds_the_field_to_a_story_that_had_none()
    {
        var result = BacklogGenerator.SetStorySkill(Md, "US-01", "us-01-first/SKILL.md");

        var story = BacklogParser.Parse(result).Epics.SelectMany(e => e.Stories).Single(s => s.Code == "US-01");
        Assert.Equal("us-01-first/SKILL.md", story.SkillPath);
        Assert.Equal("Not Yet Started", story.Status.Label);
        Assert.Equal("V1", story.Release);
    }

    [Fact]
    public void SetStorySkill_replaces_an_existing_path()
    {
        var once = BacklogGenerator.SetStorySkill(Md, "US-01", "old/SKILL.md");

        var twice = BacklogGenerator.SetStorySkill(once, "US-01", "new/SKILL.md");

        var story = BacklogParser.Parse(twice).Epics.SelectMany(e => e.Stories).Single(s => s.Code == "US-01");
        Assert.Equal("new/SKILL.md", story.SkillPath);
        Assert.DoesNotContain("old/SKILL.md", twice);
    }

    [Fact]
    public void SetStorySkill_touches_only_the_target_story()
    {
        var result = BacklogGenerator.SetStorySkill(Md, "US-01", "a/SKILL.md");

        var other = BacklogParser.Parse(result).Epics.SelectMany(e => e.Stories).Single(s => s.Code == "US-02");
        Assert.Null(other.SkillPath);
    }

    [Fact]
    public void SetStorySkill_rejects_an_unknown_story()
    {
        Assert.Throws<BacklogValidationException>(() => BacklogGenerator.SetStorySkill(Md, "US-99", "a/SKILL.md"));
    }
}
