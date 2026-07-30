using MiniTracker.Api.Backlog;
using MiniTracker.Api.Services;

namespace MiniTracker.Tests;

public class TemplateFileTests
{
    [Fact]
    public void TemplateLocator_finds_the_backlog_template()
    {
        var path = TemplateLocator.Find("BACKLOG.template.md");

        Assert.True(File.Exists(path));
        Assert.EndsWith(Path.Combine("templates", "BACKLOG.template.md"), path);
    }

    [Fact]
    public void TemplateLocator_finds_the_skill_template()
    {
        Assert.True(File.Exists(TemplateLocator.Find("SKILL.template.md")));
    }

    [Fact]
    public void TemplateLocator_throws_for_an_unknown_file()
    {
        Assert.Throws<FileNotFoundException>(() => TemplateLocator.Find("NOPE.md"));
    }

    private static readonly string[] AllStatuses =
    {
        "Not Yet Started", "Under Review", "Refined", "In Progress", "Vendor Test", "Done", "On Hold"
    };

    private static Board Template() =>
        BacklogParser.Parse(File.ReadAllText(TemplateLocator.Find("BACKLOG.template.md")));

    [Fact]
    public void Backlog_template_has_five_epics_with_expected_story_counts()
    {
        var byNum = Template().Epics.ToDictionary(e => e.Number, e => e.Stories.Count);

        Assert.Equal(5, byNum.Count);
        Assert.Equal(2, byNum[0]);   // Developer Tooling
        Assert.Equal(11, byNum[1]);  // Core Application
        Assert.Equal(4, byNum[2]);   // CI/CD and Deployment
        Assert.Equal(1, byNum[3]);   // Mobile Apps — the placeholder story
        Assert.Equal(6, byNum[4]);   // Scaling and Performance
    }

    [Fact]
    public void Backlog_template_has_24_sequentially_coded_stories()
    {
        var codes = Template().Epics.SelectMany(e => e.Stories).Select(s => s.Code).ToList();

        Assert.Equal(24, codes.Count);
        Assert.Equal(Enumerable.Range(1, 24).Select(i => $"US-{i:D2}"), codes);
    }

    [Fact]
    public void Backlog_template_exercises_all_seven_statuses()
    {
        var used = Template().Epics.SelectMany(e => e.Stories).Select(s => s.Status.Label).ToHashSet();

        Assert.All(AllStatuses, label => Assert.Contains(label, used));
    }

    [Fact]
    public void Backlog_template_covers_both_test_case_table_shapes_and_the_validation_heading()
    {
        var stories = Template().Epics.SelectMany(e => e.Stories).ToDictionary(s => s.Code);

        // Shape B: "| ID | Description | Status | Notes |"
        Assert.Contains(stories["US-01"].TestCases, tc => tc.Id == "TC-01-01");
        // Shape A: "| TC | Verifies | Scenario | Status |"
        Assert.Contains(stories["US-04"].TestCases, tc => tc.Id == "TC-04-1");
        // "### Validation" heading instead of "### Test Cases"
        Assert.Contains(stories["US-09"].TestCases, tc => tc.Id == "TC-09-01");
    }

    [Fact]
    public void Every_story_has_tasks_and_test_cases()
    {
        Assert.All(Template().Epics.SelectMany(e => e.Stories), s =>
        {
            Assert.NotEmpty(s.Tasks);
            Assert.NotEmpty(s.TestCases);
        });
    }

    [Fact]
    public void Every_skill_referenced_by_the_template_exists_on_disk()
    {
        var templatesDir = Path.GetDirectoryName(TemplateLocator.Find("BACKLOG.template.md"))!;

        var referenced = Template().Epics.SelectMany(e => e.Stories)
            .Where(s => !string.IsNullOrWhiteSpace(s.SkillPath))
            .Select(s => s.SkillPath!)
            .Distinct()
            .ToList();

        Assert.NotEmpty(referenced);
        Assert.All(referenced, rel =>
            Assert.True(File.Exists(Path.Combine(templatesDir, rel)),
                $"Story references '{rel}' but templates/{rel} does not exist — the demo's skill link would 404."));
    }

    [Fact]
    public void Template_ships_no_orphaned_skill_files()
    {
        var templatesDir = Path.GetDirectoryName(TemplateLocator.Find("BACKLOG.template.md"))!;
        var skillsDir = Path.Combine(templatesDir, "skills");

        var onDisk = Directory.GetFiles(skillsDir, "SKILL.md", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(templatesDir, p).Replace('\\', '/'))
            .ToHashSet();

        var referenced = Template().Epics.SelectMany(e => e.Stories)
            .Where(s => !string.IsNullOrWhiteSpace(s.SkillPath))
            .Select(s => s.SkillPath!.Replace('\\', '/'))
            .ToHashSet();

        Assert.Equal(onDisk.OrderBy(x => x), referenced.OrderBy(x => x));
    }
}
