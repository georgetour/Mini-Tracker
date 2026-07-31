using System.Text;
using MiniTracker.Api.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MiniTracker.Api.Backlog;

/// <summary>
/// One story's folder: SKILL.md (prose), tasks.yaml and test-cases.yaml (state). Loaded only when
/// that story is opened, never with the board.
///
/// Tasks and test cases are separate files because they are separate concerns — one measures
/// whether the work is built, the other whether it works — and ticking a task should not rewrite
/// test data.
/// </summary>
public static class StoryFolder
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private sealed class TaskDto
    {
        public string Text { get; set; } = "";
        public bool Done { get; set; }
    }

    private sealed class TestCaseDto
    {
        public string Text { get; set; } = "";
        public string Status { get; set; } = "Not Run";
    }

    private static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>Resolves a folder name under the skills root, refusing anything that would escape
    /// it. The folder name comes from the backlog file, which a person can edit — so it is
    /// untrusted input like any other.</summary>
    public static string Dir(string skillsRoot, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new BacklogValidationException("That story has no folder.");

        var rootFull = Path.GetFullPath(skillsRoot);

        string candidate;
        try { candidate = Path.GetFullPath(Path.Combine(rootFull, folder)); }
        catch (Exception) { throw new BacklogValidationException($"\"{folder}\" is not a usable folder name."); }

        if (!PathSafety.IsInside(rootFull, candidate))
            throw new BacklogValidationException($"\"{folder}\" points outside the skills folder.");

        return candidate;
    }

    public static string SkillPath(string skillsRoot, string folder) =>
        Path.Combine(Dir(skillsRoot, folder), "SKILL.md");

    public static StoryDetail Read(string skillsRoot, string folder)
    {
        var dir = Dir(skillsRoot, folder);
        return new StoryDetail(
            ReadList<TaskDto>(Path.Combine(dir, "tasks.yaml"))
                .Select(t => new TaskItem(t.Text ?? "", t.Done)).ToList(),
            ReadList<TestCaseDto>(Path.Combine(dir, "test-cases.yaml"))
                .Select(t => new TestCase(t.Text ?? "", string.IsNullOrWhiteSpace(t.Status) ? "Not Run" : t.Status))
                .ToList());
    }

    public static void WriteTasks(string skillsRoot, string folder, IReadOnlyList<TaskItem> tasks)
    {
        var dir = Dir(skillsRoot, folder);
        Directory.CreateDirectory(dir);
        WriteList(Path.Combine(dir, "tasks.yaml"),
            tasks.Select(t => new TaskDto { Text = t.Text, Done = t.Done }).ToList());
    }

    public static void WriteTestCases(string skillsRoot, string folder, IReadOnlyList<TestCase> cases)
    {
        var dir = Dir(skillsRoot, folder);
        Directory.CreateDirectory(dir);
        WriteList(Path.Combine(dir, "test-cases.yaml"),
            cases.Select(c => new TestCaseDto { Text = c.Text, Status = c.Status }).ToList());
    }

    /// <summary>Creates the folder and its SKILL.md from the template. Never overwrites a SKILL.md
    /// that already exists — a description someone wrote is not ours to replace.</summary>
    public static void Create(string skillsRoot, string folder, string storyCode, string storyTitle)
    {
        var dir = Dir(skillsRoot, folder);
        Directory.CreateDirectory(dir);

        var skill = Path.Combine(dir, "SKILL.md");
        if (File.Exists(skill)) return;

        var template = File.ReadAllText(TemplateLocator.Find("SKILL.template.md"))
            .Replace("skill-name-here", folder)
            .Replace("# [Skill Name]", $"# {storyCode} · {storyTitle}");
        File.WriteAllText(skill, template, Utf8NoBom);
    }

    public static void Delete(string skillsRoot, string folder)
    {
        var dir = Dir(skillsRoot, folder);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private static List<T> ReadList<T>(string path)
    {
        if (!File.Exists(path)) return new List<T>();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new List<T>();
        return Reader.Deserialize<List<T>>(text) ?? new List<T>();
    }

    // An empty list is written as "[]" rather than an empty file, so the file's presence always
    // means "this was written deliberately" rather than "something failed halfway".
    private static void WriteList<T>(string path, List<T> items) =>
        File.WriteAllText(path, items.Count == 0 ? "[]\n" : Writer.Serialize(items), Utf8NoBom);
}
