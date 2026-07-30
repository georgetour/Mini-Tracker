using System.Text.RegularExpressions;

namespace MiniTracker.Api.Backlog;

/// <summary>
/// Applies a single status change to BACKLOG.md text by locating the target line by content signature
/// and swapping only the status token — never re-rendering the file. Preserves all other bytes,
/// per-cell whitespace, and the file's line endings. Text-in, text-out; the service composes this with
/// summary regeneration and the actual file write.
/// </summary>
public static partial class BacklogWriter
{
    public static string SetStoryStatus(string md, string storyCode, StatusToken status)
    {
        var lines = md.Split('\n');
        var (start, end) = Section(lines, storyCode);
        for (var i = start; i < end; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith('>') && lines[i].Contains("**Status**:"))
            {
                lines[i] = ReplaceStatusToken(lines[i], $"{status.Emoji} {status.Label}");
                break;
            }
        }
        return string.Join('\n', lines);
    }

    public static string SetTaskDone(string md, string storyCode, string taskId, bool done)
    {
        var lines = md.Split('\n');
        var (start, end) = Section(lines, storyCode);
        for (var i = start; i < end; i++)
        {
            if (FirstCell(lines[i]) == taskId)
            {
                var pipes = PipePositions(lines[i]);
                lines[i] = ReplaceCell(lines[i], pipes.Count - 2, done ? "✅" : "⬜");
                break;
            }
        }
        return string.Join('\n', lines);
    }

    public static string SetTestCaseStatus(string md, string storyCode, string tcId, StatusToken status)
    {
        var lines = md.Split('\n');
        var (start, end) = Section(lines, storyCode);
        var inTests = false; var headerSeen = false; var statusCol = -1;

        for (var i = start; i < end; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("### "))
            {
                var h = t[4..].Trim();
                inTests = h.StartsWith("Test Cases") || h.StartsWith("Validation");
                headerSeen = false; statusCol = -1;
                continue;
            }
            if (!inTests || !t.StartsWith('|')) continue;

            var cells = SplitRow(lines[i]);
            if (IsSeparatorRow(cells)) continue;

            if (!headerSeen)
            {
                for (var c = 0; c < cells.Count; c++)
                    if (cells[c].Equals("Status", StringComparison.OrdinalIgnoreCase)) statusCol = c;
                headerSeen = true;
                continue;
            }

            if (statusCol >= 0 && FirstCell(lines[i]) == tcId)
            {
                lines[i] = ReplaceCell(lines[i], statusCol, $"{status.Emoji} {status.Label}");
                break;
            }
        }
        return string.Join('\n', lines);
    }

    // --- location & cell helpers (mirror the parser; kept local so the writer stands alone) ---

    private static (int start, int end) Section(string[] lines, string code)
    {
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var m = StoryHeading().Match(lines[i]);
            if (m.Success && m.Groups[1].Value == code) { start = i; break; }
        }
        if (start < 0) return (0, 0);

        var end = lines.Length;
        for (var j = start + 1; j < lines.Length; j++)
        {
            var t = lines[j].TrimStart();
            if (t.StartsWith("## ") || t.StartsWith("# ")) { end = j; break; }
        }
        return (start, end);
    }

    private static string ReplaceStatusToken(string line, string newToken)
    {
        const string marker = "**Status**:";
        var m = line.IndexOf(marker, StringComparison.Ordinal);
        if (m < 0) return line;
        var s = m + marker.Length;
        while (s < line.Length && line[s] == ' ') s++;

        var contentEnd = line.Length;
        if (contentEnd > 0 && line[contentEnd - 1] == '\r') contentEnd--;
        var e = line.IndexOf(" ·", s, StringComparison.Ordinal);
        if (e < 0 || e > contentEnd) e = contentEnd;

        return line[..s] + newToken + line[e..];
    }

    private static List<int> PipePositions(string line)
    {
        var r = new List<int>();
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] == '|') { i++; continue; }
            if (line[i] == '|') r.Add(i);
        }
        return r;
    }

    private static string ReplaceCell(string line, int cellIndex, string newTrimmed)
    {
        var pipes = PipePositions(line);
        if (cellIndex < 0 || cellIndex + 1 >= pipes.Count) return line;
        int segStart = pipes[cellIndex] + 1, segEnd = pipes[cellIndex + 1];
        var cs = segStart; while (cs < segEnd && line[cs] == ' ') cs++;
        var ce = segEnd; while (ce > cs && line[ce - 1] == ' ') ce--;
        return line[..cs] + newTrimmed + line[ce..];
    }

    private static string FirstCell(string line)
    {
        var p = PipePositions(line);
        return p.Count < 2 ? "" : line.Substring(p[0] + 1, p[1] - p[0] - 1).Trim();
    }

    private static List<string> SplitRow(string line)
    {
        var t = line.Trim();
        var cells = new List<string>();
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < t.Length; i++)
        {
            var ch = t[i];
            if (ch == '\\' && i + 1 < t.Length && t[i + 1] == '|') { sb.Append('|'); i++; }
            else if (ch == '|') { cells.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        if (sb.Length > 0) cells.Add(sb.ToString().Trim());
        if (cells.Count > 0 && cells[0].Length == 0) cells.RemoveAt(0);
        if (cells.Count > 0 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);
        return cells;
    }

    private static bool IsSeparatorRow(List<string> cells) =>
        cells.Count > 0 && cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':' or ' '));

    [GeneratedRegex(@"^##\s+(US-\d+)\s+·\s+(.+?)\s*$")]
    private static partial Regex StoryHeading();
}
