namespace MiniTracker.Api.Backlog;

/// <summary>The whole board, parsed from BACKLOG.md. FileHash lets callers detect external edits.</summary>
/// <param name="RoadmapVersions">Versions declared in the Release Roadmap table (e.g. V0.1, V1 …) — seeds
/// the "By version" rollup so a roadmap version with zero stories still shows.</param>
public sealed record Board(
    IReadOnlyList<Epic> Epics,
    IReadOnlyList<string> RoadmapVersions,
    string FileHash);

/// <param name="Slug">URL segment for this epic, e.g. "core-application". Assigned by the parser
/// via <see cref="Slugs"/> and unique across the board — the browser builds links from it rather
/// than deriving its own.</param>
public sealed record Epic(int Number, string Title, IReadOnlyList<Story> Stories)
{
    public string Slug { get; init; } = "";
}

public sealed record Story(
    string Code,          // e.g. "US-21"
    string Title,
    StatusToken Status,   // authoritative status from the "> **Status**:" line
    string Release,       // e.g. "V0.1"
    string? SkillPath,    // e.g. "skills/backlog-tracker/SKILL.md" — set once, when a story is first
                          // given a spec; the file's contents are the story's description
    IReadOnlyList<TaskItem> Tasks,
    IReadOnlyList<TestCase> TestCases,
    int StatusLine)       // 0-based line index of the "> **Status**:" line (write locator)
{
    /// <summary>URL segment for this story, unique within its epic — "checkout-and-payment".</summary>
    public string Slug { get; init; } = "";
}

public sealed record TaskItem(string Id, string Text, bool Done, int Line);

public sealed record TestCase(string Id, string Description, StatusToken Status, int Line);

/// <summary>Emoji + label pair, e.g. ("🔍", "Under Review"). The label is what sync-status keys off.</summary>
public sealed record StatusToken(string Emoji, string Label);
