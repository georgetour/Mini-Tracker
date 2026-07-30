namespace MiniTracker.Api.Backlog;

/// <summary>Raised when a requested addition would produce an invalid or duplicated backlog.
/// The message is written for the person using the app, not for a log file.</summary>
public sealed class BacklogValidationException(string message) : Exception(message);

/// <summary>
/// Adds new epics and stories to BACKLOG.md as text. Like <see cref="BacklogWriter"/> this is
/// text-in, text-out and append-only at the insertion point: existing lines are never rewritten,
/// so a generated addition shows up in a diff as purely added lines.
/// </summary>
public static class BacklogGenerator
{
    /// <summary>Appends a new epic section at the end of the file.</summary>
    public static string AddEpic(string md, int number, string title)
    {
        title = (title ?? "").Trim();
        if (number < 0 || number > 999)
            throw new BacklogValidationException("Use an epic number between 0 and 999.");
        if (title.Length == 0)
            throw new BacklogValidationException("Give the epic a title.");
        if (title.Length > 120)
            throw new BacklogValidationException("Keep the epic title under 120 characters.");

        var board = BacklogParser.Parse(md);
        if (board.Epics.Any(e => e.Number == number))
            throw new BacklogValidationException($"Epic {number} already exists. Pick another number.");

        var eol = DetectEol(md);
        var body = string.Join(eol, new[]
        {
            "", "---", "",
            $"# Epic {number}: {title}", "",
        });

        return md.TrimEnd('\r', '\n') + eol + body;
    }

    /// <summary>Inserts a story at the end of its epic, just before the next epic heading.</summary>
    public static string AddStory(string md, int epicNumber, string code, string title,
                                  string? release = null, string? skillPath = null)
    {
        code = (code ?? "").Trim();
        title = (title ?? "").Trim();
        release = (release ?? "").Trim();
        skillPath = (skillPath ?? "").Trim();

        if (code.Length == 0)
            throw new BacklogValidationException("Give the story a code, for example US-25.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"^US-\d{2,}$"))
            throw new BacklogValidationException("Use US- followed by at least two digits, for example US-25.");
        if (title.Length == 0)
            throw new BacklogValidationException("Give the story a title.");
        if (title.Length > 120)
            throw new BacklogValidationException("Keep the story title under 120 characters.");

        var board = BacklogParser.Parse(md);
        if (!board.Epics.Any(e => e.Number == epicNumber))
            throw new BacklogValidationException($"There is no epic {epicNumber} to add this story to.");
        if (board.Epics.SelectMany(e => e.Stories).Any(s => s.Code == code))
            throw new BacklogValidationException($"{code} is already used. Pick another code.");

        var eol = DetectEol(md);
        var lines = md.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();

        var insertAt = FindEpicEnd(lines, epicNumber);
        var section = BuildStorySection(code, title, release, skillPath);

        // At the very end of the file there is no following blank line to lean on, so the
        // section supplies its own; anywhere else it would double up with the existing one.
        if (insertAt >= lines.Count) section.Add("");

        lines.InsertRange(insertAt, section);
        return string.Join(eol, lines);
    }

    /// <summary>Renames an epic, leaving its stories untouched.</summary>
    public static string RenameEpic(string md, int number, string title)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0) throw new BacklogValidationException("Give the epic a title.");
        if (title.Length > 120) throw new BacklogValidationException("Keep the epic title under 120 characters.");

