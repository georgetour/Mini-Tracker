using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MiniTracker.Api.Backlog;

/// <summary>
/// Parses BACKLOG.md into a <see cref="Board"/>. Line-oriented and tolerant: rows are located by
/// content signature and every editable item records its exact line so write-back can be surgical.
/// </summary>
public static partial class BacklogParser
{
    public static Board Parse(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        var epics = new List<Epic>();
        var roadmapVersions = new List<string>();
        EpicBuilder? epic = null;
        StoryBuilder? story = null;
        Section section = Section.None;

        // Test-case table column discovery (shape varies: some tables lead with TC|Verifies|Scenario|Status,
        // others with ID|Description|Status|Notes, and Epic 2 uses a "### Validation" heading).
        int tcStatusCol = -1, tcDescCol = -1, tcIdCol = 0;
        bool tcHeaderSeen = false;

        void FlushStory()
        {
            if (story is not null) epic!.Stories.Add(story.Build());
            story = null;
        }
        void FlushEpic()
        {
            FlushStory();
            if (epic is not null) epics.Add(epic.Build());
            epic = null;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            var mRoadmap = RoadmapRow().Match(line);
            if (mRoadmap.Success && !roadmapVersions.Contains(mRoadmap.Groups[1].Value))
                roadmapVersions.Add(mRoadmap.Groups[1].Value);

            var mEpic = EpicHeading().Match(line);
            if (mEpic.Success)
            {
                FlushEpic();
                epic = new EpicBuilder(int.Parse(mEpic.Groups[1].Value), mEpic.Groups[2].Value.Trim());
                section = Section.None;
                continue;
            }

            var mStory = StoryHeading().Match(line);
            if (mStory.Success && epic is not null)
            {
                FlushStory();
                story = new StoryBuilder(mStory.Groups[1].Value, mStory.Groups[2].Value.Trim());
                section = Section.None;
                continue;
            }

            if (story is not null && StatusLine().IsMatch(line))
            {
                var m = StatusLine().Match(line);
                story.Status = new StatusToken(m.Groups[1].Value, m.Groups[2].Value.Trim());
                story.StatusLine = i;
                var skill = SkillField().Match(line);
                if (skill.Success) story.SkillPath = skill.Groups[1].Value.Trim();
                var rel = ReleaseField().Match(line);
                if (rel.Success) story.Release = rel.Groups[1].Value;
                continue;
            }

            if (line.StartsWith("### "))
            {
                var h = line[4..].Trim();
                section = h.StartsWith("Tasks") ? Section.Tasks
                    : (h.StartsWith("Test Cases") || h.StartsWith("Validation")) ? Section.TestCases
                    : Section.None;
                tcHeaderSeen = false; tcStatusCol = -1; tcDescCol = -1; tcIdCol = 0;
                continue;
            }

            if (story is null || section == Section.None || !line.TrimStart().StartsWith('|')) continue;

            var cells = SplitRow(line);
            if (cells.Count == 0 || IsSeparatorRow(cells)) continue;

            if (section == Section.Tasks)
            {
                // Data row = first cell is a task id like "21.0" / "5.1"; skips the "| # | Task | ✓ |" header.
                if (cells.Count >= 3 && TaskId().IsMatch(cells[0]))
                {
                    var done = cells[^1].Contains('✅');
                    story.Tasks.Add(new TaskItem(cells[0], cells[1], done, i));
                }
                continue;
            }

            // Section.TestCases — first pipe row is the header; find the Status/Description columns by name.
            if (!tcHeaderSeen)
            {
                for (var c = 0; c < cells.Count; c++)
                {
                    var name = cells[c].ToLowerInvariant();
                    if (name == "status") tcStatusCol = c;
                    else if (name is "description" or "scenario" or "scenario (do → expect)") tcDescCol = c;
                }
                if (tcDescCol < 0) tcDescCol = Math.Min(1, cells.Count - 1);
                tcHeaderSeen = true;
                continue;
            }

            if (tcStatusCol >= 0 && tcStatusCol < cells.Count && cells.Count > tcIdCol)
            {
                var id = cells[tcIdCol];
                var desc = tcDescCol < cells.Count ? cells[tcDescCol] : "";
                story.TestCases.Add(new TestCase(id, desc, ParseStatusCell(cells[tcStatusCol]), i));
            }
        }

        FlushEpic();
        return new Board(AssignSlugs(epics), roadmapVersions, Sha256(markdown));
    }

