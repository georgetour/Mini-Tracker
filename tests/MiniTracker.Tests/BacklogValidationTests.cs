using MiniTracker.Api.Backlog;

namespace MiniTracker.Tests;

public class BacklogValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mt-val-" + Guid.NewGuid().ToString("N"));

    private string Backlog => Path.Combine(_root, "BACKLOG.yaml");
    private string Skills => Path.Combine(_root, "skills");

    public BacklogValidationTests() => Directory.CreateDirectory(Skills);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private void WriteIndex(string yaml) => File.WriteAllText(Backlog, yaml);

    private const string Good = """
        project: Test
        roadmap: [V1]
        epics:
          - number: 0
            title: Tooling
            stories:
              - code: US-01
                title: Board
                status: Done
                release: V1
                folder: board
        """;

    [Fact]
    public void A_correct_backlog_reports_ok()
    {
        WriteIndex(Good);
        Directory.CreateDirectory(Path.Combine(Skills, "board"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.True(report.Ok);
        Assert.Empty(report.Issues.Where(i => i.Severity == "error"));
    }

    [Fact]
    public void A_bad_indent_reports_the_line_number()
    {
        WriteIndex("""
            project: Test
            epics:
              - number: 0
                 title: Broken
            """);

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        var issue = report.Issues.First(i => i.Severity == "error");
        Assert.Contains("Line", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_file_is_an_error_not_a_crash()
    {
        var report = BacklogValidation.Check(Path.Combine(_root, "nope.yaml"), Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_story_pointing_at_a_missing_folder_is_an_error()
    {
        WriteIndex(Good);   // the "board" folder is never created

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Severity == "error" && i.Message.Contains("board"));
    }

    [Fact]
    public void A_folder_nobody_references_is_a_warning_not_an_error()
    {
        WriteIndex(Good);
        Directory.CreateDirectory(Path.Combine(Skills, "board"));
        Directory.CreateDirectory(Path.Combine(Skills, "orphan"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.True(report.Ok);
        Assert.Contains(report.Issues, i => i.Severity == "warning" && i.Message.Contains("orphan"));
    }

    [Fact]
    public void A_duplicate_story_code_is_an_error()
    {
        WriteIndex("""
            project: Test
            epics:
              - number: 0
                title: Tooling
                stories:
                  - code: US-01
                    title: One
                    status: Done
                    folder: one
                  - code: US-01
                    title: Two
                    status: Done
                    folder: two
            """);
        Directory.CreateDirectory(Path.Combine(Skills, "one"));
        Directory.CreateDirectory(Path.Combine(Skills, "two"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Severity == "error" && i.Message.Contains("US-01"));
    }

    [Fact]
    public void A_duplicate_epic_number_is_an_error()
    {
        WriteIndex("""
            project: Test
            epics:
              - number: 0
                title: One
              - number: 0
                title: Two
            """);

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Message.Contains("Epic 0"));
    }

    [Fact]
    public void An_unknown_status_word_is_an_error()
    {
        WriteIndex(Good.Replace("status: Done", "status: Finished"));
        Directory.CreateDirectory(Path.Combine(Skills, "board"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Message.Contains("Finished"));
    }

    [Fact]
    public void A_release_not_in_the_roadmap_is_a_warning()
    {
        WriteIndex(Good.Replace("release: V1", "release: V9"));
        Directory.CreateDirectory(Path.Combine(Skills, "board"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.True(report.Ok);
        Assert.Contains(report.Issues, i => i.Severity == "warning" && i.Message.Contains("V9"));
    }

    [Fact]
    public void An_unknown_test_case_status_names_the_file_it_is_in()
    {
        WriteIndex(Good);
        var dir = Path.Combine(Skills, "board");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test-cases.yaml"), "- text: Check it\n  status: Maybe\n");

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Where != null && i.Where.Contains("test-cases.yaml"));
    }

    [Fact]
    public void A_story_with_no_folder_is_an_error()
    {
        WriteIndex("""
            project: Test
            epics:
              - number: 0
                title: Tooling
                stories:
                  - code: US-01
                    title: Board
                    status: Done
            """);

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Message.Contains("no folder"));
    }

    [Fact]
    public void A_folder_escaping_the_skills_root_is_an_error()
    {
        WriteIndex(Good.Replace("folder: board", "folder: ../../escape"));

        var report = BacklogValidation.Check(Backlog, Skills);

        Assert.False(report.Ok);
        Assert.Contains(report.Issues, i => i.Message.Contains("outside"));
    }
}
