namespace MiniTracker.Api.Backlog;

/// <summary>
/// The one place that decides whether a resolved path is genuinely inside a root directory.
///
/// Both callers take a path fragment out of a file a person can edit — a story's `folder:` in
/// BACKLOG.yaml, a skill path sent by the browser — so this is a trust boundary, and it is worth
/// having exactly one implementation of it rather than two that drift.
/// </summary>
public static class PathSafety
{
    /// <summary>
    /// True when <paramref name="candidate"/> sits strictly beneath <paramref name="root"/>.
    /// Both must already be absolute and canonical (<see cref="Path.GetFullPath(string)"/>).
    ///
    /// Uses <see cref="Path.GetRelativePath"/> rather than a string prefix comparison, because the
    /// prefix test needs a <see cref="StringComparison"/> and there is no single correct one:
    /// case-insensitive is right on Windows and wrong on Linux, where it would let a sibling
    /// directory differing only in case satisfy the check. GetRelativePath applies the platform's
    /// own rules, so this is correct on both — and it also catches a candidate on a different
    /// drive, which a prefix test gets right only by accident.
    /// </summary>
    public static bool IsInside(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);

        // A different drive or root leaves GetRelativePath with nothing to relativise, so it hands
        // back the absolute path unchanged.
        if (Path.IsPathRooted(relative)) return false;

        // "." is the root itself. It must not count as "inside": a story whose folder resolved to
        // the skills root would take every other story's folder with it when deleted.
        if (relative == "." || relative.Length == 0) return false;

        return !EscapesUpward(relative);
    }

    private static bool EscapesUpward(string relative) =>
        relative == ".."
        || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
}
