namespace MiniTracker.Api.Services;

/// <summary>
/// Resolves a story's recorded skill path (e.g. "skills/backlog-tooling/", read verbatim from
/// BACKLOG.md's "**Skill**:" field) to an absolute SKILL.md path under the configured skills root —
/// the folder those recorded paths are relative to. Rejects anything that would escape that root.
/// </summary>
public static class SkillFileResolver
{
    public static string? Resolve(string skillsRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var trimmed = relativePath.Trim();
        if (!trimmed.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            trimmed = Path.Combine(trimmed, "SKILL.md");

        var rootFull = Path.GetFullPath(skillsRoot);
        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull : rootFull + Path.DirectorySeparatorChar;

        var candidate = Path.GetFullPath(Path.Combine(rootFull, trimmed));
        return candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }
}
