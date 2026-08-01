using YamlDotNet.Core;

namespace MiniTracker.Api.Backlog;

/// <param name="Detail">Optional monospace block — for a parse error, the offending line and, where
/// the mistake is recognisable, the corrected line to copy over it.</param>
public sealed record ValidationIssue(string Severity, string Message, string? Where, string? Detail = null);

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
        var backlogText = "";
        try
        {
            backlogText = File.ReadAllText(backlogPath);
            board = YamlIndex.Parse(backlogText);
        }
        catch (YamlException e)
        {
            var explained = YamlDiagnostic.Explain(backlogText, e);
            issues.Add(new ValidationIssue("error", explained.Message,
                Path.GetFileName(backlogPath), explained.Detail));
            return new ValidationReport(false, issues);
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
                    dir = StoryFolder.DirectoryFor(skillsRoot, story.Folder);
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

                CheckFolderName(story, issues);
                CheckStoryStructure(dir, story, issues);
                CheckStoryFiles(skillsRoot, story, issues);
            }
        }

        if (Directory.Exists(skillsRoot))
        {
            foreach (var dir in Directory.GetDirectories(skillsRoot))
            {
                var name = Path.GetFileName(dir);
                if (!referenced.Contains(name)) CheckUnreferencedFolder(dir, name, issues);
            }
        }

        // Errors before warnings: the thing stopping you working belongs at the top, not wherever
        // it happened to be discovered.
        var ordered = issues.OrderBy(i => i.Severity == "error" ? 0 : 1).ToList();
        return new ValidationReport(!ordered.Any(i => i.Severity == "error"), ordered);
    }

    /// <summary>
    /// A folder nobody references. Creating a folder does not create a story — the index is what
    /// decides which stories exist — so this is the one place that can say so, and it is worth
    /// saying: a folder sitting there with a SKILL.md in it looks like it ought to have shown up.
    ///
    /// An empty folder and a folder full of work are not the same problem, so they do not get the
    /// same sentence: one is leftovers, the other is writing that nothing links to.
    /// </summary>
    private static void CheckUnreferencedFolder(string dir, string name, List<ValidationIssue> issues)
    {
        var empty = !Directory.EnumerateFileSystemEntries(dir).Any();

        var what = empty
            ? $"The folder \"{name}\" is empty and no story points at it."
            : $"The folder \"{name}\" has files in it, but no story points at it, so nothing in it "
            + "is shown anywhere.";

        issues.Add(new ValidationIssue("warning",
            $"{what} Adding a folder does not add a story — the backlog file is what decides which "
          + $"stories exist. To keep it, add a user story and set its folder to \"{name}\"; "
          + "otherwise delete the folder.",
            name));
    }

    /// <summary>A folder name that is not a plain slug. It works on Windows and breaks on Linux —
    /// the worst kind of problem, invisible until someone else clones the project.</summary>
    private static readonly System.Text.RegularExpressions.Regex CleanSlug =
        new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void CheckFolderName(Story story, List<ValidationIssue> issues)
    {
        if (CleanSlug.IsMatch(story.Folder)) return;

        var why = story.Folder.Any(char.IsUpper) ? "it has capital letters"
                : story.Folder.Contains(' ')     ? "it has spaces"
                : story.Folder.Contains('.')     ? "it has dots"
                : "it has characters outside a-z, 0-9 and hyphen";

        var suggestion = Slugs.From(story.Folder);
        issues.Add(new ValidationIssue("warning",
            $"The folder \"{story.Folder}\" is not a plain slug — {why}. Rename the folder to "
          + $"\"{suggestion}\" and set {story.Code}'s folder to match. Windows ignores capitals and "
          + "Linux does not, so a name like this works on one machine and not the next.",
            story.Code));
    }

    /// <summary>The files a story folder is meant to hold. Missing tasks or test cases just means an
    /// empty list; a story with no SKILL.md has nowhere to say what it is.</summary>
    private static void CheckStoryStructure(string dir, Story story, List<ValidationIssue> issues)
    {
        if (!File.Exists(Path.Combine(dir, "SKILL.md")))
            issues.Add(new ValidationIssue("warning",
                $"{story.Code} has no SKILL.md, so it has no description. Open the story and use "
              + "Edit description to create one.",
                $"{story.Folder}/SKILL.md"));
    }

    /// <summary>Each file is read on its own so a parse error names the file that actually failed.
    /// Reading both together once reported a broken test-cases.yaml as a broken tasks.yaml.</summary>
    private static void CheckStoryFiles(string skillsRoot, Story story, List<ValidationIssue> issues)
    {
        var dir = StoryFolder.DirectoryFor(skillsRoot, story.Folder);

        Try(() => StoryFolder.ReadTasks(skillsRoot, story.Folder),
            $"{story.Folder}/tasks.yaml", Path.Combine(dir, "tasks.yaml"), issues);

        var cases = Try(() => StoryFolder.ReadTestCases(skillsRoot, story.Folder),
            $"{story.Folder}/test-cases.yaml", Path.Combine(dir, "test-cases.yaml"), issues);

        foreach (var tc in (cases ?? []).Where(t => !TestStatuses.Contains(t.Status)))
            issues.Add(new ValidationIssue("error",
                $"\"{tc.Status}\" is not a test-case status. Use {string.Join(", ", TestStatuses)}.",
                $"{story.Folder}/test-cases.yaml"));
    }

    private static T? Try<T>(Func<T> read, string where, string path, List<ValidationIssue> issues)
        where T : class
    {
        try { return read(); }
        catch (YamlException e)
        {
            var text = File.Exists(path) ? File.ReadAllText(path) : "";
            var explained = YamlDiagnostic.Explain(text, e);
            issues.Add(new ValidationIssue("error", explained.Message, where, explained.Detail));
        }
        catch (Exception e) { issues.Add(new ValidationIssue("error", e.Message, where)); }
        return null;
    }

    private static ValidationReport Fail(List<ValidationIssue> issues, string message, string? where)
    {
        issues.Add(new ValidationIssue("error", message, where));
        return new ValidationReport(false, issues);
    }
}