    /// <summary>
    /// Gives every epic and story its URL segment. Epic slugs are unique across the board and avoid
    /// the app's own paths; story slugs only need to be unique inside their epic, because the story
    /// URL is always reached through it. Titles fall back to the epic number or story code when
    /// they contain nothing sluggable.
    /// </summary>
    private static List<Epic> AssignSlugs(List<Epic> epics)
    {
        var epicSlugs = Slugs.Unique(
            epics.Select(e => e.Title).ToList(),
            epics.Select(e => $"epic-{e.Number}").ToList(),
            topLevel: true);

        return epics.Select((epic, i) =>
        {
            var storySlugs = Slugs.Unique(
                epic.Stories.Select(s => s.Title).ToList(),
                epic.Stories.Select(s => s.Code).ToList(),
                topLevel: false);

            var stories = epic.Stories.Select((s, j) => s with { Slug = storySlugs[j] }).ToList();
            return epic with { Slug = epicSlugs[i], Stories = stories };
        }).ToList();
    }

    /// <summary>Splits a markdown table row into trimmed cells, honoring escaped "\|" pipes.</summary>
    private static List<string> SplitRow(string line)
    {
        var t = line.Trim();
        var cells = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < t.Length; i++)
        {
            var ch = t[i];
            if (ch == '\\' && i + 1 < t.Length && t[i + 1] == '|') { sb.Append('|'); i++; }
            else if (ch == '|') { cells.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        if (sb.Length > 0) cells.Add(sb.ToString().Trim());
        // A row "| a | b |" yields ["", "a", "b", ""] -> drop leading/trailing empties from the pipe borders.
        if (cells.Count > 0 && cells[0].Length == 0) cells.RemoveAt(0);
        if (cells.Count > 0 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);
        return cells;
    }

    private static bool IsSeparatorRow(List<string> cells) =>
        cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':' or ' '));

    private static StatusToken ParseStatusCell(string cell)
    {
        var t = cell.Trim();
        var sp = t.IndexOf(' ');
        return sp < 0 ? new StatusToken(t, "") : new StatusToken(t[..sp], t[(sp + 1)..].Trim());
    }

    private static string Sha256(string s)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private enum Section { None, Tasks, TestCases }

    private sealed class EpicBuilder(int number, string title)
    {
        public List<Story> Stories { get; } = new();
        public Epic Build() => new(number, title, Stories);
    }

    private sealed class StoryBuilder(string code, string title)
    {
        public StatusToken Status { get; set; } = new("", "");
        public string Release { get; set; } = "";
        public string? SkillPath { get; set; }
        public int StatusLine { get; set; } = -1;
        public List<TaskItem> Tasks { get; } = new();
        public List<TestCase> TestCases { get; } = new();
        public Story Build() => new(code, title, Status, Release, SkillPath, Tasks, TestCases, StatusLine);
    }

    [GeneratedRegex(@"^#\s+Epic\s+(\d+):\s*(.+?)\s*$")]
    private static partial Regex EpicHeading();

    [GeneratedRegex(@"^##\s+(US-\d+)\s+·\s+(.+?)\s*$")]
    private static partial Regex StoryHeading();

    [GeneratedRegex(@"^>\s*\*\*Status\*\*:\s*(\S+)\s+(.+?)\s*·")]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"\*\*Skill\*\*:\s*`?([^`·\n]+?)`?\s*(?:·|$)")]
    private static partial Regex SkillField();

    // Matches "**Release**: V1" and "**When**: V3" — both carry a version in BACKLOG.md.
    [GeneratedRegex(@"\*\*(?:Release|When)\*\*:\s*(V[\d.]+)")]
    private static partial Regex ReleaseField();

    [GeneratedRegex(@"^\|\s*(V[\d.]+)\s*\|")]
    private static partial Regex RoadmapRow();

    [GeneratedRegex(@"^\d[\d.]*$")]
    private static partial Regex TaskId();
}
