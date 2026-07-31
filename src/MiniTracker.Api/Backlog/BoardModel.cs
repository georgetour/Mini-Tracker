namespace MiniTracker.Api.Backlog;

/// <summary>
/// The index: every epic and story, and nothing else. Tasks and test cases live in each story's own
/// folder and are loaded only when that story is opened — which is what keeps a 1000-story board at
/// ~35ms. Holding them here instead measured 270ms, because every board load parsed detail the
/// board never renders.
/// </summary>
public sealed record Board(
    string Project,
    IReadOnlyList<string> Roadmap,
    IReadOnlyList<Epic> Epics);

/// <param name="Slug">URL segment, e.g. "core-application". Derived from the title by
/// <see cref="Slugs"/> on every read — never stored in the file, so it can't drift from the title.</param>
public sealed record Epic(int Number, string Title, IReadOnlyList<Story> Stories)
{
    public string Slug { get; init; } = "";
}

/// <param name="Status">A plain word: "In Progress", "Done". No emoji — those are presentation and
/// live in the browser.</param>
/// <param name="Folder">Directory under the skills root holding this story's SKILL.md, tasks.yaml
/// and test-cases.yaml. Stored in the file, unlike the slug, because renaming a story must not
/// silently orphan its folder.</param>
public sealed record Story(
    string Code,
    string Title,
    string Status,
    string Release,
    string Folder)
{
    public string Slug { get; init; } = "";
}

public sealed record TaskItem(string Text, bool Done);

public sealed record TestCase(string Text, string Status);

/// <summary>What a story's folder holds. Loaded on demand, never with the board.</summary>
public sealed record StoryDetail(
    IReadOnlyList<TaskItem> Tasks,
    IReadOnlyList<TestCase> TestCases);
