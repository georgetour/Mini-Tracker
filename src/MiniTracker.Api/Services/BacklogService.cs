using System.Text;
using MiniTracker.Api.Backlog;

namespace MiniTracker.Api.Services;

/// <summary>
/// Read/write gateway over BACKLOG.md. Stateless w.r.t. board data — the file is re-read on each call
/// so external edits (a CI push, a hand edit) are always reflected. The path itself is also resolved
/// fresh on each call via <paramref name="resolvePath"/>, so pointing the tracker at a different file
/// through Configure takes effect immediately, no restart. Writes are serialized and go through the
/// surgical <see cref="BacklogWriter"/>; a story-status change also regenerates the STATUS-SUMMARY
/// block (task/test-case changes don't affect the roll-up).
/// </summary>
public sealed class BacklogService(Func<string> resolvePath)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _lock = new();

    public string BacklogPath => resolvePath();

    public Board GetBoard()
    {
        lock (_lock) return BacklogParser.Parse(Read(resolvePath()));
    }

    public Board SetStoryStatus(string code, StatusToken status)
    {
        lock (_lock)
        {
            var path = resolvePath();
            var md = BacklogWriter.SetStoryStatus(Read(path), code, status);
            md = RegenerateSummaryBlock(md);   // story status feeds the roll-up
            Write(path, md);
            return BacklogParser.Parse(md);
        }
    }

    public Board SetTaskDone(string code, string taskId, bool done)
    {
        lock (_lock)
        {
            var path = resolvePath();
            var md = BacklogWriter.SetTaskDone(Read(path), code, taskId, done);
            Write(path, md);
            return BacklogParser.Parse(md);
        }
    }

    public Board SetTestCaseStatus(string code, string tcId, StatusToken status)
    {
        lock (_lock)
        {
            var path = resolvePath();
            var md = BacklogWriter.SetTestCaseStatus(Read(path), code, tcId, status);
            Write(path, md);
            return BacklogParser.Parse(md);
        }
    }

    /// <summary>Adds an epic, then refreshes the roll-up now that there is a new epic to count.</summary>
    public Board AddEpic(int number, string title)
    {
        lock (_lock)
        {
            var path = resolvePath();
            var md = RegenerateSummaryBlock(BacklogGenerator.AddEpic(Read(path), number, title));
            Write(path, md);
            return BacklogParser.Parse(md);
        }
    }

    /// <summary>Adds a story to an existing epic, then refreshes the roll-up.</summary>
    public Board AddStory(int epicNumber, string code, string title, string? release, string? skillPath)
    {
        lock (_lock)
        {
            var path = resolvePath();
            var md = RegenerateSummaryBlock(
                BacklogGenerator.AddStory(Read(path), epicNumber, code, title, release, skillPath));
            Write(path, md);
            return BacklogParser.Parse(md);
        }
    }

    /// <summary>Renames an epic. The roll-up carries the title, so it is refreshed too.</summary>
    public Board RenameEpic(int number, string title)
    {
        lock (_lock)
        {
            var path = resolvePath();
            var md = RegenerateSummaryBlock(BacklogGenerator.RenameEpic(Read(path), number, title));
            Write(path, md);
            return BacklogParser.Parse(md);
        }
    }

    /// <summary>Deletes an epic and every story in it.</summary>
    public Board DeleteEpic(int number)
    {
        lock (_lock)
        {
            var path = resolvePath();
            var md = RegenerateSummaryBlock(BacklogGenerator.RemoveEpic(Read(path), number));
            Write(path, md);
            return BacklogParser.Parse(md);
        }
    }

    /// <summary>Deletes a single story. Its SKILL.md is left on disk — deleting a backlog entry
    /// should not silently destroy a spec someone wrote.</summary>
    public Board DeleteStory(string code)
    {
        lock (_lock)
        {
            var path = resolvePath();
            var md = RegenerateSummaryBlock(BacklogGenerator.RemoveStory(Read(path), code));
            Write(path, md);
            return BacklogParser.Parse(md);
        }
    }

    /// <summary>Records a skill path against a story that had none.</summary>
    public Board SetStorySkill(string code, string skillPath)
    {
        lock (_lock)
        {
            var path = resolvePath();
            var md = BacklogGenerator.SetStorySkill(Read(path), code, skillPath);
            Write(path, md);
            return BacklogParser.Parse(md);
        }
    }

    /// <summary>Regenerates the summary in place and saves — the CLI 'sync-status' entry point.</summary>
    public void SyncStatus()
    {
        lock (_lock)
        {
            var path = resolvePath();
            Write(path, RegenerateSummaryBlock(Read(path)));
        }
    }

    private static string RegenerateSummaryBlock(string md)
    {
        var start = md.IndexOf(SummaryWriter.StartMarker, StringComparison.Ordinal);
        var end = md.IndexOf(SummaryWriter.EndMarker, StringComparison.Ordinal);
        if (start < 0 || end < start) return md; // no markers — leave untouched

        var eol = md.Contains("\r\n") ? "\r\n" : "\n";
        var block = SummaryWriter.Generate(BacklogParser.Parse(md), DateOnly.FromDateTime(DateTime.Now))
            .Replace("\n", eol);
        return md[..start] + block + md[(end + SummaryWriter.EndMarker.Length)..];
    }

    private static string Read(string path) => File.ReadAllText(path);
    private static void Write(string path, string md) => File.WriteAllText(path, md, Utf8NoBom);
}
