using System.Text.Json;
using MiniTracker.Api.Backlog;

namespace MiniTracker.Api.Services;

/// <summary>Logo/backlog/skills paths the UI edits at runtime. Nullable — an unset field just means
/// "not configured yet."</summary>
public sealed record TrackerConfig(string? BacklogPath, string? SkillsPath, string? LogoPath, bool IsDemo);

/// <summary>
/// Read/write gateway over the local, gitignored tracker.config.json. Re-reads from disk on every
/// call (no in-memory cache) so external edits are always reflected, matching BacklogService's
/// "re-read fresh every call" philosophy. Also owns backlog-path resolution — the one place that
/// decides which file Mini Tracker is actually pointed at right now.
/// </summary>
public sealed class TrackerConfigService(string configPath)
{
    private static readonly JsonSerializerOptions SaveOpts = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions LoadOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly object _lock = new();

    /// <summary>Reads the config, tolerating a missing or corrupt file by degrading to "nothing
    /// configured" — which self-heals via the demo path — rather than throwing. Accepts either
    /// PascalCase (what <see cref="Save"/> writes) or camelCase (what a hand-edit is likely to use).</summary>
    public TrackerConfig Load()
    {
        lock (_lock)
        {
            if (!File.Exists(configPath)) return new TrackerConfig(null, null, null, false);
            try
            {
                var json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<TrackerConfig>(json, LoadOpts) ?? new TrackerConfig(null, null, null, false);
            }
            catch (JsonException)
            {
                return new TrackerConfig(null, null, null, false);
            }
        }
    }

    private void Save(TrackerConfig config)
    {
        lock (_lock) File.WriteAllText(configPath, JsonSerializer.Serialize(config, SaveOpts));
    }

    /// <summary>Points the tracker at <paramref name="path"/>. If nothing exists there yet, creates it
    /// from the bundled BACKLOG.md template first — this is what lets Configure start tracking a
    /// brand-new project. Always clears IsDemo, since this is a deliberate user choice.</summary>
    public TrackerConfig SetBacklogPath(string path)
    {
        var full = ValidateBacklogPath(path);
        if (!File.Exists(full))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.Copy(TemplateLocator.Find("BACKLOG.template.md"), full);
        }
        var next = Load() with { BacklogPath = full, IsDemo = false };
        Save(next);
        return next;
    }

    /// <summary>
    /// Checks a backlog path and returns its absolute form. The browser checks the same things,
    /// but this is the API's own guarantee — the endpoint is reachable without the UI.
    /// </summary>
    public static string ValidateBacklogPath(string? path)
    {
        path = (path ?? "").Trim();
        if (path.Length == 0)
            throw new BacklogValidationException("Enter the path to your BACKLOG.md file.");
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new BacklogValidationException("Point this at a .md file, for example C:/projects/my-app/BACKLOG.md.");

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new BacklogValidationException("That doesn't look like a valid file path."); }

        if (Directory.Exists(full))
            throw new BacklogValidationException("That path is a folder. Include the file name, for example BACKLOG.md.");

        var parent = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(parent))
            throw new BacklogValidationException("Include the folder the file lives in, not just a file name.");

        return full;
    }

    /// <summary>Persists the folder story Skill paths (e.g. "skills/foo/") are relative to. No file
    /// creation — skills are authored per-story, not scaffolded up front.</summary>
    public TrackerConfig SetSkillsPath(string path)
    {
        var next = Load() with { SkillsPath = ValidateSkillsPath(path) };
        Save(next);
        return next;
    }

    /// <summary>Checks a skills folder and returns its absolute form. The folder must already
    /// exist — silently accepting a typo would make every skill link fail with no explanation.</summary>
    public static string ValidateSkillsPath(string? path)
    {
        path = (path ?? "").Trim();
        if (path.Length == 0)
            throw new BacklogValidationException("Enter the folder your skills/… paths start from.");

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new BacklogValidationException("That doesn't look like a valid folder path."); }

        if (File.Exists(full))
            throw new BacklogValidationException("That's a file. Give the folder your skills/… paths start from.");
        if (!Directory.Exists(full))
            throw new BacklogValidationException("That folder doesn't exist. Create it first, or leave this empty.");

        return full;
    }

    /// <summary>Sets the logo path, or clears it when <paramref name="webPath"/> is null.</summary>
    public TrackerConfig SetLogoPath(string? webPath)
    {
        var next = Load() with { LogoPath = webPath };
        Save(next);
        return next;
    }

    /// <summary>First-run bootstrap: materializes a live, writable demo backlog from the bundled
    /// template together with its skill files, and persists config pointing at both. Idempotent —
    /// existing files are never overwritten, so edits made to the demo survive a restart. Runs under
    /// <see cref="_lock"/> (re-entrant, so the nested Load/Save calls compose fine) so two concurrent
    /// first-run requests can't race the check-then-act file copies below.</summary>
    public TrackerConfig MaterializeDemo(string demoPath)
    {
        lock (_lock)
        {
            var demoDir = Path.GetDirectoryName(demoPath)!;
            Directory.CreateDirectory(demoDir);

            if (!File.Exists(demoPath))
                File.Copy(TemplateLocator.Find("BACKLOG.template.md"), demoPath);

            // Stories record skill paths as "skills/<slug>/SKILL.md", resolved relative to SkillsPath —
            // so the demo directory itself is the root, giving <demoDir>/skills/<slug>/SKILL.md.
            var templatesDir = Path.GetDirectoryName(TemplateLocator.Find("BACKLOG.template.md"))!;
            CopyDirectory(Path.Combine(templatesDir, "skills"), Path.Combine(demoDir, "skills"));

            var current = Load();
            // Only adopt the demo directory as SkillsPath when the user hasn't configured a real one
            // (or we're already in demo mode) — otherwise a backlog that's temporarily missing (moved
            // folder, unmounted drive) would silently clobber a real SkillsPath on fall-through.
            var skillsPath = (string.IsNullOrWhiteSpace(current.SkillsPath) || current.IsDemo)
                ? Path.GetFullPath(demoDir)
                : current.SkillsPath;
            var next = current with { BacklogPath = demoPath, SkillsPath = skillsPath, IsDemo = true };
            Save(next);
            return next;
        }
    }

    /// <summary>Recursive copy that never clobbers an existing file — keeps MaterializeDemo idempotent
    /// so a restart preserves whatever the user changed in the demo.</summary>
    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }

        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    /// <summary>Resolves the backlog path Mini Tracker should use right now, freshly, every call: an
    /// explicit deploy-time override always wins (and is never persisted); otherwise the configured
    /// path if it still exists; otherwise whatever BacklogLocator finds by walking up from the working
    /// directory; otherwise a live demo materialized from the bundled template.</summary>
    public string ResolveBacklogPath(string? overridePath, string demoPath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        var cfg = Load();
        if (!string.IsNullOrWhiteSpace(cfg.BacklogPath) && File.Exists(cfg.BacklogPath))
            return cfg.BacklogPath;

        var walked = BacklogLocator.FindOrNull();
        if (walked is not null) return walked;

        return MaterializeDemo(demoPath).BacklogPath!;
    }
}
