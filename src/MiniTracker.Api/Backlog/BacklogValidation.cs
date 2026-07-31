using YamlDotNet.Core;

namespace MiniTracker.Api.Backlog;

public sealed record ValidationIssue(string Severity, string Message, string? Where);

/// <summary>Ok is false only when there is at least one error. Warnings are worth showing but do
/// not stop the board loading.</summary>
public sealed record ValidationReport(bool Ok, IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// Everything the Sync button reports.
///
/// Splitting storage across an index and one folder per story buys a much faster board load, and
/// costs the possibility that the two disagree — a story naming a folder that is not there, or a
/// folder nobody references. That class of bug was impossible when it was all one file, so this is
/// where we pay it back.
/// </summary>
public static class BacklogValidation
{
    public static readonly IReadOnlyList<string> Statuses = new[]
    {
        "Not Yet Started", "Under Review", "Refined", "In Progress", "Vendor Test", "Done", "On Hold",
    };

    public static readonly IReadOnlyList<string> TestStatuses = new[] { "Not Run", "Passed", "Failed" };

    public static ValidationReport Check(string backlogPath, string skillsRoot)
    {
        var issues = new List<ValidationIssue>();

        if (!File.Exists(backlogPath))
            return Fail(issues, $"BACKLOG.yaml not found at {backlogPath}", Path.GetFileName(backlogPath));

        Board board;
        try
        {
            board = YamlIndex.Parse(File.ReadAllText(backlogPath));
        }
        catch (YamlException e)
        {
            return Fail(issues, Describe(e), Path.GetFileName(backlogPath));
        }
        catch (Exception e)
        {
            return Fail(issues, e.Message, Path.GetFileName(backlogPath));
        }

        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenEpics = new HashSet<int>();
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var epic in board.Epics)
        {
            if (!seenEpics.Add(epic.Number))
                issues.Add(new ValidationIssue("error", $"Epic {epic.Number} appears more than once.", null));

            foreach (var story in epic.Stories)
            {
                var where = string.IsNullOrWhiteSpace(story.Code) ? $"epic {epic.Number}" : story.Code;

                if (string.IsNullOrWhiteSpace(story.Code))
                    issues.Add(new ValidationIssue("error", $"A story in epic {epic.Number} has no code.", where));
                else if (!seenCodes.Add(story.Code))
                    issues.Add(new ValidationIssue("error", $"{story.Code} is used more than once.", where));

                if (!Statuses.Contains(story.Status))
                    issues.Add(new ValidationIssue("error",
                        $"\"{story.Status}\" is not a status. Use one of: {string.Join(", ", Statuses)}.", where));

                if (!string.IsNullOrWhiteSpace(story.Release) && !board.Roadmap.Contains(story.Release))
                    issues.Add(new ValidationIssue("warning",
                        $"{story.Code} is set to release {story.Release}, which is not in the roadmap.", where));

                if (string.IsNullOrWhiteSpace(story.Folder))
                {
                    issues.Add(new ValidationIssue("error", $"{story.Code} has no folder.", where));
                    continue;
                }

                referenced.Add(story.Folder);

                string dir;
                try
                {
                    dir = StoryFolder.Dir(skillsRoot, story.Folder);
                }
                catch (BacklogValidationException e)
                {
                    issues.Add(new ValidationIssue("error", e.Message, where));
                    continue;
                }

                if (!Directory.Exists(dir))
                {
                    issues.Add(new ValidationIssue("error",
                        $"{story.Code} points at \"{story.Folder}\", which does not exist.", where));
                    continue;
                }

                CheckStoryFiles(skillsRoot, story, issues);
            }
        }

        if (Directory.Exists(skillsRoot))
        {
            foreach (var dir in Directory.GetDirectories(skillsRoot))
            {
                var name = Path.GetFileName(dir);
                if (!referenced.Contains(name))
                    issues.Add(new ValidationIssue("warning",
                        $"The folder \"{name}\" is not referenced by any story.", name));
            }
        }

        return new ValidationReport(!issues.Any(i => i.Severity == "error"), issues);
    }

    private static void CheckStoryFiles(string skillsRoot, Story story, List<ValidationIssue> issues)
    {
        try
        {
            var detail = StoryFolder.Read(skillsRoot, story.Folder);

            foreach (var tc in detail.TestCases.Where(t => !TestStatuses.Contains(t.Status)))
                issues.Add(new ValidationIssue("error",
                    $"\"{tc.Status}\" is not a test-case status. Use {string.Join(", ", TestStatuses)}.",
                    $"{story.Folder}/test-cases.yaml"));
        }
        catch (YamlException e)
        {
            issues.Add(new ValidationIssue("error", Describe(e), $"{story.Folder}/tasks.yaml"));
        }
        catch (Exception e)
        {
            issues.Add(new ValidationIssue("error", e.Message, story.Folder));
        }
    }

    /// <summary>YamlDotNet carries the position of the problem — surface it, because "bad
    /// indentation" without a line number is useless in a 300-line file.</summary>
    private static string Describe(YamlException e)
    {
        var reason = e.InnerException?.Message ?? e.Message;
        return $"Line {e.Start.Line}, column {e.Start.Column}: {reason}";
    }

    private static ValidationReport Fail(List<ValidationIssue> issues, string message, string? where)
    {
        issues.Add(new ValidationIssue("error", message, where));
        return new ValidationReport(false, issues);
    }
}