        var eol = DetectEol(md);
        var lines = md.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"^#\s+Epic\s+(\d+):");
            if (m.Success && int.Parse(m.Groups[1].Value) == number)
            {
                lines[i] = $"# Epic {number}: {title}";
                return string.Join(eol, lines);
            }
        }
        throw new BacklogValidationException($"There is no epic {number}.");
    }

    /// <summary>Removes a story and everything under it, up to the next story or epic.</summary>
    public static string RemoveStory(string md, string code)
    {
        var eol = DetectEol(md);
        var lines = md.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();

        var start = lines.FindIndex(l =>
            System.Text.RegularExpressions.Regex.IsMatch(l, $@"^##\s+{System.Text.RegularExpressions.Regex.Escape(code)}\s+·"));
        if (start < 0) throw new BacklogValidationException($"There is no story {code}.");

        var end = lines.Count;
        for (var j = start + 1; j < lines.Count; j++)
            if (System.Text.RegularExpressions.Regex.IsMatch(lines[j], @"^##?\s+(US-\d+|Epic\s+\d+:)"))
            { end = j; break; }

        // Take the blank line that separated it too, so removals don't leave growing gaps.
        while (end > start && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
        if (end < lines.Count && string.IsNullOrWhiteSpace(lines[end])) end++;

        lines.RemoveRange(start, end - start);
        return string.Join(eol, lines);
    }

    /// <summary>Removes an epic and every story inside it.</summary>
    public static string RemoveEpic(string md, int number)
    {
        var eol = DetectEol(md);
        var lines = md.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();

        var start = lines.FindIndex(l =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(l, @"^#\s+Epic\s+(\d+):");
            return m.Success && int.Parse(m.Groups[1].Value) == number;
        });
        if (start < 0) throw new BacklogValidationException($"There is no epic {number}.");

        var end = lines.Count;
        for (var j = start + 1; j < lines.Count; j++)
            if (System.Text.RegularExpressions.Regex.IsMatch(lines[j], @"^#\s+Epic\s+\d+:"))
            { end = j; break; }

        // Drop a trailing "---" rule that belonged to this epic, plus surrounding blanks.
        while (end > start && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
        if (end > start && lines[end - 1].Trim() == "---") end--;
        while (end > start && string.IsNullOrWhiteSpace(lines[end - 1])) end--;
        if (end < lines.Count && string.IsNullOrWhiteSpace(lines[end])) end++;

        lines.RemoveRange(start, end - start);
        return string.Join(eol, lines);
    }

    /// <summary>Records a skill path on a story that has none, without touching anything else.</summary>
    public static string SetStorySkill(string md, string code, string skillPath)
    {
        skillPath = (skillPath ?? "").Trim();
        if (skillPath.Length == 0) throw new BacklogValidationException("Give the skill file a path.");

        var eol = DetectEol(md);
        var lines = md.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        var start = Array.FindIndex(lines, l =>
            System.Text.RegularExpressions.Regex.IsMatch(l, $@"^##\s+{System.Text.RegularExpressions.Regex.Escape(code)}\s+·"));
        if (start < 0) throw new BacklogValidationException($"There is no story {code}.");

        for (var i = start; i < lines.Length; i++)
        {
            if (i > start && System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"^##?\s+(US-\d+|Epic\s+\d+:)")) break;
            if (!lines[i].Contains("**Status**:")) continue;

            if (lines[i].Contains("**Skill**:"))
                lines[i] = System.Text.RegularExpressions.Regex.Replace(
                    lines[i], @"\*\*Skill\*\*:\s*`[^`]*`", $"**Skill**: `{skillPath}`");
            else
            {
                // Insert the field after the status token, keeping the " · " separators intact.
                var idx = lines[i].IndexOf(" · ", StringComparison.Ordinal);
                lines[i] = idx < 0
                    ? lines[i].TrimEnd() + $" · **Skill**: `{skillPath}`"
                    : lines[i][..idx] + $" · **Skill**: `{skillPath}`" + lines[i][idx..];
            }
            return string.Join(eol, lines);
        }
        throw new BacklogValidationException($"Story {code} has no status line to attach a skill to.");
    }

    /// <summary>Line index just past the last line of an epic — i.e. where its next story goes.</summary>
    private static int FindEpicEnd(List<string> lines, int epicNumber)
    {
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"^#\s+Epic\s+(\d+):");
            if (m.Success && int.Parse(m.Groups[1].Value) == epicNumber) { start = i; break; }
        }
        if (start < 0) throw new BacklogValidationException($"There is no epic {epicNumber} to add this story to.");

        for (var j = start + 1; j < lines.Count; j++)
            if (System.Text.RegularExpressions.Regex.IsMatch(lines[j], @"^#\s+Epic\s+\d+:"))
                return TrimBlankTail(lines, j);

        return TrimBlankTail(lines, lines.Count);
    }

    /// <summary>Backs up over trailing blank lines and a `---` rule so the new section lands
    /// directly after the last story rather than after the separator.</summary>
    private static int TrimBlankTail(List<string> lines, int index)
    {
        var i = index;
        while (i > 0 && string.IsNullOrWhiteSpace(lines[i - 1])) i--;
        if (i > 0 && lines[i - 1].Trim() == "---") i--;
        while (i > 0 && string.IsNullOrWhiteSpace(lines[i - 1])) i--;
        return i;
    }

    private static List<string> BuildStorySection(string code, string title, string release, string skillPath)
    {
        var parts = new List<string> { "> **Status**: ⬜ Not Yet Started" };
        if (skillPath.Length > 0) parts.Add($"**Skill**: `{skillPath}`");
        if (release.Length > 0) parts.Add($"**Release**: {release}");

        return new List<string>
        {
            "",
            $"## {code} · {title}",
            "",
            string.Join(" · ", parts),
            ">",
            "> Describe the story here.",
            "",
            "### Tasks",
            "",
            "| # | Task | ✓ |",
            "|---|------|---|",
            $"| {StoryNumber(code)}.0 | First task | ⬜ |",
            "",
            "### Test Cases",
            "",
            "| ID | Description | Status | Notes |",
            "|----|-------------|--------|-------|",
            $"| TC-{StoryNumber(code)}-01 | First test case | ⬜ Not Run | |",
        };
    }

    /// <summary>"US-07" -> "07", used for the task and test-case identifiers.</summary>
    private static string StoryNumber(string code) => code[3..];

    private static string DetectEol(string md) => md.Contains("\r\n") ? "\r\n" : "\n";
}
